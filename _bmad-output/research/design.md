# Design: KurrentDB Processors

## Context

Stream processing over event stores presents unique challenges: maintaining exactly-once state semantics while allowing horizontal scaling, recovering from failures without data loss, and coordinating distributed checkpoints across autonomous workers.

**Stakeholders:** Developers building event-driven applications, DevOps teams deploying processing pipelines

**Related Documents:**
- `protocol.md` — Complete gRPC protocol specification
- `checkpointing-guide.md` — Progressive guide from single worker to distributed architecture
- `checkpointing-origin.md` — Complete technical design proposal with Flink-equivalent implementation details
- `flink-comparison.md` — Detailed comparison between KurrentDB Processors and Apache Flink
- `parallelism-key-groups.md` — Deep dive into parallelism and key groups model

**Constraints:**
- Single gRPC stream per worker
- No server-hosted execution (we coordinate, not execute)
- Shared state store (technology-agnostic)
- AOT-friendly APIs (no reflection)

## Goals / Non-Goals

**Goals:**

| Priority | Goal | Success Criteria |
|----------|------|------------------|
| P0 | Fault Tolerance | Recover from any worker failure without data loss |
| P0 | Elastic Scaling | Add/remove workers dynamically without restart or data loss |
| P0 | Stateful Processing | Support local state with automatic redistribution on scaling |
| P0 | At-Least-Once Delivery | Every record delivered at least once (duplicates possible after recovery) |
| P0 | Exactly-Once State | State reflects each record exactly once through coordinated checkpoints |
| P1 | Simple API | Single gRPC stream; minimal worker implementation complexity |
| P1 | Observable | Lag metrics, throughput, checkpoint statistics |
| P2 | Pluggable Storage | Checkpoint storage is an abstraction (pluggable implementations) |

