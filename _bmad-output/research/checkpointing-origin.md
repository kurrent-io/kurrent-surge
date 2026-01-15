# Distributed Checkpointing System
## A Flink-Equivalent Implementation for .NET 10 with gRPC

**Version:** 1.0.0
**Status:** Technical Design Proposal
**Target Framework:** .NET 10 / C# 14
**Source:** Preserved from `.project/specs/001-processors/research/checkpointing-origin.md`

> **Note:** This document is a condensed version of the original 50k+ token specification. See the original file for complete implementation details, sequence diagrams, and code samples.

---

## 1. Executive Summary

This document proposes a distributed checkpointing system for .NET 10 that replicates the power and reliability of Apache Flink's checkpoint mechanism. The system enables exactly-once processing semantics in distributed stream processing applications through asynchronous barrier snapshotting, consistent distributed snapshots, and fault-tolerant state management.

### Key Design Goals

- **Exactly-Once Semantics**: Guarantee each record is processed exactly once, even during failures
- **Low-Latency Checkpointing**: Asynchronous snapshotting that minimizes impact on processing throughput
- **Pluggable Architecture**: Extensible state backends, storage providers, and serialization
- **gRPC-Native**: First-class gRPC support for all coordination and communication
- **Cloud-Native**: Designed for Kubernetes, containerized deployments, and cloud storage
- **Reusability**: Framework-agnostic core that can power multiple stream processing systems

---

## 2. Apache Flink's Checkpointing System

### 2.1 Why Flink's Checkpointing is Revolutionary

Apache Flink's checkpointing mechanism represents one of the most sophisticated approaches to fault tolerance in distributed stream processing.

#### The Fundamental Problem

In distributed stream processing, data flows through a directed acyclic graph (DAG) of operators spread across multiple machines. When a failure occurs, we need to:

1. **Not lose any data** — every record must eventually be processed
2. **Not duplicate processing** — no record should affect output more than once
3. **Recover quickly** — minimize downtime and data replay
4. **Not stop the world** — checkpointing shouldn't halt processing

#### The Chandy-Lamport Foundation

Flink's checkpointing is based on the Chandy-Lamport algorithm for distributed snapshots, published in 1985. The key insight is profound: **you can capture a consistent global state of a distributed system without stopping it, by using markers that flow through communication channels.**

A "consistent cut" divides the timeline of each process into "before" and "after" the snapshot. The cut is consistent if: whenever a message receipt is in the "after" portion, the message send is also in the "after" portion.

#### Asynchronous Barrier Snapshotting (ABS)

Flink adapts Chandy-Lamport into Asynchronous Barrier Snapshotting:

```
┌─────────────────────────────────────────────────────────────────────┐
│                    Data Stream with Barriers                         │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ──●──●──●──║──●──●──●──║──●──●──●──║──●──●──●──►                    │
│            ▲           ▲           ▲                                 │
│      Barrier n   Barrier n+1  Barrier n+2                           │
│                                                                      │
│  ● = Data Record                                                     │
│  ║ = Checkpoint Barrier                                              │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

**How it works:**

1. The **CheckpointCoordinator** triggers a checkpoint by injecting barriers into source operators
2. Barriers flow downstream through the DAG like regular records
3. When an operator receives barriers from **all** input channels, it snapshots its state
4. After snapshotting, the operator forwards the barrier downstream
5. Operators acknowledge completion to the coordinator
6. When all acknowledgments arrive, the checkpoint is complete

### 2.2 Barrier Alignment: The Key to Exactly-Once

For operators with multiple inputs, **barrier alignment** ensures consistency:

```
┌──────────────────────────────────────────────────────────────────────┐
│                     Barrier Alignment Process                         │
├──────────────────────────────────────────────────────────────────────┤
│                                                                       │
│  Input 1: ──●──●──║──●──●──●──►                                       │
│                  ▲                                                    │
│                  │ Barrier arrives, buffer subsequent records        │
│                                                                       │
│  Input 2: ──●──●──●──●──║──●──►                                       │
│                       ▲                                              │
│                       │ Barrier arrives, trigger snapshot            │
│                                                                       │
│  Operator:                                                           │
│  1. Receive barrier on Input 1                                       │
│  2. Block Input 1, buffer arriving records                           │
│  3. Continue processing Input 2                                      │
│  4. Receive barrier on Input 2                                       │
│  5. Snapshot state (all pre-barrier records processed)               │
│  6. Emit barrier downstream                                          │
│  7. Unblock Input 1, process buffered records                        │
│                                                                       │
└──────────────────────────────────────────────────────────────────────┘
```

**KurrentDB Simplification:** We don't need barrier alignment because we have a single source log ($all). Each worker has exactly one input channel.

### 2.3 Unaligned Checkpoints

Flink 1.11 introduced **unaligned checkpoints** for high-backpressure scenarios:

| Aspect | Aligned | Unaligned |
|--------|---------|-----------|
| Checkpoint Duration | Longer (waits for alignment) | Shorter (immediate) |
| Checkpoint Size | Smaller (state only) | Larger (state + in-flight data) |
| Backpressure Impact | Significant | Minimal |
| Recovery Time | Faster (less to replay) | Slower (more to restore) |

### 2.4 Incremental Checkpoints

For large state, Flink supports **incremental checkpoints** with RocksDB:

```
Checkpoint N:     [Full State Snapshot]
Checkpoint N+1:   [Delta 1]
Checkpoint N+2:   [Delta 1][Delta 2]
Checkpoint N+3:   [Compacted Snapshot] (periodic consolidation)

