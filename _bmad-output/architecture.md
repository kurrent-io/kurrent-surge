---
version: '1.0'
date: '2025-12-25'
status: 'Draft'
author: 'Sergio'
based_on:
  - .project/specs/001-processors/plan.md (v2.5)
  - openspec/changes/add-processors/contracts/protocol.md (v1.9)
---

# Kurrent Processors - Technical Architecture

## Table of Contents

1. [Overview](#1-overview)
2. [Core Concepts](#2-core-concepts)
3. [Partitioning](#3-partitioning)
4. [Checkpointing](#4-checkpointing)
5. [Rebalancing](#5-rebalancing)
6. [State Management](#6-state-management)
7. [Server-Side Design](#7-server-side-design)
8. [Client-Side Design](#8-client-side-design)
9. [Message Flows](#9-message-flows)
10. [Observability](#10-observability)

---

## 1. Overview

### 1.1 The Hive Mind Model

KurrentDB Processors use a **Hive Mind** architecture where:

- Workers are **autonomous agents** that decide when to join and leave
- The **processor emerges** from workers coordinating through the server
- Server **coordinates but doesn't control** worker execution
- Similar to Apache Flink's Reactive Mode (2021)

```
┌─────────────────────────────────────────────────────────────┐
│                      HIVE MIND MODEL                         │
│                                                              │
│    ┌─────────┐     ┌─────────┐     ┌─────────┐             │
│    │Worker A │     │Worker B │     │Worker C │             │
│    │   🧠    │     │   🧠    │     │   🧠    │  Autonomous │
│    └────┬────┘     └────┬────┘     └────┬────┘             │
│         │               │               │                   │
│         └───────────────┼───────────────┘                   │
│                         │                                   │
│                   ┌─────┴─────┐                             │
│                   │  Server   │  Coordinate                 │
│                   │ (KurrentDB)│                            │
│                   └───────────┘                             │
│                                                              │
│    Processor = emergent behavior from worker coordination    │
└─────────────────────────────────────────────────────────────┘
```

### 1.2 Two-Sided Architecture

| Side | Components | Responsibility |
|------|------------|----------------|
| **Server** | Record Reader, Checkpoint Coordinator, Partition Assigner | Coordination, record routing, checkpoint tracking |
| **Client** | ProcessorWorker, Checkpoint Handler, State Store | Processing, state management, checkpoint execution |

---

## 2. Core Concepts

### 2.1 Glossary

| Term | Definition |
|------|------------|
| **Processor** | Logical grouping of workers with same processor_id. Emerges when workers connect. |
| **Worker** | Autonomous process that connects to process records. Decides when to join/leave. |
| **Partition** | Subdivision of key space (0 to max_parallelism-1). Records with same key → same partition. |
| **Checkpoint** | Consistent snapshot of all workers' positions. Enables recovery after failures. |
| **Savepoint** | Manually-triggered checkpoint that is never auto-deleted. |
| **Barrier** | Marker message triggering checkpoint (Chandy-Lamport algorithm). |
| **Shared State Store** | Technology-agnostic storage accessible by all workers for state. |

### 2.2 Processing Guarantees

| Guarantee | Description |
|-----------|-------------|
| **At-Least-Once Delivery** | Every record delivered at least once. Duplicates possible after recovery. |
| **Exactly-Once State** | State reflects each record exactly once through coordinated checkpoints. |

---

## 3. Partitioning

### 3.1 The Problem

In stateful processing, records with the same key must go to the same worker (to access the same state). How do you parallelize without losing consistency?

### 3.2 The Solution: Key Groups (Partitions)

Instead of assigning keys directly to workers, we introduce **partitions**:

```
Keys               hash(key) % N        Workers
─────────────────────────────────────────────────
customer-123  ───►  Partition 47  ───►  Worker A (0-63)
customer-456  ───►  Partition 47  ───►  Worker A (0-63)
order-abc     ───►  Partition 99  ───►  Worker B (64-127)
order-def     ───►  Partition 99  ───►  Worker B (64-127)
```

### 3.3 Key Properties

| Property | Description |
|----------|-------------|
| **Fixed partition count** | Set at creation (`max_parallelism`), never changes |
| **Deterministic mapping** | Same key → same partition (always) |
| **Movable partitions** | Partitions move between workers, not individual keys |
| **State per partition** | Each partition has its own state in shared store |

### 3.4 Partition Assignment

```csharp
public interface IPartitionAssigner {
    /// <summary>
    /// Calculate partition for a given key.
    /// </summary>
    int GetPartition(string key, int maxParallelism);

    /// <summary>
    /// Assign partitions to workers using consistent hashing.
    /// Minimizes redistribution when workers change.
    /// </summary>
    IReadOnlyDictionary<string, int[]> AssignPartitions(
        IReadOnlyList<string> workerIds,
        int maxParallelism);
}
```

---

## 4. Checkpointing

### 4.1 The Problem

With multiple workers, how do we get a **consistent** checkpoint? If Worker A checkpoints at position 1000 and Worker B at position 2000, we don't have a coherent snapshot.

### 4.2 The Solution: Chandy-Lamport Barrier Protocol

Our checkpoint mechanism is based on the **Chandy-Lamport distributed snapshot algorithm** (1985).

**Key insight**: KurrentDB has a simpler topology than general distributed systems. There's a **single source log** that feeds all workers—no complex fan-in of multiple channels.

### 4.3 Checkpoint Flow

```
┌──────────┐                              ┌──────────┐
│  Server  │                              │ Worker(s)│
└────┬─────┘                              └────┬─────┘
     │                                         │
     │  Checkpoint triggered (interval/        │
     │  threshold/rebalance/savepoint)         │
     │                                         │
     │──── CheckpointBarrier(id, trigger) ────►│
     │                                         │
     │                        ┌────────────────┤
     │                        │ Commit state   │
     │                        │ to shared store│
     │                        └────────────────┤
     │                                         │
     │◄─── CheckpointComplete(id, positions) ──│
     │                                         │
     │  [All workers completed?]               │
     │  [Mark checkpoint COMPLETE]             │
     │                                         │
```

### 4.4 Checkpoint Triggers

| Trigger | Description | Auto-Deleted |
|---------|-------------|--------------|
| `INTERVAL` | Time elapsed since last checkpoint | Yes |
| `THRESHOLD` | Record count threshold reached | Yes |
| `CAUGHT_UP` | Subscription reached end of log | Yes |
| `REBALANCE` | Worker membership changed | Yes |
| `SAVEPOINT` | User-triggered via RPC | **No** |

### 4.5 Understanding Barrier Positions vs. Worker Positions

The barrier position is a **logical stopping point** in the log. Workers process records up to (but not beyond) this position. Each worker reports the **last position they actually processed**.

**Example with filtering:**

```
Log positions:    1    2    3    4    5    6    7    8    9    10
Record types:     ORD  SYS  ORD  ORD  SYS  ORD  ORD  SYS  ORD  SYS
Filter:           orders-* (skips SYS records)
Partition keys:   A    -    B    A    -    B    A    -    B    -

Worker 1 (A):     ✓    ·    ·    ✓    ·    ·    ✓    ·    ·    ·
Worker 2 (B):     ·    ·    ✓    ·    ·    ✓    ·    ·    ✓    ·

Barrier sent at position 10.

Worker 1 reports: position 7 (last A record processed)
Worker 2 reports: position 9 (last B record processed)
```

### 4.6 Checkpoint Settings

```protobuf
message CheckpointSettings {
    int64 interval_ms = 1 [default = 30000];      // 30 seconds
    int32 threshold = 2 [default = 10000];        // 10K records
    int64 timeout_ms = 3 [default = 60000];       // 60 seconds
    int32 retention_count = 4 [default = 3];      // Keep 3 checkpoints
    CheckpointFailurePolicy failure_policy = 5;   // What to do on failure
}
```

### 4.7 Checkpoint Failure Policy

| Policy | Behavior |
|--------|----------|
| `CONTINUE` | Disconnect failed worker, continue with remaining workers. Resume from last successful checkpoint. |
| `FAIL_PROCESSOR` | Disconnect all workers, processor goes idle. Requires manual intervention. |

---

## 5. Rebalancing

### 5.1 Worker Join Flow

```
┌──────────┐      ┌──────────┐      ┌──────────┐
│ Worker 1 │      │  Server  │      │ Worker 2 │
│(existing)│      │          │      │  (new)   │
└────┬─────┘      └────┬─────┘      └────┬─────┘
     │                 │                 │
     │                 │◄── Connect ─────│
     │                 │                 │
     │  Checkpoint before rebalance      │
     │◄── CheckpointBarrier ─────────────│
     │                 │                 │
     │  Commit state to shared store     │
     │                 │                 │
     │─── CheckpointComplete ──────────►│
     │                 │                 │
     │  Redistribute partitions          │
     │                 │                 │
     │◄── WorkAssigned([0-63]) ──────────│
     │                 │                 │
     │                 │─ WorkAssigned([64-127]) ─►│
     │                 │                 │
     │                 │  Claim partitions from    │
     │                 │  shared store             │
     │                 │                 │
     │                 │◄── WorkAccepted │
     │─── WorkAccepted ───────────────►│
     │                 │                 │
     │  Resume processing                │
```

### 5.2 Worker Leave Flow

```
┌──────────┐      ┌──────────┐      ┌──────────┐
│ Worker 1 │      │  Server  │      │ Worker 2 │
│          │      │          │      │ (leaving)│
└────┬─────┘      └────┬─────┘      └────┬─────┘
     │                 │                 │
     │                 │◄── Disconnect ──│
     │                 │                 │
     │  Checkpoint before rebalance      │
     │◄── CheckpointBarrier ─────────────│
     │                 │─ CheckpointBarrier ─────►│
     │                 │                 │
     │  Commit state   │  Commit state   │
     │                 │                 │
     │── CheckpointComplete ──────────►│
     │                 │◄── CheckpointComplete ──│
     │                 │                 │
     │                 │── Disconnected ────────►│
     │                 │                 │
     │  Redistribute W2's partitions     │
     │                 │                 │
     │◄── WorkAssigned([0-127]) ─────────│
     │                 │                 │
     │  Claim partitions 64-127          │
     │  from shared store                │
     │                 │                 │
     │─── WorkAccepted ───────────────►│
```

---

## 6. State Management

### 6.1 Shared State Store Model

Workers commit state **directly to a shared store** (technology-agnostic). This eliminates RPC-based state transfer:

| Approach | Old (RPC-based) | New (Shared Store) |
|----------|-----------------|---------------------|
| **Checkpoint** | Worker → Snapshot → Upload via RPC → Server stores | Worker → Commit directly to shared store |
| **Recovery** | Server → Download via RPC → Worker restores | Worker → Claim from shared store |
| **Rebalance** | Transfer state bytes via server | Claim/release partitions in store |

### 6.2 State Store Interface

```csharp
/// <summary>
/// Technology-agnostic interface for shared state storage.
/// All workers can access this store directly.
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
    /// Commit current state for checkpoint.
    /// Called when barrier arrives.
    /// </summary>
    Task CommitAsync(long checkpointId, CancellationToken ct = default);

    /// <summary>
    /// Claim partitions from shared store.
    /// Called when WorkAssigned received.
    /// </summary>
    Task ClaimPartitionsAsync(
        IEnumerable<int> partitions,
        long? checkpointId,
        CancellationToken ct = default);

    /// <summary>
    /// Release partitions back to shared store.
    /// Called when partitions are reassigned away.
    /// </summary>
    Task ReleasePartitionsAsync(
        IEnumerable<int> partitions,
        CancellationToken ct = default);
}
```

### 6.3 Checkpoint Flow with Shared Store

```
1. Server sends CheckpointBarrier(checkpoint_id=1000, trigger=INTERVAL)
2. Worker receives barrier
3. Worker commits state: await stateStore.CommitAsync(1000)
4. Worker sends CheckpointComplete(checkpoint_id=1000, positions={0: 950, 1: 980})
5. Server waits for ALL workers to complete
6. Server marks checkpoint 1000 as COMPLETED
```

---

## 7. Server-Side Design

### 7.1 Components

```
┌─────────────────────────────────────────────────────────────┐
│                    PROCESSOR SERVICE                         │
│                                                              │
│  ┌─────────────┐  ┌──────────────┐  ┌───────────────────┐  │
│  │   Record    │  │  Checkpoint  │  │    Partition      │  │
│  │   Reader    │  │  Coordinator │  │    Assigner       │  │
│  └──────┬──────┘  └──────┬───────┘  └────────┬──────────┘  │
│         │                │                    │             │
│         │         ┌──────┴───────┐            │             │
│         │         │   Session    │            │             │
│         └────────►│   Manager    │◄───────────┘             │
│                   └──────┬───────┘                          │
│                          │                                  │
│                   ┌──────┴───────┐                          │
│                   │  Processor   │  Event-sourced from      │
│                   │  Repository  │  $processors-{id} stream │
│                   └──────────────┘                          │
└─────────────────────────────────────────────────────────────┘
```

### 7.2 Processor Aggregate (Event-Sourced)

```csharp
public class Processor {
    public string ProcessorId { get; }
    public ProcessorSettings Settings { get; }
    public ProcessorState State { get; }
    public IReadOnlyDictionary<string, WorkerSession> Workers { get; }
    public long? LastCheckpointId { get; }

    // Event sourcing
    public void Apply(ProcessorCreated evt);
    public void Apply(WorkerConnected evt);
    public void Apply(WorkerDisconnected evt);
    public void Apply(CheckpointStarted evt);
    public void Apply(CheckpointCompleted evt);
    public void Apply(CheckpointFailed evt);
    public void Apply(RebalanceStarted evt);
    public void Apply(RebalanceCompleted evt);
}

public enum ProcessorState {
    Idle,          // No workers connected
    Running,       // Processing records
    Checkpointing, // Waiting for barrier acks
    Rebalancing    // Redistributing partitions
}
```

---

## 8. Client-Side Design

### 8.1 IRecordProcessor Interface

```csharp
/// <summary>
/// User-implemented interface for processing records.
/// Only ProcessRecords is required.
/// </summary>
public interface IRecordProcessor {
    /// <summary>
    /// Called when processor starts, before connecting.
    /// </summary>
    ValueTask OnInitialize(InitializationContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when records are available. REQUIRED.
    /// </summary>
    ValueTask ProcessRecords(ProcessRecordsContext context);

    /// <summary>
    /// Called when checkpoint occurs (informational).
    /// </summary>
    ValueTask OnCheckpoint(CheckpointContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when partition assignment changes (informational).
    /// </summary>
    ValueTask OnPartitionsAssigned(PartitionsAssignedContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when processing stops.
    /// </summary>
    ValueTask OnShutdown(ShutdownContext context) => ValueTask.CompletedTask;
}
```

### 8.2 Fluent Builder API

```csharp
// Simple usage
await KurrentProcessors.Create(new MyProcessor())
    .ConnectTo("kurrentdb://localhost:2113")
    .WithProcessorId("order-processor")
    .WithFilter(RecordFilter.ByStreamPrefix("orders-"))
    .RunAsync(stoppingToken);

// Advanced usage with worker access
var worker = KurrentProcessors.Create(new MyProcessor())
    .ConnectTo("kurrentdb://localhost:2113")
    .WithProcessorId("order-processor")
    .WithMaxParallelism(256)
    .WithCheckpoint(new CheckpointSettings { IntervalMs = 60000 })
    .Build();

Console.WriteLine($"State: {worker.State}");
await worker.RunAsync(stoppingToken);
Console.WriteLine($"Processed: {worker.Metrics.TotalRecordsProcessed}");
```

### 8.3 Position Tracking

The SDK tracks which positions each partition has processed:

```csharp
internal interface IWorkerPositionTracker {
    RecordPosition[] TrackedPositions { get; }
    RecordPosition LastFlushedPosition { get; }

    void Track(params RecordPosition[] positions);
    RecordPosition[] FlushUpTo(RecordPosition barrierPosition);
}
```

**Example:**

| Step | Action | Tracker State | Last Flushed |
|------|--------|---------------|--------------|
| 1 | Process pos 1, Track(1) | [1] | (none) |
| 2 | Process pos 3, Track(3) | [1, 3] | (none) |
| 3 | Process pos 5, Track(5) | [1, 3, 5] | (none) |
| 4 | **Barrier(8)** arrives | | |
| | FlushUpTo(8) → [1,3,5] | [] | 5 |
| | CheckpointComplete(positions={partition: 5}) | | |

---

## 9. Message Flows

### 9.1 Normal Processing

```
Worker                                    Server
   │                                        │
   │─── Connect(processor_id, settings) ───►│
   │                                        │
   │◄── WorkAssigned(partitions, ckpt_id) ──│
   │                                        │
   │  [Claim partitions from shared store]  │
   │                                        │
   │─── WorkAccepted ─────────────────────►│
   │                                        │
   │◄── Records([r1, r2, r3]) ──────────────│
   │                                        │
   │  [Process records, update state]       │
   │                                        │
   │◄── Records([r4, r5]) ──────────────────│
   │                                        │
   │  ... processing continues ...          │
```

### 9.2 Checkpoint

```
Worker                                    Server
   │                                        │
   │  [Checkpoint triggered]                │
   │                                        │
   │◄── CheckpointBarrier(id=100, INTERVAL) │
   │                                        │
   │  [Commit state to shared store]        │
   │                                        │
   │─── CheckpointComplete(id=100, pos) ───►│
   │                                        │
   │  [Server: all workers acked?]          │
   │  [Server: mark checkpoint COMPLETE]    │
```

### 9.3 Graceful Disconnect

```
Worker                                    Server
   │                                        │
   │─── Disconnect(SHUTDOWN) ─────────────►│
   │                                        │
   │◄── CheckpointBarrier(id=101, REBALANCE)│
   │                                        │
   │  [Commit final state]                  │
   │                                        │
   │─── CheckpointComplete(id=101, pos) ───►│
   │                                        │
   │◄── Disconnected(REQUESTED) ────────────│
   │                                        │
   │  [Connection closed]                   │
```

---

## 10. Observability

### 10.1 Lag Metrics

| Metric | Formula | Use Case |
|--------|---------|----------|
| `lag_records` | COUNT(*) WHERE position > current | Alerting |
| `lag_bytes` | head_position - current_position | Capacity |
| `lag_time` | head_timestamp - current_timestamp | Freshness |

### 10.2 OTEL Metrics

**Server-side:**

| Metric | Type | Description |
|--------|------|-------------|
| `kurrentdb.processor.records.processed` | Counter | Total records |
| `kurrentdb.processor.checkpoint.count` | Counter | Checkpoints by status |
| `kurrentdb.processor.checkpoint.duration` | Histogram | Checkpoint duration |
| `kurrentdb.processor.workers.count` | Gauge | Connected workers |

**Client-side:**

| Metric | Type | Description |
|--------|------|-------------|
| `kurrentdb.worker.records.processed` | Counter | Records by worker |
| `kurrentdb.worker.lag.records` | Gauge | Record lag |
| `kurrentdb.worker.lag.bytes` | Gauge | Byte lag |
| `kurrentdb.worker.lag.time` | Gauge | Time lag |

---

## Appendix A: Client-Side Parallelism Pattern

Multiple workers can run in the same process for maximum throughput:

```csharp
public class ParallelProcessorHost {
    public async Task RunAsync(int localWorkerCount, CancellationToken ct) {
        var tasks = Enumerable
            .Range(0, localWorkerCount)
            .Select(i => RunWorkerAsync(i, ct))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    private async Task RunWorkerAsync(int index, CancellationToken ct) {
        var workerId = $"{Environment.MachineName}-{index}";

        await KurrentProcessors.Create(new MyProcessor())
            .ConnectTo(_connectionString)
            .WithProcessorId(_processorId)
            .WithWorkerId(workerId)
            .RunAsync(ct);
    }
}

// Usage: 4 machines × 4 local workers = 16 workers
// 128 partitions ÷ 16 workers = 8 partitions each
```

**Benefits:**
- Parallel TCP connections (no head-of-line blocking)
- CPU cores fully utilized
- Independent backpressure per worker
- Server handles coordination automatically