**Non-Goals:**
- Operator DAGs (chaining filter→map→aggregate)
- Server-hosted workers (we coordinate, not execute)
- Exactly-once to external systems (worker's responsibility)
- Multi-region replication
- Connection pooling (future work)

---

## Problem 1: Distributing Stateful Work

> **Deep dive:** See `parallelism-key-groups.md` for complete analysis of parallelism and key groups.

### The Problem

In stateful processing, records with the same key must go to the same worker (to access the same state). But we want parallelism.

**Naive approach**: Assign keys directly to workers.
```
Worker A: customers 1-500
Worker B: customers 501-1000
```

**Problem**: When Worker C joins, how do we redistribute? Keys aren't uniform—customer-42 might have 10x more records than customer-999.

### The Solution: Key Groups (Partitions)

Instead of assigning keys to workers, we introduce an intermediate layer: **partitions** (inspired by Flink's "key groups" and Kafka's partitions).

```
Keys → hash(key) % partition_count → Partitions → Workers

customer-123  ─┐
customer-456  ─┼──► Partition 47 ──► Worker A [0-63]
order-abc     ─┤
order-def     ─┴──► Partition 99 ──► Worker B [64-127]
```

**Key properties:**
1. **Fixed partition count**: Set at creation (`max_parallelism`), never changes
2. **Deterministic mapping**: Same key → same partition (always)
3. **Movable partitions**: Partitions move between workers, not individual keys
4. **State per partition**: Each partition has its own state blob

**Why this works:**
- Adding Worker C? Move partitions 86-127 from B to C. That's it.
- State follows partition—access partition 86's state from the shared store, you have all keys that hash to 86.
- No need to track individual keys, just partition assignments.

### Interface: IPartitionAssigner

```csharp
/// <summary>
/// Assigns partitions to workers using consistent hashing.
/// </summary>
public interface IPartitionAssigner {
    /// <summary>
    /// Calculate partition for a given key.
    /// </summary>
    int GetPartition(string key, int maxParallelism);

    /// <summary>
    /// Assign partitions to workers. Called when workers join/leave.
    /// </summary>
    IReadOnlyDictionary<string, int[]> AssignPartitions(
        IReadOnlyList<string> workerIds,
        int maxParallelism);

    /// <summary>
    /// Determine which partitions need to move after worker change.
    /// </summary>
    PartitionMovement CalculateMovement(
        IReadOnlyDictionary<string, int[]> currentAssignment,
        IReadOnlyDictionary<string, int[]> newAssignment);
}

public record PartitionMovement(
    IReadOnlyDictionary<string, int[]> Added,    // worker → partitions gained
    IReadOnlyDictionary<string, int[]> Removed   // worker → partitions lost
);
```

### Interface: IPartitionKeySelector

```csharp
/// <summary>
/// Extracts partition key from a record.
/// </summary>
public interface IPartitionKeySelector {
    /// <summary>
    /// Extract the partition key from a record.
    /// Returns null if record should be skipped.
    /// </summary>
    string? GetKey(Record record);
}

/// <summary>
/// Selector that extracts key using a JSON path expression.
/// Supports paths like:
/// - "stream_id"              → The stream ID
/// - "data.customer_id"       → Field in record data
/// - "metadata.tenant"        → Field in record metadata
/// </summary>
public class JsonPathPartitionKeySelector : IPartitionKeySelector {
    public JsonPathPartitionKeySelector(string jsonPath) { }
    public string? GetKey(Record record) { }
}

/// <summary>
/// Selector that extracts key using a SQL expression evaluated against the record.
/// The record is available as a virtual table with columns:
/// stream_id, position, data (JSON), metadata (JSON), timestamp
///
/// Example: "SELECT JSON_EXTRACT(data, '$.tenant_id') || '-' || JSON_EXTRACT(data, '$.region')"
/// </summary>
public class SqlPartitionKeySelector : IPartitionKeySelector {
    public SqlPartitionKeySelector(string sqlExpression) { }
    public string? GetKey(Record record) { }
}
```

---

## Problem 2: Recovering from Failures

> **Deep dive:** See `checkpointing-guide.md` for progressive guide from single worker to distributed checkpointing.

### The Problem

Worker A is processing records 1000-2000 when it crashes at record 1500.

**Questions:**
- Did records 1000-1500 update external systems? (emails sent, APIs called)
- What state did Worker A have at record 1500?
- How do we resume without losing or duplicating work?

### The Solution: Coordinated Checkpoints

Periodically, all workers snapshot their state and progress at a **consistent point**. On recovery, restore from the last checkpoint.

```
Log positions:    1000 → ... → 1500 → ... → 1750 → ... → 2000
                            ↓                          ↓
                    Checkpoint 1500             Checkpoint 2000
                    (state saved)               (state saved)

                              ↓ 💥 Crash at 1750!

Recovery: Restore from 1500 → Replay 1500-1750
```

**Recovery scenario**: Worker A crashes at record 1750. We restore checkpoint 1500, replay records 1500-1750. Some records processed twice, but state is consistent.

**What gets checkpointed:**
- Position in the log (per partition)
- Worker state (per partition)
- Metadata (timestamp, trigger reason)

**Trade-offs:**
- **Checkpoint often**: Less replay on recovery, more I/O overhead
- **Checkpoint rarely**: More replay on recovery, less overhead
- **Default**: Every 30 seconds or 10,000 records

### Interface: Checkpoint Types

```csharp
/// <summary>
/// A checkpoint represents a consistent snapshot across all partitions.
/// </summary>
public record Checkpoint(
    string ProcessorId,
    long CheckpointId,         // Log position at barrier
    DateTimeOffset CreateTime,
    TimeSpan Duration,         // How long checkpoint took
    bool IsSavepoint,          // Never auto-deleted if true
    IReadOnlyDictionary<int, PartitionCheckpoint> Partitions
);

public record PartitionCheckpoint(
    int Partition,
    long Position,             // Log position for this partition
    ReadOnlyMemory<byte> State,
    IReadOnlyDictionary<string, string> Metadata
);
```

### Interface: ICheckpointStorage

```csharp
/// <summary>
/// Stores and retrieves checkpoint data.
/// </summary>
public interface ICheckpointStorage {
    /// <summary>
    /// Store checkpoint state for partitions.
    /// </summary>
    Task StoreAsync(
        string processorId,
        long checkpointId,
        IEnumerable<PartitionCheckpoint> partitions,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieve checkpoint state for specific partitions.
    /// </summary>
    IAsyncEnumerable<PartitionCheckpoint> RetrieveAsync(
        string processorId,
        long checkpointId,
        IEnumerable<int> partitions,
        CancellationToken ct = default);

    /// <summary>
    /// Delete old checkpoints based on retention policy.
    /// Savepoints are never deleted.
    /// </summary>
    Task ApplyRetentionAsync(
        string processorId,
        int retentionCount,
        CancellationToken ct = default);

    /// <summary>
    /// List checkpoints for a processor.
    /// </summary>
    IAsyncEnumerable<CheckpointSummary> ListAsync(
        string processorId,
        CancellationToken ct = default);
}

public record CheckpointSummary(
    long CheckpointId,
    DateTimeOffset CreateTime,
    TimeSpan Duration,
    long TotalBytes,
    int PartitionCount,
    bool IsSavepoint,
    CheckpointStatus Status
);

public enum CheckpointStatus {
    InProgress,
    Completed,
    Failed
}
```

---

## Problem 3: Scaling Without Downtime

### The Problem

We have 2 workers processing happily. Load increases, we need to add Worker C.

**Challenges:**
- Worker A and B have state in memory
- We need to give C some partitions, with their state
- We can't stop processing for minutes while transferring state

### The Solution: The Hive Mind Model

Workers are autonomous agents that join and leave freely. The server coordinates but doesn't control.

| Traditional Job Manager | KurrentDB Hive Mind |
|------------------------|---------------------|
| Server starts workers | Workers start themselves |
| Server can pause/resume | Workers decide when to leave |
| Fixed parallelism | Dynamic parallelism |
| Manager deploys → Workers (owned) | Processor (concept) ← Workers (autonomous) → Server (coordinate) |

**Scaling workflow (Adding Worker C):**

```
Step 1: Initial State
  Worker A [0-63]  ←→  Server  ←→  Worker B [64-127]

Step 2: Worker C connects
  Server receives Connect from C
  Server triggers checkpoint (barrier to A, B)
  A commits state [0-63], B commits state [64-127] to shared store

Step 3: Rebalance
  Server calculates new assignment:
    A: [0-42], B: [43-85], C: [86-127]
  Server sends WorkAssigned to all

Step 4: State Access
  C claims partitions [86-127] from shared state store (no transfer needed)

Step 5: Resume
  All workers send WorkAccepted
  Processing continues with 3 workers
  Total downtime: ~seconds
```

### Interface: IWorkerSessionManager

```csharp
/// <summary>
/// Manages worker sessions and lifecycle on the server.
/// </summary>
public interface IWorkerSessionManager {
    /// <summary>
    /// Register a new worker connection.
    /// </summary>
    Task<WorkerSession> ConnectAsync(
        string processorId,
        string workerId,
        ProcessorSettings settings,
        CancellationToken ct = default);

    /// <summary>
    /// Mark worker as having accepted work and ready to receive records.
    /// </summary>
    Task WorkAcceptedAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Handle graceful worker disconnect.
    /// </summary>
    Task DisconnectAsync(
        string sessionId,
        DisconnectReason reason,
        CancellationToken ct = default);

    /// <summary>
    /// Handle unexpected worker disconnection (crash, network failure).
    /// </summary>
    Task HandleDisconnectionAsync(string sessionId);

    /// <summary>
    /// Get all active sessions for a processor.
    /// </summary>
    IReadOnlyList<WorkerSession> GetSessions(string processorId);
}

public record WorkerSession(
    string SessionId,           // Unique per connection (detects reconnects)
    string ProcessorId,
    string WorkerId,
    DateTimeOffset ConnectTime,
    WorkerState State,
    IReadOnlyList<int> Partitions,
    IReadOnlyDictionary<int, long> Positions  // partition → log position
);

public enum WorkerState {
    Connecting,    // Just connected, waiting for partition assignment
    Restoring,     // Claiming partitions from shared state store
    Ready,         // Ready to process records
    Processing,    // Actively processing
    Checkpointing, // Responding to barrier
    Disconnecting  // Graceful shutdown in progress
}

public enum DisconnectReason {
    Requested,     // Worker asked to disconnect
    Shutdown,      // Graceful shutdown (SIGTERM)
    Error,         // Worker reported error
    Timeout,       // Checkpoint timeout
    ServerShutdown // Server shutting down
}
```

---

## Problem 4: Coordinating Distributed Checkpoints

> **Deep dive:** See `checkpointing-origin.md` for complete Chandy-Lamport implementation details and Flink-equivalent design.

### The Problem

With multiple workers, how do we get a **consistent** checkpoint? If Worker A checkpoints at position 1000 and Worker B at position 2000, we don't have a coherent snapshot.

### The Solution: Chandy-Lamport Barrier Protocol

Our checkpoint mechanism is based on the **Chandy-Lamport distributed snapshot algorithm** (1985).

**The algorithm** solves capturing a consistent global snapshot without halting operations:
- Processes communicate via message passing
- Channels are FIFO (messages arrive in order sent)
- Channels are reliable (no message loss)
- The graph of processes is strongly connected

**How it applies to KurrentDB Processors:**

| Chandy-Lamport Concept | KurrentDB Implementation |
|------------------------|-----------------------------|
| Marker message | CheckpointBarrier message |
| Process state | Worker state (per partition) |
| Channel state | Not needed (single source log) |
| Initiator | Server (Checkpoint Coordinator) |

**Key insight**: KurrentDB has a simpler topology than general distributed systems. There's a **single source log** that feeds all workers—no complex fan-in of multiple channels. This means we don't need to capture channel state or handle "barrier alignment" (a Flink complexity).

**The barrier flow:**

```
1. Server: checkpoint_id = current_log_position
2. Server broadcasts CheckpointBarrier(checkpoint_id=1000) to all workers
3. Workers (in parallel):
   - Finish current record
   - Commit state to shared store via CommitAsync(checkpointId)
   - Send CheckpointComplete(checkpoint_id=1000, positions)
4. Server: All acks received → Checkpoint 1000 = COMPLETED
```

### Understanding Barrier Positions vs. Worker Positions

**Critical concept**: The barrier position is NOT the position each worker reports. The barrier tells workers *where to stop*, but each worker reports *the last record they actually processed*.

**Example with filtering:**
```
Log positions:    1    2    3    4    5    6    7    8    9    10
Record types:     ORD  SYS  ORD  ORD  SYS  ORD  ORD  SYS  ORD  SYS
Filter:           orders-* (skips SYS records)
Partition (0-1):  0    -    1    0    -    1    0    -    1    -
                  │    │    │    │    │    │    │    │    │    │
Worker 1 (P0):    ✓    ·    ·    ✓    ·    ·    ✓    ·    ·    ·
Worker 2 (P1):    ·    ·    ✓    ·    ·    ✓    ·    ·    ✓    ·
```

**Barrier sent at position 10.**

- Worker 1 processes: positions 1, 4, 7 → reports **position 7**
- Worker 2 processes: positions 3, 6, 9 → reports **position 9**
- Nobody processed position 10 (filtered out as SYS record)

**Checkpoint result:**

| Worker | Partitions | Last Position |
|--------|------------|---------------|
| 1 | [0] | 7 |
| 2 | [1] | 9 |

**Why this matters for recovery**: On recovery, we resume from position 10 (the barrier position)—the **global consistent cut**. Worker positions are metadata for debugging.

### Barriers and Batching

When checkpoint triggers during batch accumulation:

**Our approach: Flush-then-Barrier**
1. **Flush all pending batches** (sends whatever is buffered to each worker)
2. **Send Barrier to all workers**
3. Resume normal batching

Workers never handle checkpoints mid-batch.

### Interface: ICheckpointCoordinator

```csharp
/// <summary>
/// Coordinates checkpoint lifecycle across all workers.
/// Implements the server-side of the Chandy-Lamport algorithm.
/// </summary>
public interface ICheckpointCoordinator {
    /// <summary>
    /// Trigger a checkpoint across all workers.
    /// Broadcasts checkpoint barrier (Chandy-Lamport marker) to all connected workers.
    /// </summary>
    Task<long> TriggerCheckpointAsync(
        string processorId,
        CheckpointTrigger trigger,
        CancellationToken ct = default);

    /// <summary>
    /// Handle worker acknowledgment of checkpoint.
    /// </summary>
    Task AcknowledgeAsync(
        string processorId,
        long checkpointId,
        string sessionId,
        IReadOnlyDictionary<int, long> positions,
        CancellationToken ct = default);

    /// <summary>
    /// Get current checkpoint status.
    /// </summary>
    CheckpointProgress? GetProgress(string processorId, long checkpointId);

    /// <summary>
    /// Get the last completed checkpoint.
    /// </summary>
    long? GetLastCompletedCheckpoint(string processorId);

    /// <summary>
    /// Raised when checkpoint completes successfully.
    /// </summary>
    event EventHandler<CheckpointCompletedEventArgs> CheckpointCompleted;

    /// <summary>
    /// Raised when checkpoint fails.
    /// </summary>
    event EventHandler<CheckpointFailedEventArgs> CheckpointFailed;
}

public record CheckpointProgress(
    long CheckpointId,
    CheckpointTrigger Trigger,
    DateTimeOffset StartTime,
    IReadOnlySet<string> PendingWorkers,
    IReadOnlySet<string> CompletedWorkers,
    IReadOnlySet<string> FailedWorkers
);

public enum CheckpointTrigger {
    Interval,           // Time since last checkpoint
    Threshold,          // Record count threshold
    CaughtUp,           // Reached end of log
    Rebalance,          // Worker membership changed
    Savepoint           // Manual trigger (never auto-deleted)
}
```

---

## Problem 5: Managing Worker State

### The Problem

Workers need to:
1. Store state locally (fast access during processing)
2. Commit state atomically on checkpoints
3. Handle state for multiple partitions (some may be added/removed during rebalancing)

### The Solution: Shared State Store

State is managed through a **shared distributed store** that all workers can access. This eliminates upload/download RPCs—state is committed directly to the shared store on checkpoint, and new workers claim partitions from the same store.

```
Worker Process (Client-Side)
┌─────────────────────────────────────────────┐
│  IRecordProcessor (User Code)               │
│       ↓                                     │
│  ┌─────────────────────────────────┐        │
│  │ Shared State Store              │        │
│  │  ┌───────┬───────┬───────┐     │        │
│  │  │ P0    │ P1    │ P2    │     │        │
│  │  │ state │ state │ state │     │        │
│  │  └───────┴───────┴───────┘     │        │
│  └─────────────────────────────────┘        │
│       ↓ On Barrier: CommitAsync()           │
│  Checkpoint Handler ──► CheckpointComplete ──► Server
└─────────────────────────────────────────────┘
```

**Why shared store (not RPC transfer)?**
- No upload/download latency on rebalancing
- Server not a bottleneck for state
- Simpler protocol (fewer RPCs)
- State always available to any worker
- Enables larger state sizes

### Interface: ISharedStateStore

```csharp
/// <summary>
/// Manages partitioned state in a shared distributed store.
/// State is accessible by all workers - no upload/download needed.
/// </summary>
public interface ISharedStateStore : IAsyncDisposable {
    /// <summary>
    /// Get or create state for a partition.
    /// </summary>
    TState GetState<TState>(int partition) where TState : new();

    /// <summary>
    /// Check if partition has state.
    /// </summary>
    bool HasState(int partition);

    /// <summary>
    /// Commit state changes atomically (called on checkpoint).
    /// Persists all pending state changes to the shared store.
    /// </summary>
    Task CommitAsync(long checkpointId, CancellationToken ct = default);

    /// <summary>
    /// Claim partitions this worker now owns (during rebalancing).
    /// Loads state from the shared store for the claimed partitions.
    /// </summary>
    Task ClaimPartitionsAsync(
        IEnumerable<int> partitions,
        long? checkpointId,
        CancellationToken ct = default);

    /// <summary>
    /// Release partitions when losing ownership (during rebalancing).
    /// Commits any pending changes and releases locks.
    /// </summary>
    Task ReleasePartitionsAsync(
        IEnumerable<int> partitions,
        CancellationToken ct = default);
}
```

---

## Problem 6: Handling Checkpoint Failures

### The Problem

What if a worker doesn't respond to a barrier? Options:
1. Wait forever (blocks all progress)
2. Fail the whole processor (too aggressive?)
3. Remove the slow worker and continue (lose some parallelism)
4. Make it configurable (flexibility)

### The Solution: Configurable Failure Policy

**Policy: CONTINUE (default)**
```
Server sends Barrier to A, B, C
A acks ✓, B acks ✓, C... 60 seconds pass... no response

→ Disconnect C
→ Mark checkpoint FAILED
→ Discard A, B state (incomplete checkpoint)
→ Reassign C's partitions to A, B
→ Resume from last successful checkpoint
```

**Policy: FAIL_PROCESSOR**
```
Server sends Barrier to A, B, C
Any worker doesn't ack in time

→ Disconnect ALL workers
→ Processor goes IDLE
→ Requires manual intervention
```

### Interface: CheckpointSettings

```csharp
/// <summary>
/// Policy for handling checkpoint failures.
/// </summary>
public enum CheckpointFailurePolicy {
    /// <summary>
    /// Disconnect failed workers, continue with remaining workers.
    /// Processing resumes from last successful checkpoint.
    /// </summary>
    Continue,

    /// <summary>
    /// Disconnect all workers, processor goes idle.
    /// Requires manual intervention to resume.
    /// </summary>
    FailProcessor
}

/// <summary>
/// Checkpoint configuration.
/// </summary>
public record CheckpointSettings(
    TimeSpan Interval = default,      // Default: 30s
    int Threshold = 10000,            // Min records between checkpoints
    TimeSpan Timeout = default,       // Default: 60s
    int RetentionCount = 3,           // Checkpoints to keep
    CheckpointFailurePolicy FailurePolicy = CheckpointFailurePolicy.Continue
) {
    public static CheckpointSettings Default => new(
        Interval: TimeSpan.FromSeconds(30),
        Threshold: 10000,
        Timeout: TimeSpan.FromSeconds(60),
        RetentionCount: 3,
        FailurePolicy: CheckpointFailurePolicy.Continue
    );
}
```

---

## Scaling Pattern: Client-Side Parallelism

### The Insight

A common concern with single-stream-per-worker design is head-of-line blocking. **Solution**: Run multiple workers in the same process!

```
Your Process
┌─────────────────────────────────────────────┐
│  Worker A (task 1)  →  Partitions [0-42]    │──┐
│  Worker B (task 2)  →  Partitions [43-85]   │──┼──► Server sees 3 workers
│  Worker C (task 3)  →  Partitions [86-127]  │──┘    (doesn't know they're
└─────────────────────────────────────────────┘       in the same process!)
```

### Why This Works

The "barrier alignment" problem requires alignment when a **single worker** has **multiple input channels**. But here:
- Each local worker has **ONE input channel** (its gRPC stream)
- The server coordinates barriers **across workers** (which it already does!)
- The "alignment" happens server-side, where it's already implemented

### Benefits

| Benefit | Description |
|---------|-------------|
| Parallel TCP connections | No head-of-line blocking between partitions |
| Parallel processing threads | CPU cores fully utilized |
| Independent backpressure | Slow partition doesn't block fast ones |
| No code changes | Server already handles multi-worker coordination |
| Flexible scaling | Choose parallelism at runtime |

### Example Implementation

```csharp
/// <summary>
/// Runs multiple workers in the same process for maximum throughput.
/// </summary>
public class ParallelProcessorHost {
    private readonly string _connectionString;
    private readonly string _processorId;
    private readonly Func<IRecordProcessor> _processorFactory;

    /// <summary>
    /// Run N local workers in parallel.
    /// </summary>
    public async Task RunAsync(int localWorkerCount, CancellationToken ct = default) {
        var tasks = Enumerable
            .Range(0, localWorkerCount)
            .Select(i => RunWorkerAsync(i, ct))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    private async Task RunWorkerAsync(int index, CancellationToken ct) {
        var workerId = $"{Environment.MachineName}-{index}";

        await using var worker = new ProcessorWorkerBuilder()
            .WithConnection(_connectionString)
            .WithStateStore(new SharedStateStore(_connectionString, _processorId))
            .Build();

        await worker.RunAsync(
            processorId: _processorId,
            workerId: workerId,
            processor: _processorFactory(),
            ct: ct);
    }
}

// Usage: 4 machines × 4 local workers = 16 workers
// 128 partitions ÷ 16 workers = 8 partitions each
```

---

## System Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                    SERVER-SIDE (KurrentDB)                       │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                  Processor Service                       │    │
│  │  ┌──────────────┐  ┌───────────────────┐  ┌──────────┐  │    │
│  │  │ Record Reader│  │Checkpoint         │  │ Partition│  │    │
│  │  │              │  │Coordinator        │  │ Assigner │  │    │
│  │  └──────────────┘  └───────────────────┘  └──────────┘  │    │
│  │  ┌──────────────┐  ┌───────────────────┐  ┌──────────┐  │    │
│  │  │Session       │  │Checkpoint Storage │  │ Processor│  │    │
│  │  │Manager       │  │                   │  │Repository│  │    │
│  │  └──────────────┘  └───────────────────┘  └──────────┘  │    │
│  └─────────────────────────────────────────────────────────┘    │
│         ↑                                                        │
│    [$all Log]                                                    │
└─────────────────────────────────────────────────────────────────┘
              ↕ gRPC (bidirectional)
┌─────────────────────────────────────────────────────────────────┐
│                    CLIENT-SIDE (Workers)                         │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                     Worker A                             │    │
│  │  ┌──────────┐  ┌────────────────┐  ┌───────────────┐    │    │
│  │  │gRPC      │  │Checkpoint      │  │ IRecordProcessor│   │    │
│  │  │Client    │  │Handler         │  │ (User Code)   │    │    │
│  │  └──────────┘  └────────────────┘  └───────────────┘    │    │
│  │  ┌──────────────────────────────────────────────────┐   │    │
│  │  │              Shared State Store                   │   │    │
│  │  └──────────────────────────────────────────────────┘   │    │
│  └─────────────────────────────────────────────────────────┘    │
│  [ Worker B ... ]  [ Worker C ... ]                              │
└─────────────────────────────────────────────────────────────────┘
```

### Server-Side Components

| Component | Responsibility |
|-----------|----------------|
| **Record Reader** | Reads from $all, applies filters, routes to workers by partition |
| **Session Manager** | Tracks worker connections, handles connect/disconnect lifecycle |
| **Partition Assigner** | Calculates partition assignments, triggers rebalancing |
| **Checkpoint Coordinator** | Sends barriers, collects acks, manages checkpoint lifecycle |
| **Checkpoint Storage** | Stores/retrieves checkpoint metadata, applies retention |
| **Processor Repository** | Persists processor configuration and state (event-sourced) |

### Client-Side Components

| Component | Responsibility |
|-----------|----------------|
| **ProcessorWorker** | Main entry point; manages connection lifecycle |
| **gRPC Client** | Maintains bidirectional stream, handles reconnection |
| **Shared State Store** | Shared state storage (technology-agnostic) |
| **Checkpoint Handler** | Commits state to shared store on barrier, sends ack |
| **IRecordProcessor** | User-implemented interface for processing records |

---

## Client-Side Component Design

### IRecordProcessor (User-Implemented)

```csharp
/// <summary>
/// Defines the interface for processing records from a processor.
/// All methods except ProcessRecords have default implementations.
/// </summary>
[PublicAPI]
public interface IRecordProcessor {
    /// <summary>
    /// Called when the processor starts, before connecting to the server.
    /// Use for setup: logging, connections, caches, configuration validation.
    /// </summary>
    ValueTask OnInitialize(InitializationContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when records are available for processing.
    /// This is the only method that must be implemented.
    /// </summary>
    ValueTask ProcessRecords(ProcessRecordsContext context);

    /// <summary>
    /// Called when a checkpoint occurs (informational).
    /// The SDK handles state commits to the shared store automatically.
    /// </summary>
    ValueTask OnCheckpoint(CheckpointContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when partition assignment changes (informational).
    /// The SDK handles partition claim/release via shared state store automatically.
    /// </summary>
    ValueTask OnPartitionsAssigned(PartitionsAssignedContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when processing stops, regardless of reason.
    /// </summary>
    ValueTask OnShutdown(ShutdownContext context) => ValueTask.CompletedTask;
}
```

### Fluent Builder API

```csharp
/// <summary>
/// Entry point for the Processors feature.
/// </summary>
public static class KurrentProcessors {
    public static KurrentProcessorBuilder Create(IRecordProcessor processor);
    public static KurrentProcessorBuilder Create(IRecordProcessorFactory factory);
}

/// <summary>
/// Fluent builder for configuring processor execution.
/// </summary>
public sealed class KurrentProcessorBuilder {
    // Connection
    public KurrentProcessorBuilder ConnectTo(string connectionString);
    public KurrentProcessorBuilder WithClient(KurrentClientOptions options);
    public KurrentProcessorBuilder UseClient(KurrentClient client);

    // Identity
    public KurrentProcessorBuilder WithProcessorId(string processorId);
    public KurrentProcessorBuilder WithWorkerId(string workerId);

    // Subscription
    public KurrentProcessorBuilder WithFilter(IRecordFilter filter);
    public KurrentProcessorBuilder WithFilterExpression(string filterExpression);
    public KurrentProcessorBuilder WithPartitionKeySelector(string selector);
    public KurrentProcessorBuilder WithInitialPosition(SubscriptionInitialPosition position);

    // Batching & Checkpointing
    public KurrentProcessorBuilder WithBatching(BatchingSettings settings);
    public KurrentProcessorBuilder WithCheckpoint(CheckpointSettings settings);
    public KurrentProcessorBuilder WithMaxParallelism(int maxParallelism);

    // Execution
    public ValueTask RunAsync(CancellationToken stoppingToken = default);
    public IProcessorWorker Build();
}
```

---

## Decisions Summary

### Decision 1: Single gRPC Bidirectional Stream per Worker

All worker-server communication flows through one bidirectional stream.

**Rationale:** Simplifies connection management, natural backpressure, ordered message delivery required by Chandy-Lamport.

**Trade-offs:** Potential head-of-line blocking. Mitigated by client-side parallelism pattern.

### Decision 2: Barrier Position vs. Worker Positions

Checkpoint barrier position is a logical stopping point; workers report actual last processed position.

**Rationale:** Due to partitioning and filtering, workers see non-contiguous positions.

### Decision 3: Flush-then-Barrier for Batching

Server flushes pending batches then sends barrier.

**Rationale:** Keeps worker logic simple—no mid-batch checkpoint handling.

### Decision 4: Protobuf Edition 2024

Protocol uses Edition 2024 with explicit field presence.

**Rationale:** `HasXxx` properties, default values, UNSPECIFIED enum pattern.

### Decision 5: Settings Immutable After Creation

Processor settings locked at creation.

**Rationale:** Changing max_parallelism invalidates checkpoints.

### Decision 6: Client-Side Parallelism Pattern

Multiple workers can run in same process.

**Rationale:** Eliminates head-of-line blocking between partitions.

### Decision 7: Shared State Store

Default state store is shared across all workers (not RPC-based transfer).

**Rationale:** Eliminates server bottleneck, simpler protocol, enables larger state sizes.

### Decision 8: No Barrier Alignment Required

Unlike Flink, workers don't align barriers across channels.

**Rationale:** Single source log topology—coordination happens server-side.

---

## Alternatives Considered

### Alternative 1: Server-Hosted Workers

**Considered:** Server deploys and manages worker processes.

**Rejected:** Adds complexity, requires container orchestration, users lose deployment flexibility.

### Alternative 2: Barrier Alignment (Flink-style)

**Considered:** Buffer records from channels that received barrier.

**Rejected:** KurrentDB has single source—no fan-in. Adds complexity for no benefit.

### Alternative 3: Incremental Checkpoints

**Considered:** Upload only changed state.

**Deferred:** Adds complexity, state format must support diffing. Full upload initially.

### Alternative 4: Exactly-Once Sinks

**Considered:** Built-in transactional writes to external systems.

**Rejected:** Each sink has different semantics. Worker responsibility.

### Alternative 5: Multiple Input Streams

**Considered:** Allow workers to subscribe to multiple filters.

**Rejected:** Complicates barrier alignment. Single subscription keeps model simple.

### Alternative 6: RPC-Based State Transfer

**Considered:** Workers upload/download state via server RPCs.

**Rejected:** Server becomes bottleneck, adds latency on rebalancing, limits state size.

---

## Risks / Trade-offs

| Risk | Mitigation |
|------|------------|
| Large state slows checkpoints | Shared store; future incremental checkpoints |
| Worker crash during checkpoint | Configurable failure policy |
| Network partition during rebalance | Timeout-based detection |
| Hot partitions (skewed keys) | Monitor partition lag metrics |
| Checkpoint storage failure | Retry with backoff, resume from previous |

---

## Non-Functional Requirements

### Performance Targets

| Metric | Target |
|--------|--------|
| Checkpoint latency (p99) | < 100ms for state < 1MB |
| Record delivery latency | < 10ms from write to worker receipt |
| Rebalance time | < 5s for partition reassignment |

### Scalability Targets

| Metric | Target |
|--------|--------|
| Workers per processor | Up to 1000 |
| Partitions per processor | Up to 32,768 |
| Records per second (aggregate) | 100,000+ depending on worker capacity |

### Reliability Targets

| Metric | Target |
|--------|--------|
| Checkpoint success rate | > 99.9% under normal conditions |
| Recovery time objective (RTO) | < 30s from failure detection to resumed processing |
| Data loss | Zero (at-least-once guarantee) |

---

## References

### Research Documents (This Project)

| Document | Description |
|----------|-------------|
| `../contracts/protocol.md` | Complete gRPC protocol specification with proto definitions |
| `checkpointing-guide.md` | Progressive guide from single worker to distributed architecture |
| `checkpointing-origin.md` | Complete technical design with Flink-equivalent implementation |
| `flink-comparison.md` | Detailed comparison with Apache Flink |
| `parallelism-key-groups.md` | Deep dive into parallelism and key groups model |

### Academic Papers

1. Chandy, K.M. and Lamport, L. (1985). "Distributed Snapshots: Determining Global States of Distributed Systems". ACM TOCS, 3(1), 63-75. [DOI:10.1145/214451.214456](https://doi.org/10.1145/214451.214456)

2. Carbone, P., et al. (2015). "Lightweight Asynchronous Snapshots for Distributed Dataflows". [arXiv:1506.08603](https://arxiv.org/abs/1506.08603)

### External References

- [Apache Flink Checkpointing](https://nightlies.apache.org/flink/flink-docs-stable/docs/ops/state/checkpoints/)
- [Flink Reactive Mode (1.13)](https://flink.apache.org/2021/05/03/announcing-the-release-of-apache-flink-1.13/)
- [Apache Kafka Consumer Groups](https://kafka.apache.org/documentation/#consumerconfigs)

---

## Open Questions

- [ ] Should watermarks and event-time processing be added? (Future work)
- [ ] Should incremental checkpoints be supported? (Future work)
- [ ] Should server-hosted workers be an optional mode? (Future work)