Benefits:
• Checkpoint N=10GB, but Delta=100MB
• 100x reduction in checkpoint I/O
• Enables checkpointing of TB-scale state
```

---

## 3. Core Concepts and Theoretical Foundation

### 3.1 Consistent Cuts and Global Snapshots

A **consistent global snapshot** captures the state of a distributed system at a logical instant:

```
Process 1:  ────●────●────●────●────●────►
                   │    ▲
                   │    │ message m
                   │    │
Process 2:  ────●────●────●────●────●────►
                        │
              ─ ─ ─ ─ ─ ┼ ─ ─ ─ ─ ─ ─   Consistent Cut

✓ Message m: send before cut, receive before cut
```

### 3.2 Checkpoint Semantics

| Guarantee | Description | How Achieved |
|-----------|-------------|--------------|
| **At-Most-Once** | Records may be lost, never duplicated | Acknowledge immediately, no replay |
| **At-Least-Once** | Records never lost, may be duplicated | Replay from checkpoint, no alignment |
| **Exactly-Once** | Records never lost, never duplicated | Barrier alignment + idempotent sinks |

### 3.3 Failure Model

The system handles these failure scenarios:

| Scenario | Detection | Recovery |
|----------|-----------|----------|
| Worker Process Crash | Heartbeat timeout | Restore from last checkpoint |
| Network Partition | gRPC deadline exceeded | Checkpoint timeout, abort |
| Coordinator Failure | Leader election timeout | New leader, resume from checkpoint metadata |
| Storage Failure | I/O errors | Retry with backoff, fall back to previous |
| Checkpoint Timeout | Duration exceeds threshold | Abort checkpoint, next scheduled |

---

## 4. System Architecture

### 4.1 High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                 Distributed Checkpointing System                     │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │                    Control Plane (gRPC)                      │    │
│  │  ┌─────────────────────────────────────────────────────┐    │    │
│  │  │           Checkpoint Coordinator Cluster             │    │    │
│  │  │  ┌─────────┐  ┌─────────┐  ┌─────────┐             │    │    │
│  │  │  │ Leader  │  │Follower │  │Follower │             │    │    │
│  │  │  └────┬────┘  └─────────┘  └─────────┘             │    │    │
│  │  │       │ Raft/Paxos for leader election              │    │    │
│  │  └───────┼─────────────────────────────────────────────┘    │    │
│  │          │ gRPC (TriggerCheckpoint, Acknowledge)            │    │
│  │          ▼                                                   │    │
│  └──────────┼───────────────────────────────────────────────────┘    │
│             │                                                        │
│  ┌──────────┼────────────────────────────────────────────────────┐   │
│  │          │                Data Plane                           │   │
│  │          ▼                                                     │   │
│  │  ┌─────────────────────────────────────────────────────────┐  │   │
│  │  │                   Worker Cluster                         │  │   │
│  │  │  ┌──────────┐    ┌──────────┐    ┌──────────┐          │  │   │
│  │  │  │ Worker 1 │───►│ Worker 2 │───►│ Worker 3 │          │  │   │
│  │  │  │ [State]  │    │ [State]  │    │ [State]  │          │  │   │
│  │  │  └──────────┘    └──────────┘    └──────────┘          │  │   │
│  │  └─────────┬───────────────┬───────────────┬───────────────┘  │   │
│  │            │               │               │                   │   │
│  └────────────┼───────────────┼───────────────┼───────────────────┘   │
│               │               │               │                       │
│  ┌────────────┼───────────────┼───────────────┼───────────────────┐   │
│  │            ▼               ▼               ▼                   │   │
│  │  ┌─────────────────────────────────────────────────────────┐  │   │
│  │  │                 Checkpoint Storage                       │  │   │
│  │  │   ┌─────────┐  ┌─────────┐  ┌─────────┐                │  │   │
│  │  │   │  S3 /   │  │  Azure  │  │  Local  │                │  │   │
│  │  │   │  GCS    │  │   Blob  │  │   Disk  │                │  │   │
│  │  │   └─────────┘  └─────────┘  └─────────┘                │  │   │
│  │  │                                                         │  │   │
│  │  │   /checkpoints/{job-id}/{checkpoint-id}/                │  │   │
│  │  │   ├── _metadata                                         │  │   │
│  │  │   ├── task-{id}-state                                  │  │   │
│  │  │   └── shared/                                           │  │   │
│  │  └─────────────────────────────────────────────────────────┘  │   │
│  │                       Storage Plane                            │   │
│  └────────────────────────────────────────────────────────────────┘   │
│                                                                       │
└───────────────────────────────────────────────────────────────────────┘
```

### 4.2 Layered Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                       Application Layer                              │
│  • Stream Processing DSL                                            │
│  • Operator Definitions                                             │
│  • User-Defined Functions                                           │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      Coordination Layer                              │
│  • CheckpointCoordinator                                            │
│  • CheckpointTracker                                                │
│  • FailureDetector                                                  │
│  • RecoveryManager                                                  │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        Execution Layer                               │
│  • TaskExecutor                                                     │
│  • BarrierHandler                                                   │
│  • CheckpointableTask                                               │
│  • StateManager                                                     │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                         State Layer                                  │
│  • StateBackend (InMemory, RocksDB, Custom)                        │
│  • KeyedStateStore                                                  │
│  • OperatorStateStore                                               │
│  • BroadcastStateStore                                              │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        Storage Layer                                 │
│  • CheckpointStorage (FileSystem, S3, Azure Blob, GCS)             │
│  • StateSerializer                                                  │
│  • CompactionManager                                                │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      Communication Layer                             │
│  • gRPC Services                                                    │
│  • Barrier Channels                                                 │
│  • Acknowledgment Channels                                          │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 5. Component Design

### 5.1 Checkpoint Coordinator

The central orchestrator responsible for triggering and tracking checkpoints.

```csharp
/// <summary>
/// Core responsibilities:
/// 1. Trigger checkpoints on schedule or on-demand
/// 2. Track acknowledgments from all tasks
/// 3. Manage checkpoint lifecycle (pending → completed/failed)
/// 4. Coordinate recovery on failure
/// 5. Manage checkpoint retention policies
/// </summary>
public interface ICheckpointCoordinator
{
    // Lifecycle
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);

    // Checkpoint Operations
    ValueTask<CheckpointTriggerResult> TriggerCheckpointAsync(
        CheckpointType type,
        CancellationToken cancellationToken);

    ValueTask<bool> AcknowledgeCheckpointAsync(
        CheckpointAcknowledgment acknowledgment,
        CancellationToken cancellationToken);

    // State Queries
    CheckpointCoordinatorState GetState();

    // Recovery
    ValueTask<RestorationResult> RestoreLatestCheckpointAsync(
        CancellationToken cancellationToken);

    // Events
    IObservable<CheckpointEvent> Events { get; }
}
```

**State Machine:**

```
                      ┌─────────────┐
                      │    Idle     │
                      └──────┬──────┘
                             │
                TriggerCheckpoint()
                             │
                             ▼
                      ┌─────────────┐
        ┌────────────►│  Pending    │◄──────────────┐
        │             └──────┬──────┘               │
        │                    │                      │
        │     ┌──────────────┼──────────────┐       │
        │     │              │              │       │
        │     ▼              ▼              ▼       │
        │ Timeout     All Acks         Decline     │
        │     │         Received           │       │
        │     │              │              │       │
        │     ▼              ▼              ▼       │
        │ ┌───────┐   ┌───────────┐   ┌────────┐   │
        │ │Expired│   │ Completed │   │ Failed │   │
        │ └───┬───┘   └─────┬─────┘   └────┬───┘   │
        │     │             │              │       │
        │     └─────────────┼──────────────┘       │
        │                   │                      │
        │                   ▼                      │
        │             ┌───────────┐                │
        └─────────────│   Idle    │────────────────┘
```

### 5.2 Checkpoint Barrier

```csharp
/// <summary>
/// Immutable checkpoint barrier that flows through the DAG.
/// </summary>
public readonly record struct CheckpointBarrier
{
    public required long CheckpointId { get; init; }
    public required long Timestamp { get; init; }
    public required CheckpointOptions Options { get; init; }
}

public enum CheckpointType
{
    Checkpoint,   // Regular periodic checkpoint
    Savepoint,    // User-triggered, portable snapshot
    Sync          // Synchronization barrier (no state)
}

public enum CheckpointMode
{
    ExactlyOnce,  // Barrier alignment required
    AtLeastOnce   // No alignment, faster but may duplicate
}
```

### 5.3 State Backend

```csharp
/// <summary>
/// Abstraction for state storage backends.
/// Implementations: InMemory, RocksDB, Custom
/// </summary>
public interface IStateBackend : IAsyncDisposable
{
    // Factory methods for state stores
    IKeyedStateStore<TKey> CreateKeyedStateStore<TKey>(
        KeyedStateDescriptor descriptor) where TKey : notnull;

    IOperatorStateStore CreateOperatorStateStore(
        OperatorStateDescriptor descriptor);

    // Snapshot operations
    ValueTask<StateSnapshot> SnapshotAsync(
        long checkpointId,
        SnapshotOptions options,
        CancellationToken cancellationToken);

    ValueTask RestoreAsync(
        StateSnapshot snapshot,
        CancellationToken cancellationToken);

    // Incremental checkpoint support
    bool SupportsIncrementalCheckpoints { get; }
}
```

---

## 6. KurrentDB Simplifications

The full Flink-equivalent design from this document includes many features that KurrentDB Processors simplifies:

| Feature | Flink-Equivalent | KurrentDB Processors |
|---------|------------------|---------------------|
| Barrier Alignment | Required for multi-input operators | Not needed (single source log) |
| Operator DAGs | Full support | Single processor (no chaining) |
| Unaligned Checkpoints | Optional | Not needed |
| Incremental Checkpoints | RocksDB-based | Future work |
| State Backend | RocksDB, Memory, HashMap | Shared State Store |
| Checkpoint Storage | HDFS, S3, Azure Blob | Shared State Store |
| Two-Phase Commit | Built-in for sinks | Worker responsibility |

**Key insight**: KurrentDB's simpler topology (single source log, fan-out only) allows us to skip much of Flink's complexity while maintaining the same fundamental guarantees.

---

## 7. Original Document Reference

The full document contains additional sections:

- **Section 6**: gRPC Service Contracts (complete proto definitions)
- **Section 7**: Protocol Buffer Message Contracts (all message types)
- **Section 8**: Sequence Diagrams (checkpoint, recovery, failure flows)
- **Section 9**: State Management (detailed state backend design)
- **Section 10**: Recovery and Restoration (complete recovery protocols)
- **Section 11**: Configuration and Tuning (all configuration options)
- **Section 12**: Extension Points for Reusability (plugin architecture)
- **Section 13**: Implementation Roadmap (phased implementation plan)

For the complete 50k+ token specification, see:
`.project/specs/001-processors/research/checkpointing-origin.md`

---

## 8. References

1. Chandy, K.M. and Lamport, L. (1985). "Distributed Snapshots: Determining Global States of Distributed Systems". ACM TOCS, 3(1), 63-75.

2. Carbone, P., et al. (2015). "Lightweight Asynchronous Snapshots for Distributed Dataflows". arXiv:1506.08603

3. Apache Flink Documentation - Checkpointing
