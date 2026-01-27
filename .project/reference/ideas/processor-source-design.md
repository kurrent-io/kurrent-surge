# Processor Source Design

A comprehensive design document consolidating checkpointing research and the Source Reader design for KurrentDB Processors.

**Version:** 1.0
**Status:** Design Proposal
**Date:** January 2026

---

## Table of Contents

- [Part 1: Checkpointing Foundation](#part-1-checkpointing-foundation)
  - [Topic A: Chandy-Lamport Algorithm](#topic-a-chandy-lamport-algorithm)
  - [Topic B: Asynchronous Barrier Snapshotting (ABS)](#topic-b-asynchronous-barrier-snapshotting-abs)
  - [Topic C: Coordinator + Worker Architecture](#topic-c-coordinator--worker-architecture)
  - [Topic D: Checkpoint Lifecycle](#topic-d-checkpoint-lifecycle)
  - [Topic E: State Management](#topic-e-state-management)
  - [Topic F: Recovery Semantics](#topic-f-recovery-semantics)
  - [Topic G: KurrentDB Simplifications](#topic-g-kurrentdb-simplifications)
- [Part 2: Source Reader Design](#part-2-source-reader-design)
  - [Flink Source Model (What to Preserve)](#flink-source-model-what-to-preserve)
  - [What Java Forces (Removed in C#)](#what-java-forces-removed-in-c)
  - [Option 1: Full Model (ISource + IPartitionReader)](#option-1-full-model-isource--ipartitionreader)
  - [Option 2: Simplified Model (IRecordSource)](#option-2-simplified-model-irecordsource)
  - [ProcessorWorker Architecture](#processorworker-architecture)
- [Part 2.5: Sink Design](#part-25-sink-design)
  - [Why Sinks Need 2PC](#why-sinks-need-2pc)
  - [Flink's Sink Model](#flinks-sink-model)
  - [KurrentDB's Different Model](#kurrentdbs-different-model)
  - [The Collect Pattern](#the-collect-pattern)
  - [Unified ITransactional Interface](#unified-itransactional-interface)
  - [Final Sink Interface](#final-sink-interface)
  - [Source vs Sink Comparison](#source-vs-sink-comparison)
- [Part 2.6: Checkpoint Protocol & Failure Handling](#part-26-checkpoint-protocol--failure-handling)
  - [Checkpoint Protocol (2PC Messages)](#checkpoint-protocol-2pc-messages)
  - [Consistency Levels](#consistency-levels)
  - [Timeout Handling Flow](#timeout-handling-flow)
  - [Key Insight: Snapshots Survive ABORT](#key-insight-snapshots-survive-abort)
- [Part 2.7: Recovery & State Store Design](#part-27-recovery--state-store-design)
- [Part 2.8: Progress Tracking & Observability](#part-28-progress-tracking--observability)
- [Part 2.9: Processor Core Design](#part-29-processor-core-design)
  - [Existing IRecordProcessor & ProcessRecordsContext](#existing-irecordprocessor--processrecordscontext-already-implemented)
  - [What Needs to Be Added for Sinks](#what-needs-to-be-added-for-sinks)
  - [Multi-Partition Processing](#multi-partition-processing-already-supported)
  - [Partition-Aware State](#partition-aware-state-using-existing-statekey)
  - [Component Interaction Summary](#component-interaction-summary)
- [Part 2.10: MVP Release Plan](#part-210-mvp-release-plan)
  - [v1.0 MVP: Core Processing](#v10-mvp-core-processing-ship-now)
  - [v1.1: Stateful Processing](#v11-stateful-processing)
  - [v1.2: Sinks (Exactly-Once Delivery)](#v12-sinks-exactly-once-delivery)
  - [Release Timeline Summary](#release-timeline-summary)
  - [Migration Path for Users](#migration-path-for-users)
- [Part 3: Design Trade-offs](#part-3-design-trade-offs)
- [Part 3.5: Industry Comparison - Axon Framework](#part-35-industry-comparison---axon-framework)
  - [Axon Architecture Overview](#axon-architecture-overview)
  - [Axon's Event Processor Types](#axons-event-processor-types)
  - [Axon's Key Concepts](#axons-key-concepts)
  - [Axon's Claims/Leases-Based Coordination](#axons-claimsleases-based-coordination)
  - [Axon's Co-located Token Pattern](#axons-co-located-token-pattern)
  - [Axon Features Comparison](#axon-features-comparison)
- [Part 4: Open Questions](#part-4-open-questions)

---

## Part 1: Checkpointing Foundation

### Topic A: Chandy-Lamport Algorithm

The Chandy-Lamport algorithm (1985) is the theoretical foundation for distributed snapshots. It solves the fundamental problem of capturing a consistent global state of a distributed system **without halting operations**.

**Key insight:** You can capture a consistent global snapshot by using markers that flow through communication channels.

#### Consistent Cuts and Global Snapshots

A "consistent cut" divides the timeline of each process into "before" and "after" the snapshot. The cut is consistent if: whenever a message receipt is in the "after" portion, the message send is also in the "after" portion.

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

#### Algorithm Assumptions

- Processes communicate via message passing
- Channels are FIFO (messages arrive in order sent)
- Channels are reliable (no message loss)
- The graph of processes is strongly connected

#### How It Works

1. An **initiator** starts the snapshot by recording its state and sending a marker on all outgoing channels
2. When a process receives a marker for the first time, it records its state and forwards the marker
3. For each channel, the process records messages received between its state recording and the marker arrival
4. The snapshot completes when all processes have recorded and all channels are accounted for

**Reference:** Chandy, K.M. and Lamport, L. (1985). "Distributed Snapshots: Determining Global States of Distributed Systems". ACM TOCS, 3(1), 63-75.

---

### Topic B: Asynchronous Barrier Snapshotting (ABS)

Apache Flink adapted Chandy-Lamport into **Asynchronous Barrier Snapshotting** for stream processing:

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

**How ABS works:**

1. The **CheckpointCoordinator** triggers a checkpoint by injecting barriers into source operators
2. Barriers flow downstream through the DAG like regular records
3. When an operator receives barriers from **all** input channels, it snapshots its state
4. After snapshotting, the operator forwards the barrier downstream
5. Operators acknowledge completion to the coordinator
6. When all acknowledgments arrive, the checkpoint is complete

#### Barrier Alignment

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

**Reference:** Carbone, P., et al. (2015). "Lightweight Asynchronous Snapshots for Distributed Dataflows". arXiv:1506.08603

---

### Topic C: Coordinator + Worker Architecture

#### Checkpoint Coordinator Responsibilities

The coordinator orchestrates checkpoint lifecycle:

1. **Trigger checkpoints** on schedule or on-demand
2. **Broadcast barriers** to all workers (Chandy-Lamport markers)
3. **Track acknowledgments** from all workers
4. **Manage checkpoint lifecycle** (pending → completed/failed)
5. **Coordinate recovery** on failure
6. **Manage retention policies** (keep N checkpoints)

#### Barrier Protocol Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         BARRIER PROTOCOL                                 │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Server                              Worker A            Worker B        │
│    │                                    │                   │            │
│    │  checkpoint triggered              │                   │            │
│    │  barrier_id = next_id              │                   │            │
│    │                                    │                   │            │
│    │── Barrier(barrier_id) ────────────►│                   │            │
│    │── Barrier(barrier_id) ────────────────────────────────►│            │
│    │                                    │                   │            │
│    │                                    │ finish current    │            │
│    │                                    │ snapshot state    │            │
│    │                                    │ upload state      │            │
│    │◄── BarrierAck(positions) ──────────│                   │            │
│    │                                    │                   │ snapshot   │
│    │                                    │                   │ upload     │
│    │◄── BarrierAck(positions) ───────────────────────────────│            │
│    │                                    │                   │            │
│    │  all acks received                 │                   │            │
│    │  checkpoint COMPLETE               │                   │            │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

#### State Machine

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

---

### Topic D: Checkpoint Lifecycle

#### Checkpoint States

| State        | Description                                    |
|--------------|------------------------------------------------|
| **Pending**  | Barrier sent, waiting for worker acks          |
| **InProgress** | Some workers have acked, waiting for rest    |
| **Completed** | All workers acked, checkpoint finalized       |
| **Failed**   | Timeout or worker failure during checkpoint    |

#### Trigger Types

| Trigger               | Description                                |
|-----------------------|--------------------------------------------|
| **Interval**          | Time since last checkpoint (e.g., 30 seconds) |
| **Threshold**         | Record count threshold reached             |
| **CaughtUp**          | Reached end of log                         |
| **WorkerDisconnecting** | Graceful shutdown in progress            |
| **Savepoint**         | Manual trigger (never auto-deleted)        |

#### Retention Policies

- Keep N most recent checkpoints (configurable, default 3)
- Delete older checkpoints automatically
- **Savepoints** are exempt from retention and never auto-deleted
- Savepoints used for migrations, upgrades, debugging

---

### Topic E: State Management

#### Partitioned State Model

State is partitioned by **key groups** (partitions). Each partition has its own state blob:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    PARTITIONED STATE                                     │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Keys → hash(key) % max_parallelism → Partition → Worker                │
│                                                                          │
│  ┌───────────┬─────────────┬─────────────┬──────────────┐               │
│  │ partition │ key         │ value       │ updated_at   │               │
│  ├───────────┼─────────────┼─────────────┼──────────────┤               │
│  │ 12        │ cust:alice  │ {...}       │ 1234567890   │               │
│  │ 42        │ cust:carol  │ {...}       │ 1234567891   │               │
│  │ 12        │ cust:dave   │ {...}       │ 1234567892   │               │
│  └───────────┴─────────────┴─────────────┘──────────────┘               │
│                                                                          │
│  Workers only access partitions they own!                                │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

#### Shared State Store Model

Unlike Flink's upload/download model, KurrentDB uses a **shared state store**:

| Approach               | Flink                           | KurrentDB                         |
|------------------------|---------------------------------|-----------------------------------|
| **State location**     | Worker-local, then upload       | Shared store (accessible by all)  |
| **Checkpoint flow**    | Worker → upload → storage       | Worker → commit to shared store   |
| **Recovery flow**      | Download → restore to worker    | Claim partitions from shared store |
| **Rebalancing**        | Transfer state via coordinator  | New worker claims from store      |

**Benefits of shared store:**
- No upload/download latency on rebalancing
- Server not a bottleneck for state transfer
- Simpler protocol (fewer RPCs)
- State always available to any worker
- Enables larger state sizes

---

### Topic F: Recovery Semantics

#### Delivery Guarantees

| Guarantee          | Description                                              |
|--------------------|----------------------------------------------------------|
| **At-Most-Once**   | Records may be lost, never duplicated                    |
| **At-Least-Once**  | Records never lost, may be duplicated (KurrentDB default) |
| **Exactly-Once**   | Records never lost, never duplicated (state only)        |

KurrentDB Processors provide **at-least-once delivery** + **exactly-once state semantics**:

- Every record delivered at least once
- Records may be redelivered after recovery
- State reflects each record exactly once (via checkpoints)

#### Recovery Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    RECOVERY FLOW                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Worker crashes at position 1900                                         │
│  Last checkpoint: #42 at position 1847                                  │
│                                                                          │
│  On restart:                                                             │
│  1. Connect to server                                                    │
│  2. Server: "Last checkpoint #42 at position 1847"                      │
│  3. Claim partitions from shared store (state already there)            │
│  4. Resume from position 1848                                            │
│                                                                          │
│  Events 1848-1900 are reprocessed, but that's OK:                       │
│  - State is consistent with position 1847                               │
│  - Reprocessing from 1848 rebuilds correct state                        │
│  - No duplicates in state, no data loss                                 │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

### Topic G: KurrentDB Simplifications

KurrentDB's simpler topology allows us to skip much of Flink's complexity:

| Feature                | Flink-Equivalent           | KurrentDB Processors          |
|------------------------|----------------------------|-------------------------------|
| **Barrier Alignment**  | Required for multi-input   | Not needed (single source)    |
| **Operator DAGs**      | Full support               | Single processor (no chaining)|
| **Unaligned Checkpoints** | Optional              | Not needed                    |
| **Incremental Checkpoints** | RocksDB-based        | Future work                   |
| **State Backend**      | RocksDB, Memory, HashMap   | Shared State Store            |
| **Watermarks**         | Event-time tracking        | Not needed (processing time)  |
| **Two-Phase Commit**   | Built-in for sinks         | Worker responsibility         |

#### Why No Barrier Alignment?

KurrentDB has a single source ($all log) and no fan-in at workers:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    KURRENTDB: NO ALIGNMENT NEEDED                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│                    $all log (single source)                             │
│                           │                                              │
│                           ▼                                              │
│                    ┌─────────────┐                                      │
│                    │   Server    │                                      │
│                    │  (fan-out)  │                                      │
│                    └──────┬──────┘                                      │
│                           │                                              │
│           ┌───────────────┼───────────────┐                             │
│           │               │               │                             │
│           ▼               ▼               ▼                             │
│      Worker A        Worker B        Worker C                           │
│    (one input)     (one input)     (one input)                         │
│                                                                          │
│   Each worker has exactly ONE input channel                             │
│   Barrier arrives once, snapshot immediately                            │
│   No buffering, no alignment, no complexity                             │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

#### The Hive Mind Model

Unlike Flink's JobManager controlling workers, KurrentDB workers are autonomous:

| Traditional Job Manager        | KurrentDB Hive Mind                     |
|--------------------------------|-----------------------------------------|
| Server starts workers          | Workers start themselves                |
| Server can pause/resume        | Workers decide when to leave            |
| Fixed parallelism              | Dynamic parallelism                     |
| Manager deploys → Workers      | Processor ← Workers → Server coordinate |

---

## Part 2: Source Reader Design

### Flink Source Model (What to Preserve)

From Flink's Source API, we want to preserve these capabilities:

1. **Split discovery** - static and dynamic partition discovery
2. **Split assignment** - coordinator assigns partitions to readers
3. **One reader per split** - clean separation of concerns
4. **Checkpoint state** - both enumerator and reader checkpoint
5. **2PC protocol** - snapshot → complete/abort
6. **Bounded/unbounded** sources - batch and streaming
7. **State restoration** - reader receives previous state on recovery

### What Java Forces (Removed in C#)

| Java Problem              | Flink Workaround                    | C# Solution                          |
|---------------------------|-------------------------------------|--------------------------------------|
| No IAsyncEnumerable       | `pollNext(output)` + `isAvailable()` | `IAsyncEnumerable<T>`               |
| Manual backpressure       | `CompletableFuture<Void>` signaling | Automatic with async enumerable     |
| Verbose serialization     | `SimpleVersionedSerializer<T>`      | Opaque `ReadOnlyMemory<byte>`       |
| No async/await            | Callback-heavy APIs                 | `ValueTask`, `async/await`          |

### Option 1: Full Model (ISource + IPartitionReader)

This model provides maximum flexibility, combining Flink's Source + SplitEnumerator discovery with a clean reader abstraction.

```csharp
/// <summary>
/// A source that can be read in parallel across partitions.
/// Combines Flink's Source + SplitEnumerator discovery.
/// </summary>
public interface ISource<TRecord> {
    /// <summary>
    /// Bounded (batch) or unbounded (streaming).
    /// </summary>
    Boundedness Boundedness { get; }

    /// <summary>
    /// Discover partitions.
    /// - Static sources: yield all partitions immediately, complete
    /// - Dynamic sources: yield as discovered, never complete
    /// </summary>
    IAsyncEnumerable<string> DiscoverPartitions(CancellationToken ct);

    /// <summary>
    /// Create a reader for a specific partition.
    /// </summary>
    /// <param name="partitionId">Which partition to read</param>
    /// <param name="state">Previous checkpoint state. Empty = fresh start.</param>
    IPartitionReader<TRecord> CreatePartitionReader(
        string partitionId,
        ReadOnlyMemory<byte> state);
}

/// <summary>
/// Reads from a single partition.
/// One instance per partition.
/// </summary>
public interface IPartitionReader<TRecord> : IAsyncDisposable {
    /// <summary>
    /// Which partition this reader handles.
    /// </summary>
    string PartitionId { get; }

    /// <summary>
    /// Read records.
    /// IAsyncEnumerable gives us backpressure, cancellation, composability for free.
    /// </summary>
    IAsyncEnumerable<TRecord> Read(CancellationToken ct);

    /// <summary>
    /// Snapshot state for checkpoint.
    /// Returns opaque bytes - source owns the format.
    /// Contains position/offset/whatever the source needs.
    /// </summary>
    ReadOnlyMemory<byte> SnapshotState(long checkpointId);

    /// <summary>
    /// Checkpoint committed successfully.
    /// Can commit external state (Kafka offsets, etc.).
    /// </summary>
    void NotifyCheckpointComplete(long checkpointId);

    /// <summary>
    /// Checkpoint aborted.
    /// Discard any pending checkpoint state.
    /// </summary>
    void NotifyCheckpointAborted(long checkpointId);
}

public enum Boundedness { Bounded, Unbounded }
```

#### Capability Mapping: Flink → Option 1

| Flink Component                    | Option 1 C#                      | Notes                              |
|------------------------------------|----------------------------------|------------------------------------|
| `Source<T, SplitT, EnumChkT>`      | `ISource<TRecord>`               | Factory + discovery combined       |
| `SplitEnumerator`                  | Framework (server)               | Assignment is framework concern    |
| `SplitEnumerator.snapshotState()`  | Framework (server)               | Server tracks unassigned partitions|
| `SourceReader`                     | `IPartitionReader<TRecord>`      | One per partition, not per worker  |
| `SourceReader.pollNext()`          | `IAsyncEnumerable<TRecord>`      | Backpressure free                  |
| `SourceReader.isAvailable()`       | Not needed                       | IAsyncEnumerable handles it        |
| `SourceReader.addSplits()`         | Not needed                       | Reader IS per-partition            |
| `SourceReader.snapshotState()`     | `SnapshotState(checkpointId)`    | Returns bytes                      |
| `SourceReader.notifyCheckpointComplete()` | `NotifyCheckpointComplete()` | 2PC commit                    |
| `SourceSplit`                      | `ReadOnlyMemory<byte>`           | Opaque state                       |
| `SimpleVersionedSerializer`        | Not needed                       | Source serializes its own bytes    |

#### Example: KurrentDB Source (Option 1)

```csharp
public sealed class KurrentDbSource : ISource<Record> {
    readonly KurrentClient _client;

    public KurrentDbSource(KurrentClient client) => _client = client;

    public Boundedness Boundedness => Boundedness.Unbounded;

    public async IAsyncEnumerable<string> DiscoverPartitions(
        [EnumeratorCancellation] CancellationToken ct) {
        // Single partition for $all
        yield return "$all";
    }

    public IPartitionReader<Record> CreatePartitionReader(
        string partitionId,
        ReadOnlyMemory<byte> state) {
        var position = state.IsEmpty
            ? Position.Start
            : DeserializePosition(state);

        return new KurrentDbPartitionReader(_client, partitionId, position);
    }

    static Position DeserializePosition(ReadOnlyMemory<byte> state) =>
        new Position(
            BinaryPrimitives.ReadUInt64LittleEndian(state.Span),
            BinaryPrimitives.ReadUInt64LittleEndian(state.Span[8..]));
}

sealed class KurrentDbPartitionReader : IPartitionReader<Record> {
    readonly KurrentClient _client;
    readonly string _partitionId;
    Position _currentPosition;

    public KurrentDbPartitionReader(
        KurrentClient client,
        string partitionId,
        Position startPosition) {
        _client = client;
        _partitionId = partitionId;
        _currentPosition = startPosition;
    }

    public string PartitionId => _partitionId;

    public async IAsyncEnumerable<Record> Read(
        [EnumeratorCancellation] CancellationToken ct) {
        await foreach (var record in _client.Streams.ReadAllAsync(_currentPosition, ct)) {
            _currentPosition = record.Position;
            yield return record;
        }
    }

    public ReadOnlyMemory<byte> SnapshotState(long checkpointId) {
        var buffer = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, _currentPosition.CommitPosition);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(8), _currentPosition.PreparePosition);
        return buffer;
    }

    public void NotifyCheckpointComplete(long checkpointId) {
        // Nothing to commit externally for KurrentDB
    }

    public void NotifyCheckpointAborted(long checkpointId) {
        // Nothing to rollback
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

#### Example: Kafka Source with External Offset Commit (Option 1)

```csharp
public sealed class KafkaSource : ISource<ConsumeResult<string, string>> {
    readonly string _bootstrapServers;
    readonly string _topic;
    readonly string _groupId;

    public Boundedness Boundedness => Boundedness.Unbounded;

    public async IAsyncEnumerable<string> DiscoverPartitions(
        [EnumeratorCancellation] CancellationToken ct) {
        using var admin = new AdminClientBuilder(new AdminClientConfig {
            BootstrapServers = _bootstrapServers
        }).Build();

        var metadata = admin.GetMetadata(_topic, TimeSpan.FromSeconds(10));
        foreach (var partition in metadata.Topics[0].Partitions) {
            yield return $"{_topic}-{partition.PartitionId}";
        }
    }

    public IPartitionReader<ConsumeResult<string, string>> CreatePartitionReader(
        string partitionId,
        ReadOnlyMemory<byte> state) {
        var offset = state.IsEmpty
            ? Offset.Beginning
            : new Offset(BinaryPrimitives.ReadInt64LittleEndian(state.Span));

        return new KafkaPartitionReader(_bootstrapServers, _groupId, partitionId, offset);
    }
}

sealed class KafkaPartitionReader : IPartitionReader<ConsumeResult<string, string>> {
    readonly IConsumer<string, string> _consumer;
    readonly TopicPartition _topicPartition;
    long _currentOffset;
    long _pendingCheckpointOffset;

    public string PartitionId { get; }

    public async IAsyncEnumerable<ConsumeResult<string, string>> Read(
        [EnumeratorCancellation] CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            var result = _consumer.Consume(ct);
            _currentOffset = result.Offset;
            yield return result;
        }
    }

    public ReadOnlyMemory<byte> SnapshotState(long checkpointId) {
        _pendingCheckpointOffset = _currentOffset;
        var buffer = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, _currentOffset);
        return buffer;
    }

    public void NotifyCheckpointComplete(long checkpointId) {
        // NOW we can commit offsets to Kafka
        // This is the 2PC commit - only after framework confirmed checkpoint stored
        _consumer.Commit(new[] {
            new TopicPartitionOffset(_topicPartition, _pendingCheckpointOffset)
        });
    }

    public void NotifyCheckpointAborted(long checkpointId) {
        // Don't commit offsets - checkpoint failed
        _pendingCheckpointOffset = 0;
    }

    public ValueTask DisposeAsync() {
        _consumer.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

---

### Option 2: Simplified Model (IRecordSource)

This model is simpler for most use cases where the framework handles checkpointing, barriers, and position tracking.

```csharp
/// <summary>
/// The fundamental abstraction for a record source.
///
/// A source knows three things:
/// 1. What partitions exist (streams, shards, files, topics)
/// 2. How to read records from a partition starting from a position
/// 3. How to extract a position from a record for checkpointing
///
/// That's it. The framework handles everything else: distribution across workers,
/// checkpoint persistence, failure recovery, assignment strategies, barrier flow.
/// </summary>
public interface IRecordSource<TRecord, TPosition> {
    /// <summary>
    /// Returns all partitions this source can read from.
    /// Called once at startup.
    /// </summary>
    IReadOnlyList<string> GetPartitions();

    /// <summary>
    /// Reads records from a partition, starting AFTER the given position.
    ///
    /// The returned IAsyncEnumerable should:
    /// - Yield records in order
    /// - Respect cancellation token
    /// - Handle backpressure naturally (IAsyncEnumerable does this)
    /// - Complete when exhausted (bounded) or run forever (unbounded)
    /// </summary>
    IAsyncEnumerable<TRecord> Read(
        string partition,
        TPosition? startAfter,
        CancellationToken ct);

    /// <summary>
    /// Extracts the position from a record for checkpointing.
    /// This position will be passed to Read() on recovery.
    /// </summary>
    TPosition GetPosition(TRecord record);
}

/// <summary>
/// Extension for sources that discover partitions dynamically.
/// </summary>
public interface IDynamicRecordSource<TRecord, TPosition>
    : IRecordSource<TRecord, TPosition> {
    IAsyncEnumerable<string> DiscoverPartitions(CancellationToken ct);
}
```

#### Example: KurrentDB Source (Option 2, ~20 lines)

```csharp
public sealed class KurrentDbSource : IRecordSource<Record, Position> {
    readonly KurrentClient _client;

    public KurrentDbSource(KurrentClient client) => _client = client;

    public IReadOnlyList<string> GetPartitions() =>
        ["$all"];  // For $all, there's one "partition"

    public async IAsyncEnumerable<Record> Read(
        string partition,
        Position? startAfter,
        [EnumeratorCancellation] CancellationToken ct) {
        await foreach (var record in _client.Streams.ReadAllAsync(startAfter ?? Position.Start, ct))
            yield return record;
    }

    public Position GetPosition(Record record) => record.Position;
}
```

---

### ProcessorWorker Architecture

In the simplified architecture, the **worker IS the coordinator** for the single-source case:

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                      ProcessorWorker<TRecord, TPosition>                         │
│                                                                                  │
│  IS the coordinator. IS the executor of IRecordProcessor.                       │
│                                                                                  │
│  ┌────────────────────────────────────────────────────────────────────────────┐ │
│  │ Server Coordination Channel                                                 │ │
│  │   - WorkOrder(partitions, barrierId)                                        │ │
│  │   - CheckpointBarrier(barrierId)                                            │ │
│  └───────────────────────────────┬────────────────────────────────────────────┘ │
│                                  │                                               │
│                                  ▼                                               │
│  ┌────────────────────────────────────────────────────────────────────────────┐ │
│  │ IRecordSource<TRecord, TPosition>                                           │ │
│  │   - GetPartitions()                                                         │ │
│  │   - Read(partition, startAfter, ct) → IAsyncEnumerable<TRecord>            │ │
│  │   - GetPosition(record) → TPosition                                         │ │
│  └───────────────────────────────┬────────────────────────────────────────────┘ │
│                                  │                                               │
│                                  ▼                                               │
│  ┌────────────────────────────────────────────────────────────────────────────┐ │
│  │ Merged Output: IAsyncEnumerable<TRecord | Barrier>                          │ │
│  │                                                                              │ │
│  │   Worker merges:                                                            │ │
│  │   - Records from IRecordSource (tracks positions as they flow)             │ │
│  │   - Barriers from server (snapshots positions when received)               │ │
│  └───────────────────────────────┬────────────────────────────────────────────┘ │
│                                  │                                               │
│                                  ▼                                               │
│  ┌────────────────────────────────────────────────────────────────────────────┐ │
│  │ IRecordProcessor (user implements)                                          │ │
│  │   - OnRecord(record)                                                        │ │
│  │   - OnCheckpoint(barrier) [optional hook]                                   │ │
│  └───────────────────────────────┬────────────────────────────────────────────┘ │
│                                  │                                               │
│                                  ▼                                               │
│  ┌────────────────────────────────────────────────────────────────────────────┐ │
│  │ When Barrier flows through:                                                 │ │
│  │   1. Store checkpoint (positions at barrier point)                          │ │
│  │   2. Ack barrier                                                            │ │
│  │   3. Send CheckpointComplete to server                                      │ │
│  └────────────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────────┘
```

#### Checkpoint Flow with Option 2

```
Server                           ProcessorWorker                    IRecordSource
  │                                    │                                  │
  │── WorkOrder ──────────────────────▶│                                  │
  │                                    │ (load checkpoint, init positions)│
  │◀── WorkerReady ────────────────────│                                  │
  │                                    │                                  │
  │                                    │── Read(partition, startAfter) ──▶│
  │                                    │◀── record ───────────────────────│
  │                                    │ (track position, process record) │
  │                                    │◀── record ───────────────────────│
  │                                    │                                  │
  │── CheckpointBarrier(42) ──────────▶│                                  │
  │                                    │ (snapshot positions)             │
  │                                    │ (inject barrier into stream)     │
  │                                    │                                  │
  │                                    │ (barrier flows through processor)│
  │                                    │                                  │
  │                                    │ (store checkpoint)               │
  │◀── CheckpointComplete(42) ─────────│                                  │
  │                                    │                                  │
  │                                    │◀── record ───────────────────────│
  │                                    │ (continues)                      │
```

---

## Part 2.5: Sink Design

### Why Sinks Need 2PC

Without 2PC, exactly-once delivery to external systems is impossible:

```
Timeline WITHOUT 2PC:
  1. Read record R from source
  2. Process R
  3. Write R to external DB         ← DB now has R
  4. Checkpoint triggered
  5. CRASH before checkpoint stored

On recovery:
  - Restore from previous checkpoint (before R)
  - Replay R
  - Write R to DB again             ← DUPLICATE!

The problem: Output committed before checkpoint confirmed.
```

2PC solves this by separating write and commit:
1. **Prepare**: Buffer writes, don't commit yet
2. **Checkpoint**: Store pending transaction ID
3. **Commit**: Only after checkpoint confirmed, make writes visible

### Flink's Sink Model

Flink separates writing from committing:

```java
// Writer receives records, produces "committables"
public interface SinkWriter<InputT> {
    void write(InputT element, Context context);
    List<CommT> prepareCommit(boolean flush);  // Returns tokens to commit
}

// Committer runs AFTER checkpoint, actually commits
public interface Committer<CommT> {
    void commit(Collection<CommT> committables);
}
```

The "committable" is a token (transaction ID, temp file path, etc.) that represents what to commit. Writer produces them, Committer consumes them after checkpoint.

**Key limitation**: Flink sinks are typed (`Sink<T>`). The type flows through the pipeline.

### KurrentDB's Different Model

KurrentDB processors are NOT a typed pipeline:

```
Flink:      Source<A> → Operator<A,B> → Sink<B>  (types flow)

KurrentDB:  Source → Processor (user does arbitrary work, writes to N sinks)
```

The user might write DIFFERENT types to DIFFERENT sinks:

```csharp
public async ValueTask ProcessRecords(ProcessRecordsContext ctx) {
    foreach (var record in ctx.Records) {
        await kafkaSink.WriteAsync(new OrderEvent(...));      // Type A
        await rabbitSink.WriteAsync(new AuditEntry(...));     // Type B
        await metricsSink.WriteAsync(new MetricPoint(...));   // Type C
    }
}
```

**Why untyped sinks make sense:**
- Kafka, RabbitMQ, etc. serialize to **bytes** - they don't care about .NET types
- Schema registries handle type information at the serialization layer
- Forcing a single type would be artificial and limiting

### The Collect Pattern

Sinks are configured once, then accept any object:

**Configuration:**
```csharp
processor.AddSink(new KafkaSink(config, new AvroSerializer(schemaRegistry)));
processor.AddSink(new RabbitMqSink(config, JsonSerializer.Default));
```

**Processing:**
```csharp
public async ValueTask ProcessRecords(ProcessRecordsContext ctx) {
    foreach (var record in ctx.Records) {
        ctx.Collect(new OrderCreated(...));  // Goes to ALL sinks
        ctx.Collect(new AuditLog(...));      // Each sink serializes its own way
    }
}
```

### Unified ITransactional Interface

Both sources and sinks implement the same 2PC interface:

```csharp
public interface ITransactional {
    ValueTask<Snapshot> PrepareAsync(long barrierId, CancellationToken ct);
    ValueTask CommitAsync(long barrierId, CancellationToken ct);
    ValueTask AbortAsync(long barrierId, CancellationToken ct);
}
```

**Note**: No `ComponentId` (framework handles identification) and no `RestoreAsync` (restoration is initialization, not 2PC).

### Final Sink Interface

```csharp
/// <summary>
/// UNTYPED sink - accepts any object, handles serialization internally.
/// </summary>
public interface ISink : ITransactional, IAsyncDisposable {
    /// <summary>
    /// Write a record. Sink buffers until PrepareAsync.
    /// </summary>
    ValueTask WriteAsync<T>(T record, CancellationToken ct);

    /// <summary>
    /// Flush any buffered writes.
    /// </summary>
    ValueTask FlushAsync(CancellationToken ct);

    /// <summary>
    /// Restore pending state from checkpoint (called after creation).
    /// </summary>
    ValueTask RestoreAsync(Snapshot snapshot, CancellationToken ct);
}
```

### Source vs Sink Comparison

| Aspect | ISourceReader | ISink |
|--------|---------------|-------|
| **Typed?** | Yes (`ISourceReader<TRecord>`) | No (accepts any `T`) |
| **Restoration** | At creation via `CreateReader(partition, snapshot)` | After creation via `RestoreAsync(snapshot)` |
| **PrepareAsync** | Snapshot read position | Snapshot pending writes |
| **CommitAsync** | Commit external offsets (Kafka consumer) | Commit transaction (Kafka producer) |
| **Data flow** | Produces records | Consumes records |

---

## Part 2.6: Checkpoint Protocol & Failure Handling

This section details the 2PC checkpoint protocol, consistency levels, and failure handling semantics.

### Checkpoint Protocol (2PC Messages)

The checkpoint protocol follows a two-phase commit pattern:

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                      TWO-PHASE CHECKPOINT PROTOCOL                               │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                  │
│  PHASE 1 - PREPARE                                                              │
│  ─────────────────                                                              │
│                                                                                  │
│  Server                              Worker A            Worker B               │
│    │                                    │                   │                   │
│    │── Barrier { BarrierId: 42 } ──────►│                   │                   │
│    │── Barrier { BarrierId: 42 } ───────────────────────────►│                   │
│    │                                    │                   │                   │
│    │                                    │ process barrier   │                   │
│    │                                    │ call PrepareAsync │                   │
│    │                                    │ on all ITransac-  │                   │
│    │                                    │ tional components │                   │
│    │                                    │ store snapshots   │                   │
│    │                                    │                   │                   │
│    │◄── BarrierAck { BarrierId: 42 } ───│                   │                   │
│    │                                    │                   │ process barrier   │
│    │                                    │                   │ PrepareAsync      │
│    │                                    │                   │ store snapshots   │
│    │◄── BarrierAck { BarrierId: 42 } ────────────────────────│                   │
│    │                                    │                   │                   │
│                                                                                  │
│  PHASE 2 - COMMIT or ABORT                                                      │
│  ────────────────────────                                                       │
│                                                                                  │
│  Server waits for all acks (or timeout)                                         │
│                                                                                  │
│  ┌─────────────────────────────────────────────────────────────────────────┐    │
│  │ IF ALL WORKERS ACKED:                                                    │    │
│  ├─────────────────────────────────────────────────────────────────────────┤    │
│  │                                                                          │    │
│  │  Server                           Worker A            Worker B          │    │
│  │    │                                 │                   │              │    │
│  │    │── CheckpointCompleted ─────────►│                   │              │    │
│  │    │      { BarrierId: 42 }          │                   │              │    │
│  │    │── CheckpointCompleted ──────────────────────────────►│              │    │
│  │    │                                 │                   │              │    │
│  │    │                                 │ CommitAsync on    │              │    │
│  │    │                                 │ all components    │ CommitAsync  │    │
│  │    │                                 │                   │              │    │
│  │  Checkpoint #42 is now COMPLETE                                         │    │
│  │                                                                          │    │
│  └─────────────────────────────────────────────────────────────────────────┘    │
│                                                                                  │
│  ┌─────────────────────────────────────────────────────────────────────────┐    │
│  │ IF TIMEOUT (worker failed to ack):                                       │    │
│  ├─────────────────────────────────────────────────────────────────────────┤    │
│  │                                                                          │    │
│  │  Server                           Worker A            Worker B (dead)   │    │
│  │    │                                 │                   ╳              │    │
│  │    │── CheckpointAborted ───────────►│                                  │    │
│  │    │      { BarrierId: 42 }          │                                  │    │
│  │    │                                 │                                  │    │
│  │    │                                 │ AbortAsync on                    │    │
│  │    │                                 │ all components                   │    │
│  │    │                                 │                                  │    │
│  │  Checkpoint #42 is INCOMPLETE (but snapshots preserved!)               │    │
│  │                                                                          │    │
│  └─────────────────────────────────────────────────────────────────────────┘    │
│                                                                                  │
└─────────────────────────────────────────────────────────────────────────────────┘
```

**Message Summary:**

| Phase | Direction | Message | Purpose |
|-------|-----------|---------|---------|
| 1 | Server → Worker | `Barrier { BarrierId }` | Initiate checkpoint |
| 1 | Worker → Server | `BarrierAck { BarrierId }` | Confirm prepare complete |
| 2 | Server → Worker | `CheckpointCompleted { BarrierId }` | Commit transaction |
| 2 | Server → Worker | `CheckpointAborted { BarrierId }` | Abort transaction |

---

### Consistency Levels

Users can configure checkpoint consistency based on their use case:

#### Global Consistency

All workers must complete checkpoint together or none do.

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                         GLOBAL CONSISTENCY                                       │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                  │
│  Use case: State spans partitions (e.g., cross-partition aggregations)          │
│                                                                                  │
│  Worker A         Worker B         Worker C                                     │
│  [Part 0-3]      [Part 4-7]       [Part 8-11]                                  │
│      │               │                │                                         │
│      │    Barrier 42 arrives          │                                         │
│      │               │                │                                         │
│      ✓ ACK           ✓ ACK            ╳ TIMEOUT                                 │
│      │               │                │                                         │
│      ▼               ▼                │                                         │
│  ┌───────────────────────────────────────────┐                                  │
│  │  Server: ABORT checkpoint 42              │                                  │
│  │  - Worker C failed to ack                 │                                  │
│  │  - ALL workers receive ABORT              │                                  │
│  │  - Nobody commits external transactions   │                                  │
│  └───────────────────────────────────────────┘                                  │
│      │               │                                                          │
│      ▼               ▼                                                          │
│  AbortAsync()    AbortAsync()     (Worker C evicted)                           │
│                                                                                  │
│  Result: Checkpoint 42 INCOMPLETE, all workers rolled back                      │
│          Partitions 8-11 rebalanced to healthy workers                          │
│                                                                                  │
└─────────────────────────────────────────────────────────────────────────────────┘
```

**Behavior on timeout:**
1. ABORT sent to all workers
2. Nobody commits external transactions
3. Evict failed worker
4. Rebalance partitions to healthy workers
5. New owners restore from last COMPLETE checkpoint

---

#### Partition Consistency

Each partition commits independently. Healthy workers proceed; only failed partitions roll back.

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                       PARTITION CONSISTENCY                                      │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                  │
│  Use case: Independent partitions (most event sourcing scenarios)               │
│                                                                                  │
│  Worker A         Worker B         Worker C                                     │
│  [Part 0-3]      [Part 4-7]       [Part 8-11]                                  │
│      │               │                │                                         │
│      │    Barrier 42 arrives          │                                         │
│      │               │                │                                         │
│      ✓ ACK           ✓ ACK            ╳ TIMEOUT                                 │
│      │               │                │                                         │
│      ▼               ▼                │                                         │
│  ┌───────────────────────────────────────────┐                                  │
│  │  Server: COMMIT healthy, handle failure   │                                  │
│  │  - Workers A, B acked successfully        │                                  │
│  │  - Send COMMIT to A, B                    │                                  │
│  │  - Evict Worker C                         │                                  │
│  └───────────────────────────────────────────┘                                  │
│      │               │                                                          │
│      ▼               ▼                                                          │
│  CommitAsync()   CommitAsync()    (Worker C evicted)                           │
│                                                                                  │
│  Result: Checkpoint 42 PARTIAL                                                  │
│          - Partitions 0-7: committed at barrier 42                              │
│          - Partitions 8-11: restore from last checkpoint, rebalance            │
│                                                                                  │
└─────────────────────────────────────────────────────────────────────────────────┘
```

**Behavior on timeout:**
1. COMMIT sent to healthy workers (they proceed)
2. Evict failed worker
3. Rebalance failed partitions to healthy workers
4. New owners restore from last available checkpoint for those partitions

---

#### Comparison

| Aspect | Global Consistency | Partition Consistency |
|--------|-------------------|----------------------|
| **Atomicity** | All-or-nothing | Per-partition |
| **Failure impact** | All workers abort | Only failed partitions affected |
| **Use case** | Cross-partition state | Independent partitions |
| **Recovery** | All restore from same checkpoint | Each partition may have different checkpoint |
| **Throughput** | Lower (waits for slowest) | Higher (healthy partitions proceed) |
| **Complexity** | Simpler reasoning | More complex state management |

---

### Timeout Handling Flow

The timeout handling flow is similar for both consistency modes, differing only in the COMMIT/ABORT decision:

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                       TIMEOUT HANDLING FLOW                                      │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                  │
│  1. TIMEOUT DETECTED                                                            │
│     ─────────────────                                                           │
│     Server: "Worker C hasn't acked Barrier 42 within timeout"                   │
│                                                                                  │
│  2. EVICT FAILED WORKER                                                         │
│     ────────────────────                                                        │
│     Server: Remove Worker C from worker pool                                    │
│             Close connection, mark as unhealthy                                 │
│                                                                                  │
│  3. BROADCAST DECISION (differs by consistency level)                           │
│     ─────────────────────────────────────────────────────                       │
│     ┌─────────────────────────────┬────────────────────────────┐               │
│     │ GLOBAL CONSISTENCY          │ PARTITION CONSISTENCY      │               │
│     ├─────────────────────────────┼────────────────────────────┤               │
│     │ Broadcast ABORT to all      │ Broadcast COMMIT to        │               │
│     │ healthy workers             │ healthy workers            │               │
│     │                             │                            │               │
│     │ Workers call AbortAsync()   │ Workers call CommitAsync() │               │
│     └─────────────────────────────┴────────────────────────────┘               │
│                                                                                  │
│  4. REBALANCE PARTITIONS                                                        │
│     ────────────────────                                                        │
│     Server: Assign Worker C's partitions (8-11) to healthy workers              │
│             e.g., Worker A gets 8-9, Worker B gets 10-11                        │
│                                                                                  │
│  5. NEW OWNERS RESTORE                                                          │
│     ──────────────────                                                          │
│     Worker A: Load last checkpoint for partitions 8-9                           │
│     Worker B: Load last checkpoint for partitions 10-11                         │
│                                                                                  │
│  6. CONTINUE PROCESSING                                                         │
│     ───────────────────                                                         │
│     All workers resume from their respective checkpoint positions               │
│                                                                                  │
└─────────────────────────────────────────────────────────────────────────────────┘
```

---

### Key Insight: Snapshots Survive ABORT

**Critical understanding**: The stored snapshots from healthy workers are NOT deleted on ABORT.

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                    SNAPSHOTS SURVIVE ABORT                                       │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                  │
│  What ABORT means:                                                              │
│  ─────────────────                                                              │
│  - DON'T commit external transactions (Kafka offsets, DB transactions)          │
│  - DON'T mark checkpoint as COMPLETE                                            │
│                                                                                  │
│  What ABORT does NOT mean:                                                      │
│  ─────────────────────────                                                      │
│  - DON'T delete stored snapshots                                                │
│  - DON'T rollback internal state                                                │
│                                                                                  │
│  Timeline:                                                                       │
│                                                                                  │
│  ┌─────────────────────────────────────────────────────────────────────────┐    │
│  │                                                                          │    │
│  │  Barrier 42: Worker A acks, Worker B acks, Worker C times out           │    │
│  │                                                                          │    │
│  │  Checkpoint Store State:                                                 │    │
│  │  ┌──────────────┬────────────────┬────────────────────────────────────┐ │    │
│  │  │ Checkpoint   │ Status         │ Snapshots                          │ │    │
│  │  ├──────────────┼────────────────┼────────────────────────────────────┤ │    │
│  │  │ #41          │ COMPLETE       │ All partitions (0-11)              │ │    │
│  │  │ #42          │ INCOMPLETE     │ Partitions 0-7 (from A, B)         │ │    │
│  │  │              │                │ Partitions 8-11 missing            │ │    │
│  │  └──────────────┴────────────────┴────────────────────────────────────┘ │    │
│  │                                                                          │    │
│  │  When Worker A takes over partitions 8-9:                               │    │
│  │  - Partitions 0-3: Can use checkpoint #42 (was COMMITTED for A)        │    │
│  │  - Partitions 8-9: Must use checkpoint #41 (last complete for these)   │    │
│  │                                                                          │    │
│  └─────────────────────────────────────────────────────────────────────────┘    │
│                                                                                  │
│  The snapshots are VALID data. ABORT only affects:                              │
│  1. External transaction commit (Kafka producer transactions, etc.)             │
│  2. Checkpoint completion status                                                │
│                                                                                  │
│  With partition consistency, healthy partitions' snapshots from #42             │
│  are still usable - they're just marked at the partition level.                │
│                                                                                  │
└─────────────────────────────────────────────────────────────────────────────────┘
```

**Summary:**

| Action | Affected by ABORT? |
|--------|-------------------|
| Stored snapshots | NO - snapshots preserved |
| External transactions | YES - not committed |
| Checkpoint status | YES - marked INCOMPLETE |
| Internal state | NO - continues as-is |

---

### Finalized Checkpoint Saga Design

#### Key Insight: KurrentDB ≠ Flink

Flink owns its infrastructure - it can restart tasks, roll back state, control everything. KurrentDB workers are autonomous - deployed anywhere, by the user. The server coordinates but doesn't control.

**Consequence**: No reason to "roll back" successful work. Workers that succeeded should commit. Only the failed worker's partitions need attention.

#### Worker Responsibility

Workers are smart and handle their own retry logic:

1. Receive barrier
2. Try preCommit (snapshot state, prepare sinks)
3. On failure: retry with configurable attempts/timeout
4. After exhausting retries: ack with failure
5. On success: ack with success

The server doesn't retry on behalf of workers. Workers are autonomous.

#### Server Responsibility (Checkpoint Saga)

The server runs a simple saga:

```
PendingCheckpoint {
    BarrierId: long
    ExpectedAcks: Set<WorkerId>
    ReceivedAcks: Map<WorkerId, Success | Failure>
    Timeout: DateTime
    Status: Pending | Partial | Complete | Aborted
}
```

**Flow:**
1. Inject barrier to all workers
2. Collect acks (success or failure)
3. On timeout or all acks received → decide outcome

#### Outcome Decision by Consistency Level

**Partition Mode:**
| Scenario | Action |
|----------|--------|
| All acks success | Mark COMPLETE, send CommitCheckpoint to all |
| Some acks success, some failure | Mark COMPLETE, send CommitCheckpoint to successful workers |
| Some acks success, some timeout | Mark PARTIAL, send CommitCheckpoint to successful workers, evict timed-out workers, rebalance |
| All timeout | Mark ABORTED, evict all, rebalance |

**Global Mode:**
| Scenario | Action |
|----------|--------|
| All acks success | Mark COMPLETE, send CommitCheckpoint to all |
| Any failure or timeout | Mark ABORTED, send AbortCheckpoint to all, evict failed/timed-out workers, rebalance |

#### Checkpoint States

- **COMPLETE**: All workers acked successfully. Full restore point.
- **PARTIAL**: Some workers succeeded, some timed out. Partial restore point (only for succeeded partitions).
- **ABORTED**: Checkpoint failed entirely. Not a restore point.

#### Key Principles

1. **Never roll back successful work** - Workers that succeeded commit their checkpoints
2. **Workers handle retries** - Server doesn't retry preCommit, workers do
3. **Timeout = eviction** - Timed-out workers are removed from topology and rebalanced
4. **Partial is valid** - A partial checkpoint is better than no checkpoint (in Partition mode)

---

## Part 2.7: Recovery & State Store Design

### Architecture: Separation of Concerns

| Component | Responsibility |
|-----------|---------------|
| Gateway | Coordination only (barriers, assignments) - agnostic to storage |
| ICheckpointStateStore | Persistence (positions, sinks, processor state) - opaque bytes |
| IStateStore | User-facing state API (Value, Map, Table) - via ProcessRecordsContext |

The Gateway doesn't know or care about storage. Workers interact with ICheckpointStateStore directly for recovery.

### Unified ICheckpointStateStore

A single store handles all checkpoint state as opaque bytes - positions, sink committables, processor state.

```csharp
public record PartitionStateSnapshot(
    string PartitionId,
    long BarrierId,
    ReadOnlyMemory<byte> State  // Contains: position + sink state + processor state
);

public interface ICheckpointStateStore {
    // Single partition
    ValueTask SaveAsync(PartitionStateSnapshot snapshot, CancellationToken ct);
    ValueTask<PartitionStateSnapshot?> LoadLatestAsync(string partitionId, CancellationToken ct);

    // Batch operations (efficient for multi-partition workers)
    ValueTask SaveManyAsync(IReadOnlyList<PartitionStateSnapshot> snapshots, CancellationToken ct);
    ValueTask<IReadOnlyDictionary<string, PartitionStateSnapshot>> LoadLatestManyAsync(
        IReadOnlyList<string> partitionIds, CancellationToken ct);
}
```

### Key Design Decisions

- **Keyed by partition** - Load latest state for a partition, not by barrier
- **Opaque bytes** - Store doesn't know the structure, components serialize/deserialize
- **Batch operations** - Workers handle multiple partitions, need efficient bulk save/load
- **Evolvable** - Interface stays same, bytes grow, backend can change

### Evolution Path

| Phase | State Contains | Backend |
|-------|---------------|---------|
| v1 | Positions only | KurrentDB |
| +Sinks | Positions + sink committables | KurrentDB or S3 |
| +State | Positions + sinks + processor state | S3, RocksDB, etc. |

### Recovery Flow

```
Worker receives WorkOrder { Partitions: [A, B, C] }
  │
  ├── ICheckpointStateStore.LoadLatestManyAsync(["A", "B", "C"])
  │     → {
  │         "A": { BarrierId: 42, State: bytesA },
  │         "B": { BarrierId: 42, State: bytesB },
  │         "C": { BarrierId: 41, State: bytesC }  // different barrier!
  │       }
  │
  ├── Deserialize each partition's state:
  │     - Extract position → create source reader
  │     - Extract sink state → call sink.RestoreAsync() → commit pending txns
  │     - Extract processor state → restore
  │
  └── Send WorkerReady { Positions }
```

### Checkpoint Flow

```
Barrier 42 arrives (worker has partitions A, B, C)
  │
  ├── For each partition:
  │     - Source reader position
  │     - Sink.PrepareAsync() → sink committable bytes
  │     - Processor state snapshot
  │     - Combine all → serialize to bytes
  │
  ├── ICheckpointStateStore.SaveManyAsync([
  │       { PartitionId: "A", BarrierId: 42, State: bytesA },
  │       { PartitionId: "B", BarrierId: 42, State: bytesB },
  │       { PartitionId: "C", BarrierId: 42, State: bytesC }
  │   ])
  │
  └── Send CheckpointComplete { BarrierId: 42, Progress: [...] }
```

### Sink Recovery: Always Commit on Restore

When recovering, if a sink snapshot contains a pending transaction:
- The checkpoint was successful for this partition (otherwise no snapshot)
- Therefore: **commit the pending transaction** (Flink's recoverAndCommit pattern)

The existence of a snapshot implies success. Always commit on recovery.

### Naming Clarification

- **IStateStore** - User-facing API for developers (Value, Map, Table via ProcessRecordsContext)
- **ICheckpointStateStore** - Internal system component for checkpoint persistence
- **ICheckpointStore** (existing) - May be deprecated or kept for backwards compatibility

---

## Part 2.8: Progress Tracking & Observability

### Two Approaches (Both Needed)

| Approach | Update Frequency | Use Case |
|----------|------------------|----------|
| PartitionProgress in protocol | On checkpoint complete | Server bookkeeping, basic UI |
| OTEL metrics | Continuous (1-5 sec) | Real-time dashboards, alerts |

### PartitionProgress (Protocol-Based)

Source-agnostic progress reported with CheckpointComplete:

```csharp
public record PartitionProgress(
    string PartitionId,
    string SourceType,                         // "kurrentdb", "kafka", etc.
    string DisplayPosition,                    // Human readable: "1000/5000"
    double? PercentComplete,                   // 0.0-1.0, if calculable
    IReadOnlyDictionary<string, string>? Metrics  // Source-specific extras
);

public readonly record struct CheckpointComplete(
    BarrierId BarrierId,
    IReadOnlyList<PartitionProgress> Progress
);
```

**For v1 (KurrentDB only):**
```csharp
new PartitionProgress(
    PartitionId: "stream-123",
    SourceType: "kurrentdb",
    DisplayPosition: "1000/5000",
    PercentComplete: 0.2,
    Metrics: new Dictionary<string, string> {
        ["position"] = "1000",
        ["streamEnd"] = "5000"
    }
)
```

**For future (Kafka):**
```csharp
new PartitionProgress(
    PartitionId: "orders-0",
    SourceType: "kafka",
    DisplayPosition: "offset:12345, lag:100",
    PercentComplete: null,  // Can't calculate without knowing end
    Metrics: new Dictionary<string, string> {
        ["offset"] = "12345",
        ["lag"] = "100",
        ["topic"] = "orders",
        ["partition"] = "0"
    }
)
```

### OTEL Metrics (Future, Real-Time)

Workers emit metrics continuously for real-time observability:

**Core processor metrics:**
- `kurrent.processor.position` (Gauge) - current position per partition
- `kurrent.processor.lag` (Gauge) - records behind, if calculable
- `kurrent.processor.records_processed` (Counter) - total processed
- `kurrent.processor.checkpoint.duration_ms` (Histogram) - checkpoint timing

**Source-specific metrics:**
- `kurrent.source.kurrentdb.position`
- `kurrent.source.kurrentdb.stream_end`
- `kurrent.source.kafka.offset`
- `kurrent.source.kafka.lag`

**Sink metrics:**
- `kurrent.sink.records_written` (Counter)
- `kurrent.sink.prepare_duration_ms` (Histogram)
- `kurrent.sink.commit_duration_ms` (Histogram)

### Why Both Approaches?

- **PartitionProgress**: Simple, comes with checkpoint, server can store/display immediately
- **OTEL**: Industry standard, real-time updates, integrates with existing observability stack (Prometheus, Grafana, etc.)

**v1**: PartitionProgress only (sufficient for basic monitoring)
**Future**: Add OTEL for real-time monitoring and alerting

---

## Part 2.9: Processor Core Design

This section details how sources, sinks, and state interact within the ProcessorWorker at runtime.

### The ProcessorWorker Lifecycle

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                     PROCESSOR WORKER LIFECYCLE                                   │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                  │
│  1. STARTUP                                                                     │
│     ─────────────────────────────────────────────────────────────────────────   │
│     Worker starts → connects to Gateway                                         │
│                                                                                  │
│  2. WORK ORDER                                                                  │
│     ─────────────────────────────────────────────────────────────────────────   │
│     Gateway sends WorkOrder { Partitions: [A, B, C] }                          │
│                                                                                  │
│  3. RECOVERY                                                                    │
│     ─────────────────────────────────────────────────────────────────────────   │
│     a. Load checkpoints: StateStore.LoadLatestManyAsync(["A", "B", "C"])       │
│     b. For each partition:                                                      │
│        - Deserialize checkpoint → extract source position, sink state          │
│        - Create source reader: source.CreateReader(partition, snapshot)        │
│     c. Restore sinks: foreach sink → sink.RestoreAsync(snapshot)               │
│        - If pending transactions exist → COMMIT them (recovery commit)         │
│     d. Send WorkerReady { Positions }                                          │
│                                                                                  │
│  4. PROCESSING LOOP                                                             │
│     ─────────────────────────────────────────────────────────────────────────   │
│     ┌──────────────────────────────────────────────────────────────────────┐   │
│     │  while (!cancellation) {                                              │   │
│     │      // Merge records from all partition readers + barriers           │   │
│     │      var item = await MergedStream.ReadAsync();                       │   │
│     │                                                                        │   │
│     │      if (item is Record record) {                                     │   │
│     │          // Build context for this batch                              │   │
│     │          ctx.Records.Add(record);                                     │   │
│     │          if (ctx.Records.Count >= batchSize || timeout) {            │   │
│     │              await processor.ProcessRecords(ctx);                     │   │
│     │              ctx.Records.Clear();                                     │   │
│     │          }                                                             │   │
│     │      }                                                                 │   │
│     │                                                                        │   │
│     │      if (item is Barrier barrier) {                                   │   │
│     │          // Flush any pending records first                           │   │
│     │          if (ctx.Records.Any())                                       │   │
│     │              await processor.ProcessRecords(ctx);                     │   │
│     │                                                                        │   │
│     │          // Checkpoint sequence                                       │   │
│     │          await HandleBarrier(barrier);                                │   │
│     │      }                                                                 │   │
│     │  }                                                                     │   │
│     └──────────────────────────────────────────────────────────────────────┘   │
│                                                                                  │
│  5. SHUTDOWN                                                                    │
│     ─────────────────────────────────────────────────────────────────────────   │
│     - Cancel processing loop                                                    │
│     - Dispose readers: foreach reader → reader.DisposeAsync()                  │
│     - Dispose sinks: foreach sink → sink.DisposeAsync()                        │
│     - Disconnect from Gateway                                                   │
│                                                                                  │
└─────────────────────────────────────────────────────────────────────────────────┘
```

### Record Flow: Source → Processor → Sinks

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                           RECORD FLOW                                            │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                  │
│  ┌─────────────────────────────────────────────────────────────────────────┐   │
│  │                      SOURCE READERS                                      │   │
│  │                                                                          │   │
│  │  Reader[A].Read()  Reader[B].Read()  Reader[C].Read()                   │   │
│  │       │                 │                 │                              │   │
│  │       ▼                 ▼                 ▼                              │   │
│  │  ┌─────────────────────────────────────────────────────────────────┐    │   │
│  │  │              MERGED ASYNC ENUMERABLE                             │    │   │
│  │  │                                                                  │    │   │
│  │  │  Combines all partition readers + barrier channel into          │    │   │
│  │  │  single stream, preserving per-partition order                  │    │   │
│  │  └─────────────────────────────────────────────────────────────────┘    │   │
│  └─────────────────────────────────────────────────────────────────────────┘   │
│                                    │                                            │
│                                    ▼                                            │
│  ┌─────────────────────────────────────────────────────────────────────────┐   │
│  │                     BATCHING LAYER                                       │   │
│  │                                                                          │   │
│  │  Collects records into batches (configurable size/timeout)              │   │
│  │  Flushes immediately when barrier arrives                               │   │
│  └─────────────────────────────────────────────────────────────────────────┘   │
│                                    │                                            │
│                                    ▼                                            │
│  ┌─────────────────────────────────────────────────────────────────────────┐   │
│  │                     PROCESSOR (User Code)                                │   │
│  │                                                                          │   │
│  │  public class OrderProcessor : IRecordProcessor {                       │   │
│  │      public async ValueTask ProcessRecords(ProcessRecordsContext ctx) { │   │
│  │          foreach (var record in ctx.Records) {                          │   │
│  │              var order = record.GetData<OrderPlaced>();                 │   │
│  │                                                                          │   │
│  │              // State operations                                         │   │
│  │              await ctx.State.Value<Stats>("stats")                      │   │
│  │                  .UpdateAsync(s => s.Count++);                          │   │
│  │                                                                          │   │
│  │              // Collect to sinks                                         │   │
│  │              await ctx.Collect(new OrderProcessed(order.Id));           │   │
│  │          }                                                               │   │
│  │      }                                                                   │   │
│  │  }                                                                       │   │
│  └─────────────────────────────────────────────────────────────────────────┘   │
│                                    │                                            │
│                         ctx.Collect(obj)                                       │
│                                    │                                            │
│                                    ▼                                            │
│  ┌─────────────────────────────────────────────────────────────────────────┐   │
│  │                           SINKS                                          │   │
│  │                                                                          │   │
│  │  ┌─────────────┐   ┌─────────────┐   ┌─────────────┐                   │   │
│  │  │ Kafka Sink  │   │ RabbitMQ    │   │   S3 Sink   │                   │   │
│  │  │             │   │   Sink      │   │             │                   │   │
│  │  │ Avro/Proto  │   │   JSON      │   │   Parquet   │                   │   │
│  │  └─────────────┘   └─────────────┘   └─────────────┘                   │   │
│  │                                                                          │   │
│  │  Each sink receives ALL collected records                               │   │
│  │  Sink handles: buffering, serialization, batching to external system    │   │
│  └─────────────────────────────────────────────────────────────────────────┘   │
│                                                                                  │
└─────────────────────────────────────────────────────────────────────────────────┘
```

### Barrier Handling

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                         BARRIER HANDLING                                         │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                  │
│  async Task HandleBarrier(Barrier barrier) {                                    │
│      var barrierId = barrier.BarrierId;                                         │
│      var snapshots = new Dictionary<string, Snapshot>();                        │
│                                                                                  │
│      try {                                                                       │
│          // ═══════════════════════════════════════════════════════════════     │
│          // PHASE 1: PREPARE                                                    │
│          // ═══════════════════════════════════════════════════════════════     │
│                                                                                  │
│          // 1a. Prepare source readers (snapshot positions)                     │
│          foreach (var reader in _readers) {                                     │
│              var snapshot = await reader.PrepareAsync(barrierId, ct);           │
│              snapshots[$"reader:{reader.Partition.Id}"] = snapshot;             │
│          }                                                                       │
│                                                                                  │
│          // 1b. Flush and prepare sinks (stage pending writes)                  │
│          foreach (var (sink, index) in _sinks.WithIndex()) {                    │
│              await sink.FlushAsync(ct);                                         │
│              var snapshot = await sink.PrepareAsync(barrierId, ct);             │
│              snapshots[$"sink:{index}"] = snapshot;                             │
│          }                                                                       │
│                                                                                  │
│          // 1c. Prepare processor state (if any)                                │
│          var stateSnapshot = await _state.PrepareAsync(barrierId, ct);          │
│          snapshots["state"] = stateSnapshot;                                    │
│                                                                                  │
│          // ═══════════════════════════════════════════════════════════════     │
│          // PHASE 1.5: STORE CHECKPOINT                                         │
│          // ═══════════════════════════════════════════════════════════════     │
│                                                                                  │
│          // Combine all snapshots into partition-level checkpoints              │
│          var partitionSnapshots = CombineSnapshots(snapshots);                  │
│          await _stateStore.SaveManyAsync(partitionSnapshots, ct);               │
│                                                                                  │
│          // ═══════════════════════════════════════════════════════════════     │
│          // PHASE 1.6: ACK BARRIER                                              │
│          // ═══════════════════════════════════════════════════════════════     │
│                                                                                  │
│          var progress = BuildProgress();  // PartitionProgress for each        │
│          await _gateway.SendBarrierAck(barrierId, success: true, progress);    │
│                                                                                  │
│      } catch (Exception ex) {                                                   │
│          // Retry logic here (configurable attempts)                            │
│          // If all retries fail:                                                │
│          await _gateway.SendBarrierAck(barrierId, success: false, error: ex);  │
│          return;                                                                 │
│      }                                                                           │
│                                                                                  │
│      // ═══════════════════════════════════════════════════════════════════     │
│      // WAIT FOR PHASE 2 (COMMIT or ABORT)                                      │
│      // ═══════════════════════════════════════════════════════════════════     │
│                                                                                  │
│      var decision = await _gateway.WaitForCheckpointDecision(barrierId);       │
│                                                                                  │
│      if (decision == CheckpointDecision.Commit) {                              │
│          // PHASE 2a: COMMIT                                                    │
│          foreach (var reader in _readers)                                       │
│              await reader.CommitAsync(barrierId, ct);                           │
│          foreach (var sink in _sinks)                                           │
│              await sink.CommitAsync(barrierId, ct);                             │
│          await _state.CommitAsync(barrierId, ct);                               │
│      } else {                                                                    │
│          // PHASE 2b: ABORT                                                     │
│          foreach (var reader in _readers)                                       │
│              await reader.AbortAsync(barrierId, ct);                            │
│          foreach (var sink in _sinks)                                           │
│              await sink.AbortAsync(barrierId, ct);                              │
│          await _state.AbortAsync(barrierId, ct);                                │
│      }                                                                           │
│  }                                                                               │
│                                                                                  │
└─────────────────────────────────────────────────────────────────────────────────┘
```

### Existing IRecordProcessor & ProcessRecordsContext (Already Implemented)

**Important**: The processor interfaces already exist in `src/Kurrent.Processors/IRecordProcessor.cs`.

**Existing `IRecordProcessor`:**
```csharp
public interface IRecordProcessor {
    ValueTask OnInitialize(InitializationContext context) => ValueTask.CompletedTask;
    ValueTask ProcessRecords(ProcessRecordsContext context);  // Required
    ValueTask OnCheckpointComplete(CheckpointCompleteContext context) => ValueTask.CompletedTask;
    ValueTask OnShutdown(ShutdownContext context) => ValueTask.CompletedTask;
}
```

**Existing `ProcessRecordsContext`:**
```csharp
public record ProcessRecordsContext(
    ProcessorId ProcessorId,
    WorkerId WorkerId,
    SessionId SessionId,
    RecordBatch Records,
    ProcessorProgress Progress,
    IStateStore State,          // ← Already has state!
    ILogger Logger,
    CancellationToken CancellationToken
);
```

**Existing `Record` with partition info:**
```csharp
public record Record {
    public RecordPosition Position { get; init; }  // Contains Partition!
    // ... other properties
}

public readonly record struct RecordPosition {
    public Partition Partition { get; init; }  // ← Already has partition!
    public LogPosition LogPosition { get; init; }
    public StreamName Stream { get; init; }
    public StreamRevision StreamRevision { get; init; }
}
```

### What Needs to Be Added for Sinks

The only missing piece is sink integration via `Collect`. Options:

**Option A: Add to ProcessRecordsContext**
```csharp
public record ProcessRecordsContext(
    // ... existing properties ...
    IStateStore State,
    ISinkCollector Sinks,       // NEW: sink access
    ILogger Logger,
    CancellationToken CancellationToken
);

public interface ISinkCollector {
    ValueTask Collect<T>(T record, CancellationToken ct = default);
    ValueTask CollectTo<T>(string sinkName, T record, CancellationToken ct = default);
}
```

**Option B: Extension methods on context**
```csharp
public static class ProcessRecordsContextExtensions {
    public static ValueTask Collect<T>(this ProcessRecordsContext ctx, T record)
        => ctx.Sinks.WriteAsync(record, ctx.CancellationToken);
}
```

**Recommendation**: Option A with `ISinkCollector` property - explicit, discoverable, testable.

### Multi-Partition Processing (Already Supported)

Records already carry partition info via `record.Position.Partition`. Users can:

```csharp
public ValueTask ProcessRecords(ProcessRecordsContext ctx) {
    // Process all records uniformly (ignoring partition)
    foreach (var record in ctx.Records) {
        await ctx.State.Value<Stats>("global-stats").UpdateAsync(s => s.Count++);
    }

    // Or group by partition when needed
    foreach (var group in ctx.Records.GroupBy(r => r.Position.Partition)) {
        var partition = group.Key;
        foreach (var record in group) {
            // Partition-scoped state via StateKey
            await ctx.State.Value<Stats>((partition, "stats")).UpdateAsync(s => s.Count++);
        }
    }

    return ValueTask.CompletedTask;
}
```

### Partition-Aware State (Using Existing StateKey)

State operations already support partition scoping via `StateKey`:

```csharp
// StateKey supports implicit conversion from:
// - string: uses default partition (0)
// - (int partition, string name): explicit partition

// Global state (partition 0)
await ctx.State.Value<Stats>("stats").GetAsync();

// Partition-scoped state
foreach (var record in ctx.Records) {
    var partition = record.Position.Partition;
    await ctx.State.Value<Stats>((partition, "stats")).UpdateAsync(s => s.Count++);
}
```

### Component Interaction Summary

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                    COMPONENT INTERACTION SUMMARY                                 │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                  │
│  ┌─────────────────────────────────────────────────────────────────────────┐   │
│  │                        ProcessorWorker                                   │   │
│  │                                                                          │   │
│  │  Owns and coordinates:                                                   │   │
│  │  - ISource + ISourceReader[] (one reader per assigned partition)        │   │
│  │  - ISink[] (registered sinks, shared across partitions)                 │   │
│  │  - IStateStore (user-facing state API)                                  │   │
│  │  - ICheckpointStateStore (internal checkpoint persistence)              │   │
│  │  - IRecordProcessor (user code)                                         │   │
│  │  - Gateway connection                                                    │   │
│  └─────────────────────────────────────────────────────────────────────────┘   │
│                                                                                  │
│  Data Flow:                                                                     │
│  ───────────────────────────────────────────────────────────────────────────   │
│                                                                                  │
│  Source Readers ──┬── Record ──► Batching ──► IRecordProcessor                 │
│                   │                                   │                         │
│  Gateway ─────────┴── Barrier ──► HandleBarrier()     │                         │
│                                                       │                         │
│                                         ctx.State ◄───┤                         │
│                                                       │                         │
│                                         ctx.Collect ──┴──► Sinks               │
│                                                                                  │
│  Checkpoint Flow:                                                               │
│  ───────────────────────────────────────────────────────────────────────────   │
│                                                                                  │
│  Gateway sends Barrier                                                          │
│       │                                                                         │
│       ▼                                                                         │
│  Flush pending batch to IRecordProcessor                                       │
│       │                                                                         │
│       ▼                                                                         │
│  PrepareAsync on all ITransactional components                                 │
│  (readers + sinks + state)                                                     │
│       │                                                                         │
│       ▼                                                                         │
│  Combine snapshots → ICheckpointStateStore.SaveManyAsync()                     │
│       │                                                                         │
│       ▼                                                                         │
│  SendBarrierAck(success, progress) to Gateway                                  │
│       │                                                                         │
│       ▼                                                                         │
│  Wait for CommitCheckpoint or AbortCheckpoint                                  │
│       │                                                                         │
│       ▼                                                                         │
│  CommitAsync or AbortAsync on all components                                   │
│                                                                                  │
└─────────────────────────────────────────────────────────────────────────────────┘
```

### Open Design Questions for Processor Core

**Q1: Batching Strategy**
- Fixed size batches vs time-based vs hybrid?
- Should batch boundaries respect partition boundaries?
- Current design: hybrid (size OR timeout), mixed partitions

**Q2: Backpressure from Sinks**
- What happens when a sink is slow?
- Options: block processing, buffer (with limit), drop (with metrics)
- Current design: block (sinks buffer internally, backpressure propagates)

**Q3: Error Handling in ProcessRecords**
- If user code throws, what happens?
- Options: fail batch (retry), fail worker (rebalance), skip record (log)
- Current design: configurable per-processor

**Q4: Partition Rebalance Mid-Processing**
- Worker loses partition A mid-batch containing A records
- Options: discard batch, filter out A records, fail and restart
- Current design: discard batch for lost partitions, rely on checkpoint

---

## Part 2.10: MVP Release Plan

This section defines the phased release strategy for KurrentDB Processors, prioritizing delivery under time pressure while maintaining a clean evolution path.

### Release Overview

| Version | Scope | Key Capability |
|---------|-------|----------------|
| **v1.0** | Core Processing | Read → Process → Checkpoint |
| **v1.1** | + State | Stateful processing with DuckDB |
| **v1.2** | + Sinks | Exactly-once delivery to external systems |

---

### v1.0 MVP: Core Processing (Ship Now)

**Tagline**: "Read records, process them, checkpoint positions"

#### What Ships

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                           v1.0 MVP ARCHITECTURE                                  │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                  │
│  KurrentDB ──► Gateway ──► Worker ──► IRecordProcessor (User Code)             │
│                              │                                                   │
│                              └──► ICheckpointStore (Position tracking)          │
│                                                                                  │
│  User sees:                                                                     │
│  ┌────────────────────────────────────────────────────────────────────────┐    │
│  │  public class MyProcessor : IRecordProcessor {                          │    │
│  │      public ValueTask ProcessRecords(ProcessRecordsContext ctx) {       │    │
│  │          foreach (var record in ctx.Records) {                          │    │
│  │              var data = record.GetData<OrderPlaced>();                  │    │
│  │              // Process, call external APIs, write to DB, etc.          │    │
│  │              // NO ctx.State, NO ctx.Sinks                              │    │
│  │          }                                                               │    │
│  │          return ValueTask.CompletedTask;                                │    │
│  │      }                                                                   │    │
│  │  }                                                                       │    │
│  └────────────────────────────────────────────────────────────────────────┘    │
│                                                                                  │
└─────────────────────────────────────────────────────────────────────────────────┘
```

#### Features Included

| Feature | Status | Notes |
|---------|--------|-------|
| `IRecordProcessor` interface | ✓ EXISTS | No changes |
| `ProcessRecordsContext` | ✓ EXISTS | Hide `State` property (internal) |
| Lifecycle hooks | ✓ EXISTS | OnInitialize, OnCheckpointComplete, OnShutdown |
| `ICheckpointStore` | ✓ EXISTS | Position-based checkpointing |
| Gateway protocol | ✓ EXISTS | WorkOrder, RecordBatch, CheckpointBarrier |
| Multi-partition support | ✓ EXISTS | `record.Position.Partition` |
| Progress tracking | ✓ EXISTS | `ProcessorProgress` with per-partition metrics |

#### Code Changes Required

**1. Hide State from public API:**

```csharp
// ProcessRecordsContext - make State internal or remove from public signature
public record ProcessRecordsContext(
    ProcessorId ProcessorId,
    WorkerId WorkerId,
    SessionId SessionId,
    RecordBatch Records,
    ProcessorProgress Progress,
    // IStateStore State,       // HIDE: make internal or don't expose
    ILogger Logger,
    CancellationToken CancellationToken
) {
    // Internal access for future use
    internal IStateStore? State { get; init; }
}
```

**2. No sink-related code** - simply don't add it.

**3. Documentation** - Document v1.0 capabilities without State/Sinks.

#### What Users Can Do

- ✓ Subscribe to streams ($all, category, specific streams)
- ✓ Process records in batches
- ✓ Track progress per partition
- ✓ Automatic checkpointing (position-based)
- ✓ Lifecycle hooks for setup/teardown
- ✓ Scale horizontally with multiple workers
- ✓ Call external APIs, write to databases (at-least-once)
- ✗ Stateful aggregations (use external DB)
- ✗ Exactly-once to external systems (handle idempotency manually)

#### Effort Estimate

```
Changes needed: ~0-1 day
- Hide State property (internal modifier or conditional)
- Update documentation
- No new interfaces or implementations
```

---

### v1.1: Stateful Processing

**Tagline**: "Process with state, checkpoint state automatically"

#### What's Added

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                        v1.1 ADDITIONS (State)                                    │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                  │
│  v1.0 components +                                                              │
│                                                                                  │
│  ┌─────────────────────────────────────────────────────────────────────────┐   │
│  │                         STATE LAYER                                      │   │
│  │                                                                          │   │
│  │  IStateStore (User-facing API)                                          │   │
│  │      ├── Value<T>(key)   → Single value per key                         │   │
│  │      ├── Map<K,V>(key)   → Key-value dictionary per state key           │   │
│  │      └── Table<T>(key)   → SQL-like operations                          │   │
│  │                                                                          │   │
│  │  Backend: DuckDB (local) or MotherDuck (shared)                         │   │
│  │                                                                          │   │
│  │  Checkpoint Integration:                                                 │   │
│  │      State participates in checkpoint barriers                          │   │
│  │      State is restored on recovery                                       │   │
│  │                                                                          │   │
│  └─────────────────────────────────────────────────────────────────────────┘   │
│                                                                                  │
│  User sees:                                                                     │
│  ┌────────────────────────────────────────────────────────────────────────┐    │
│  │  public ValueTask ProcessRecords(ProcessRecordsContext ctx) {           │    │
│  │      foreach (var record in ctx.Records) {                              │    │
│  │          var order = record.GetData<OrderPlaced>();                     │    │
│  │                                                                          │    │
│  │          // NEW: Stateful operations                                    │    │
│  │          await ctx.State                                                │    │
│  │              .Value<CustomerStats>(order.CustomerId)                    │    │
│  │              .UpdateAsync(s => s.OrderCount++);                         │    │
│  │                                                                          │    │
│  │          var topOrders = await ctx.State                                │    │
│  │              .Table<Order>("orders")                                    │    │
│  │              .Where(o => o.CustomerId == order.CustomerId)              │    │
│  │              .Take(10)                                                  │    │
│  │              .ToArrayAsync();                                           │    │
│  │      }                                                                   │    │
│  │  }                                                                       │    │
│  └────────────────────────────────────────────────────────────────────────┘    │
│                                                                                  │
└─────────────────────────────────────────────────────────────────────────────────┘
```

#### Features Added

| Feature | Interface | Implementation |
|---------|-----------|----------------|
| Value state | `IValueStateBuilder<T>` | DuckDB JSON column |
| Map state | `IMapStateBuilder<K,V>` | DuckDB key-value table |
| Table state | `ITableStateBuilder<T>` | DuckDB table with JSON |
| State checkpointing | State participates in barrier | Snapshot on prepare |
| State recovery | Restore on startup | Load from checkpoint |

#### Code Changes Required

**1. Expose State on ProcessRecordsContext:**

```csharp
public record ProcessRecordsContext(
    // ... existing properties ...
    IStateStore State,          // NOW PUBLIC
    ILogger Logger,
    CancellationToken CancellationToken
);
```

**2. Implement IStateStore backend:**

```csharp
// DuckDB implementation
public class DuckDbStateStore : IStateStore {
    public IValueStateBuilder<T> Value<T>(StateKey key) where T : class;
    public IMapStateBuilder<TKey, TValue> Map<TKey, TValue>(StateKey key);
    public ITableStateBuilder<T> Table<T>(StateKey key) where T : class;
    public ValueTask ClearAllAsync(CancellationToken ct);

    // Internal: checkpoint participation
    internal ValueTask<ReadOnlyMemory<byte>> SnapshotAsync(CancellationToken ct);
    internal ValueTask RestoreAsync(ReadOnlyMemory<byte> snapshot, CancellationToken ct);
}
```

**3. Implement ICheckpointStateStore (opaque bytes storage):**

This is the key infrastructure piece - evolve from typed positions to opaque bytes:

```csharp
/// <summary>
/// Snapshot of all processor state for a single partition.
/// Contains: source position + processor state (DuckDB).
/// In v1.2, will also contain sink transaction IDs.
/// </summary>
public record PartitionStateSnapshot(
    string PartitionId,
    long BarrierId,
    ReadOnlyMemory<byte> State   // Opaque bytes - worker serializes/deserializes
);

/// <summary>
/// Internal checkpoint persistence - stores opaque bytes per partition.
/// </summary>
public interface ICheckpointStateStore {
    ValueTask SaveAsync(PartitionStateSnapshot snapshot, CancellationToken ct);
    ValueTask<PartitionStateSnapshot?> LoadLatestAsync(string partitionId, CancellationToken ct);

    // Batch operations for multi-partition workers
    ValueTask SaveManyAsync(IReadOnlyList<PartitionStateSnapshot> snapshots, CancellationToken ct);
    ValueTask<IReadOnlyDictionary<string, PartitionStateSnapshot>> LoadLatestManyAsync(
        IReadOnlyList<string> partitionIds, CancellationToken ct);
}
```

**4. Worker serializes combined state:**

The worker owns serialization - combines position + DuckDB state into bytes:

```csharp
// Worker combines all state into opaque bytes
sealed record CheckpointPayload(
    RecordPosition Position,           // Source position (existing)
    ReadOnlyMemory<byte> ProcessorState // DuckDB snapshot (new)
    // v1.2: Dictionary<string, ReadOnlyMemory<byte>> SinkStates
);

// Serialize for storage
var payload = new CheckpointPayload(position, await _stateStore.SnapshotAsync(ct));
var bytes = SerializePayload(payload);  // JSON, MessagePack, etc.
await _checkpointStateStore.SaveAsync(new(partitionId, barrierId, bytes), ct);

// Deserialize on recovery
var snapshot = await _checkpointStateStore.LoadLatestAsync(partitionId, ct);
var payload = DeserializePayload(snapshot.State);
await _stateStore.RestoreAsync(payload.ProcessorState, ct);
```

**5. State participation in barrier handling:**

```csharp
async Task HandleBarrier(Barrier barrier) {
    // Flush pending batch
    await FlushPendingRecords();

    // Snapshot state
    var stateSnapshot = await _stateStore.SnapshotAsync(ct);

    // Combine with position into checkpoint payload
    var payload = new CheckpointPayload(_currentPosition, stateSnapshot);
    var bytes = SerializePayload(payload);

    // Store checkpoint (opaque bytes)
    await _checkpointStateStore.SaveAsync(
        new PartitionStateSnapshot(partitionId, barrier.BarrierId, bytes), ct);

    // Ack barrier
    await _gateway.SendBarrierAck(barrier.BarrierId, success: true, progress);
}
```

**6. State recovery on startup:**

```csharp
async Task OnWorkOrder(WorkOrder order) {
    var snapshots = await _checkpointStateStore.LoadLatestManyAsync(order.Partitions, ct);

    foreach (var (partitionId, snapshot) in snapshots) {
        var payload = DeserializePayload(snapshot.State);

        // Restore position
        _positions[partitionId] = payload.Position;

        // Restore DuckDB state
        await _stateStore.RestoreAsync(payload.ProcessorState, ct);
    }

    await _gateway.SendWorkerReady(_positions);
}
```

#### What Users Can Do (Cumulative)

Everything from v1.0, plus:
- ✓ Stateful aggregations (counts, sums, averages)
- ✓ Lookup tables and caches
- ✓ Complex queries on processed data
- ✓ State automatically checkpointed
- ✓ State automatically restored on recovery
- ✗ Exactly-once to external systems (still manual idempotency)

#### Effort Estimate

```
New code:
- ICheckpointStateStore interface + KurrentDB implementation
- CheckpointPayload serialization (JSON or MessagePack)
- DuckDbStateStore implementation (Value, Map, Table)
- DuckDB snapshot/restore (export/import)
- Worker barrier handling updates
- Worker recovery updates
- Tests

Estimate: 2-3 weeks (revised up from 1-2 weeks)
```

---

### v1.2: Sinks (Exactly-Once Delivery)

**Tagline**: "Exactly-once from KurrentDB to anywhere"

#### What's Added

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                        v1.2 ADDITIONS (Sinks)                                    │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                  │
│  v1.0 + v1.1 components +                                                       │
│                                                                                  │
│  ┌─────────────────────────────────────────────────────────────────────────┐   │
│  │                         SINK LAYER                                       │   │
│  │                                                                          │   │
│  │  ISink (2PC-enabled output)                                             │   │
│  │      ├── WriteAsync<T>()     → Buffer records                           │   │
│  │      ├── FlushAsync()        → Flush to pending transaction             │   │
│  │      ├── PrepareAsync()      → Stage transaction, return snapshot       │   │
│  │      ├── CommitAsync()       → Make transaction visible                 │   │
│  │      ├── AbortAsync()        → Discard pending transaction              │   │
│  │      └── RestoreAsync()      → Recover and commit pending txns          │   │
│  │                                                                          │   │
│  │  ISinkCollector (User-facing API)                                       │   │
│  │      ├── Collect<T>()        → Send to ALL sinks                        │   │
│  │      └── CollectTo<T>()      → Send to specific sink                    │   │
│  │                                                                          │   │
│  │  Built-in Sinks:                                                        │   │
│  │      ├── KafkaSink           → Exactly-once via Kafka transactions      │   │
│  │      ├── PostgresSink        → Exactly-once via DB transactions         │   │
│  │      └── S3Sink              → Exactly-once via atomic uploads          │   │
│  │                                                                          │   │
│  └─────────────────────────────────────────────────────────────────────────┘   │
│                                                                                  │
│  User sees:                                                                     │
│  ┌────────────────────────────────────────────────────────────────────────┐    │
│  │  public ValueTask ProcessRecords(ProcessRecordsContext ctx) {           │    │
│  │      foreach (var record in ctx.Records) {                              │    │
│  │          var order = record.GetData<OrderPlaced>();                     │    │
│  │                                                                          │    │
│  │          // State (from v1.1)                                           │    │
│  │          await ctx.State.Value<Stats>("stats")                          │    │
│  │              .UpdateAsync(s => s.Count++);                              │    │
│  │                                                                          │    │
│  │          // NEW: Collect to sinks (exactly-once)                        │    │
│  │          await ctx.Sinks.Collect(new OrderProcessed {                   │    │
│  │              OrderId = order.Id,                                        │    │
│  │              ProcessedAt = DateTime.UtcNow                              │    │
│  │          });                                                             │    │
│  │                                                                          │    │
│  │          // Or to specific sink                                         │    │
│  │          await ctx.Sinks.CollectTo("kafka", new OrderEvent(order));     │    │
│  │      }                                                                   │    │
│  │  }                                                                       │    │
│  └────────────────────────────────────────────────────────────────────────┘    │
│                                                                                  │
│  Configuration:                                                                 │
│  ┌────────────────────────────────────────────────────────────────────────┐    │
│  │  var processor = new ProcessorBuilder()                                 │    │
│  │      .AddSink("kafka", new KafkaSink(                                   │    │
│  │          bootstrapServers: "localhost:9092",                            │    │
│  │          topic: "processed-orders",                                     │    │
│  │          serializer: new JsonSerializer()                               │    │
│  │      ))                                                                  │    │
│  │      .AddSink("postgres", new PostgresSink(                             │    │
│  │          connectionString: "Host=localhost;Database=analytics"          │    │
│  │      ))                                                                  │    │
│  │      .Build();                                                          │    │
│  └────────────────────────────────────────────────────────────────────────┘    │
│                                                                                  │
└─────────────────────────────────────────────────────────────────────────────────┘
```

#### Features Added

| Feature | Interface | Implementation |
|---------|-----------|----------------|
| Sink interface | `ISink` | 2PC contract |
| User-facing collect | `ISinkCollector` | Routes to registered sinks |
| Kafka sink | `KafkaSink` | Kafka transactions |
| Postgres sink | `PostgresSink` | DB transactions |
| S3 sink | `S3Sink` | Atomic multipart upload |
| Sink checkpointing | Sink state in checkpoint | Transaction IDs |
| Sink recovery | Commit pending on restore | Resume transactions |

#### Code Changes Required

**1. Add ISink interface:**

```csharp
public interface ISink : IAsyncDisposable {
    ValueTask WriteAsync<T>(T record, CancellationToken ct);
    ValueTask FlushAsync(CancellationToken ct);
    ValueTask<Snapshot> PrepareAsync(long barrierId, CancellationToken ct);
    ValueTask CommitAsync(long barrierId, CancellationToken ct);
    ValueTask AbortAsync(long barrierId, CancellationToken ct);
    ValueTask RestoreAsync(Snapshot snapshot, CancellationToken ct);
}
```

**2. Add ISinkCollector interface:**

```csharp
public interface ISinkCollector {
    ValueTask Collect<T>(T record, CancellationToken ct = default);
    ValueTask CollectTo<T>(string sinkName, T record, CancellationToken ct = default);
}
```

**3. Add Sinks to ProcessRecordsContext:**

```csharp
public record ProcessRecordsContext(
    // ... existing properties ...
    IStateStore State,
    ISinkCollector Sinks,       // NEW
    ILogger Logger,
    CancellationToken CancellationToken
);
```

**4. Full 2PC barrier handling:**

```csharp
async Task HandleBarrier(Barrier barrier) {
    // Flush pending batch
    await FlushPendingRecords();

    // Prepare all components
    var stateSnapshot = await _stateStore.PrepareAsync(barrier.BarrierId, ct);

    // NEW: Prepare sinks
    var sinkSnapshots = new Dictionary<string, Snapshot>();
    foreach (var (name, sink) in _sinks) {
        await sink.FlushAsync(ct);
        sinkSnapshots[name] = await sink.PrepareAsync(barrier.BarrierId, ct);
    }

    // Store checkpoint (positions + state + sinks)
    await _checkpointStore.StoreAsync(..., stateSnapshot, sinkSnapshots);

    // Ack barrier
    await _gateway.SendBarrierAck(barrier.BarrierId, success: true, progress);

    // Wait for decision
    var decision = await _gateway.WaitForCheckpointDecision(barrier.BarrierId);

    if (decision == Commit) {
        await _stateStore.CommitAsync(barrier.BarrierId, ct);
        foreach (var sink in _sinks.Values)
            await sink.CommitAsync(barrier.BarrierId, ct);
    } else {
        await _stateStore.AbortAsync(barrier.BarrierId, ct);
        foreach (var sink in _sinks.Values)
            await sink.AbortAsync(barrier.BarrierId, ct);
    }
}
```

**5. Sink recovery:**

```csharp
async Task OnWorkOrder(WorkOrder order) {
    var checkpoint = await _checkpointStore.LoadLatestAsync(...);

    // Restore state
    await _stateStore.RestoreAsync(checkpoint.StateSnapshot, ct);

    // NEW: Restore sinks (commit pending transactions)
    foreach (var (name, snapshot) in checkpoint.SinkSnapshots) {
        await _sinks[name].RestoreAsync(snapshot, ct);
    }

    // Continue...
}
```

#### What Users Can Do (Cumulative)

Everything from v1.0 and v1.1, plus:
- ✓ Exactly-once delivery to Kafka
- ✓ Exactly-once delivery to databases
- ✓ Exactly-once delivery to S3/blob storage
- ✓ Multiple sinks per processor
- ✓ Sink transactions coordinated with checkpoints
- ✓ Automatic recovery of pending sink transactions

#### Effort Estimate

```
New code:
- ISink, ISinkCollector interfaces
- SinkCollector implementation (routes to sinks)
- KafkaSink implementation
- PostgresSink implementation (optional for v1.2)
- S3Sink implementation (optional for v1.2)
- 2PC integration in worker
- Sink recovery logic
- Tests

Estimate: 2-4 weeks (with 1-2 sink implementations)
```

---

### Release Timeline Summary

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                           RELEASE TIMELINE                                       │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                  │
│  v1.0 MVP ─────────────────────────────────────────────────────► SHIP NOW       │
│  │                                                                               │
│  │  • Read, process, checkpoint                                                 │
│  │  • Hide State (internal)                                                     │
│  │  • No sinks                                                                  │
│  │  • Effort: ~0-1 day                                                          │
│  │                                                                               │
│  ▼                                                                               │
│  v1.1 ──────────────────────────────────────────────────────────► +2-3 weeks    │
│  │                                                                               │
│  │  • Expose State (IStateStore)                                                │
│  │  • DuckDB backend                                                            │
│  │  • ICheckpointStateStore (opaque bytes)                                      │
│  │  • State checkpoint/recovery                                                 │
│  │  • Effort: 2-3 weeks                                                         │
│  │                                                                               │
│  ▼                                                                               │
│  v1.2 ──────────────────────────────────────────────────────────► +2-4 weeks    │
│                                                                                  │
│     • Add Sinks (ISink, ISinkCollector)                                         │
│     • 2PC integration                                                           │
│     • Kafka sink (minimum)                                                      │
│     • Effort: 2-4 weeks                                                         │
│                                                                                  │
└─────────────────────────────────────────────────────────────────────────────────┘
```

---

### Migration Path for Users

**v1.0 → v1.1**: Non-breaking
- `ctx.State` becomes available
- Existing code continues to work

**v1.1 → v1.2**: Non-breaking
- `ctx.Sinks` becomes available
- Existing code continues to work
- Users can gradually adopt sinks

**API Stability Guarantee**:
- `IRecordProcessor` interface: STABLE from v1.0
- `ProcessRecordsContext`: Additive changes only (new properties)
- Lifecycle hooks: STABLE from v1.0

---

## Part 3: Design Trade-offs

### Option 1 vs Option 2

| Aspect                     | Option 1 (Full Model)              | Option 2 (Simplified)              |
|----------------------------|------------------------------------|------------------------------------|
| **Complexity**             | Higher - more interfaces           | Lower - single interface           |
| **Flexibility**            | Maximum - full control             | Limited - framework controls most  |
| **External sources**       | Full 2PC support                   | Would need extension               |
| **Lines of code**          | ~80-200 per source                 | ~20-40 per source                  |
| **Position serialization** | Source responsibility              | Framework handles                  |
| **Checkpoint hooks**       | Built-in                           | Not needed (framework handles)     |

### Opaque Bytes vs Typed State

**Option 1** uses `ReadOnlyMemory<byte>` for checkpoint state:

| Aspect     | Opaque Bytes                        | Typed State                        |
|------------|-------------------------------------|------------------------------------|
| **Pros**   | Source owns serialization           | Framework can inspect/validate     |
|            | No version compatibility issues     | Type safety                        |
|            | Any format works                    | Easier debugging                   |
| **Cons**   | Framework can't inspect             | Serialization coupling             |
|            | Debugging harder                    | Version migration complexity       |

### IAsyncEnumerable Simplification

Both options use `IAsyncEnumerable<TRecord>` instead of Flink's `pollNext()`/`isAvailable()`:

| Aspect              | Flink (Java)                       | C# (IAsyncEnumerable)              |
|---------------------|------------------------------------|------------------------------------|
| **Backpressure**    | Manual `CompletableFuture`         | Automatic                          |
| **Cancellation**    | Custom implementation              | `CancellationToken` built-in       |
| **Composition**     | Difficult                          | LINQ operators                     |
| **Code complexity** | High (callbacks, futures)          | Low (async/await)                  |

### 2PC Protocol (Option 1 only)

For external sources (Kafka), Option 1's `IPartitionReader` provides 2PC:

1. `SnapshotState(checkpointId)` → capture current position, return opaque bytes
2. Framework stores checkpoint
3. `NotifyCheckpointComplete(checkpointId)` → commit external state (Kafka offsets)
4. `NotifyCheckpointAborted(checkpointId)` → discard pending state

### Lines of Code Comparison

| Source Type                   | Flink (Java)   | Option 1 (C#)  | Option 2 (C#)  |
|-------------------------------|----------------|----------------|----------------|
| Simple static source          | ~200 lines     | ~80 lines      | ~20 lines      |
| Dynamic discovery source      | ~300 lines     | ~120 lines     | ~40 lines      |
| With external commit (Kafka)  | ~400 lines     | ~150 lines     | N/A*           |

*Option 2 doesn't support external commit without extension

---

## Part 3.5: Industry Comparison - Axon Framework

Axon Framework is a mature Java framework for event-sourced, CQRS applications. Their event processing model is highly relevant to KurrentDB Processors.

### Axon Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    AXON FRAMEWORK ARCHITECTURE                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Command Side                         Query Side (Projections)              │
│  ┌─────────────────────┐              ┌─────────────────────────────────┐   │
│  │  Command Bus        │              │  Event Processors               │   │
│  │       │             │              │  ┌─────────────────────────────┐│   │
│  │       ▼             │              │  │ Tracking Event Processor   ││   │
│  │  ┌─────────────┐    │              │  │ • Pull-based               ││   │
│  │  │ Aggregate   │────┼──► Events ──►│  │ • Tracking Token           ││   │
│  │  │ (Command    │    │              │  │ • Replay support           ││   │
│  │  │  Handler)   │    │              │  │ • Co-located token store   ││   │
│  │  └─────────────┘    │              │  └─────────────────────────────┘│   │
│  └─────────────────────┘              │                                 │   │
│                                       │  ┌─────────────────────────────┐│   │
│  Event Store                          │  │ Pooled Streaming Processor ││   │
│  ┌─────────────────────┐              │  │ • Multi-threaded           ││   │
│  │  Axon Server or     │              │  │ • Work coordination        ││   │
│  │  JPA/JDBC Store     │              │  │ • Segment assignment       ││   │
│  └─────────────────────┘              │  └─────────────────────────────┘│   │
│                                       └─────────────────────────────────┘   │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Axon's Event Processor Types

| Type | Description | Use Case |
|------|-------------|----------|
| **Subscribing** | Push-based, no checkpoint | Simple handlers, no replay needed |
| **Tracking** | Pull-based, checkpointed | Projections, replay, catch-up |
| **Pooled Streaming** | Multi-threaded tracking | High throughput, parallel processing |

### Axon's Key Concepts

**Tracking Token (Checkpoint)**:
```java
// Like our RecordPosition
public interface TrackingToken {
    // Opaque position marker
}

public class GlobalSequenceTrackingToken implements TrackingToken {
    private final long globalIndex;
}

public class MultiSourceTrackingToken implements TrackingToken {
    private final Map<String, TrackingToken> tokens;  // Per-source positions
}
```

**Token Store (Checkpoint Store)**:
```java
// Like our ICheckpointStore
public interface TokenStore {
    void storeToken(TrackingToken token, String processorName, int segment);
    TrackingToken fetchToken(String processorName, int segment);
    void extendClaim(String processorName, int segment);  // Lease extension
    void releaseClaim(String processorName, int segment);
}

// Implementations: JpaTokenStore, JdbcTokenStore, InMemoryTokenStore
```

**Segments (Partitions)**:
```
Events distributed across segments by aggregate ID hash:

Segment 0: Events where hash(aggregateId) % 4 == 0
Segment 1: Events where hash(aggregateId) % 4 == 1
Segment 2: Events where hash(aggregateId) % 4 == 2
Segment 3: Events where hash(aggregateId) % 4 == 3
```

### Axon's Claims/Leases-Based Coordination

Axon supports two coordination modes:

**Mode 1: Axon Server (Centralized)**
- Central coordinator assigns segments to processors
- Similar to our Gateway model

**Mode 2: Database Claims (Decentralized)**
- No central coordinator
- Processors claim segments via database tokens
- Claims have TTL and must be extended periodically

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              AXON CLAIMS-BASED COORDINATION                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Token Table (in projection database):                                      │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  processor_name │ segment │ token      │ owner    │ timestamp       │    │
│  │  ───────────────┼─────────┼────────────┼──────────┼─────────────────│    │
│  │  orders         │ 0       │ {pos:1234} │ worker-1 │ 2024-01-15 10:00│    │
│  │  orders         │ 1       │ {pos:5678} │ worker-2 │ 2024-01-15 10:01│    │
│  │  orders         │ 2       │ {pos:9012} │ worker-1 │ 2024-01-15 10:00│    │
│  │  orders         │ 3       │ {pos:3456} │ NULL     │ (unclaimed)     │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  Claim Protocol:                                                             │
│                                                                              │
│  1. CLAIM: Worker attempts to claim unclaimed or expired segment            │
│     ┌─────────────────────────────────────────────────────────────────┐     │
│     │  UPDATE tokens                                                   │     │
│     │  SET owner = 'worker-3', timestamp = NOW()                      │     │
│     │  WHERE processor_name = 'orders'                                 │     │
│     │    AND segment = 3                                               │     │
│     │    AND (owner IS NULL OR timestamp < NOW() - INTERVAL '10s')    │     │
│     └─────────────────────────────────────────────────────────────────┘     │
│                                                                              │
│  2. EXTEND: Owner periodically extends claim (heartbeat)                    │
│     ┌─────────────────────────────────────────────────────────────────┐     │
│     │  UPDATE tokens                                                   │     │
│     │  SET timestamp = NOW()                                          │     │
│     │  WHERE processor_name = 'orders'                                 │     │
│     │    AND segment = 0                                               │     │
│     │    AND owner = 'worker-1'                                        │     │
│     └─────────────────────────────────────────────────────────────────┘     │
│                                                                              │
│  3. RELEASE: Worker releases claim on shutdown                              │
│     ┌─────────────────────────────────────────────────────────────────┐     │
│     │  UPDATE tokens                                                   │     │
│     │  SET owner = NULL                                                │     │
│     │  WHERE processor_name = 'orders'                                 │     │
│     │    AND segment = 0                                               │     │
│     │    AND owner = 'worker-1'                                        │     │
│     └─────────────────────────────────────────────────────────────────┘     │
│                                                                              │
│  4. STEAL: If claim expires, another worker can take it                     │
│     Worker-3 sees segment 0's timestamp is old → claims it                  │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Claim Timeout Logic**:
```java
// Axon's claim extension interval (default 5 seconds)
// Claim timeout (default 10 seconds)

// Worker loop:
while (running) {
    // Process events
    processEvents();

    // Extend claim every N seconds
    if (timeSinceLastExtend > claimExtensionInterval) {
        tokenStore.extendClaim(processorName, segment);
    }
}

// If worker crashes:
// - No more extend calls
// - After 10 seconds, claim expires
// - Another worker can claim the segment
```

**Pros of Claims-Based Coordination**:
- No central coordinator needed (simpler deployment)
- Database is the coordination primitive (already have one)
- Natural failover (expired claims get reassigned)
- Works with any SQL database

**Cons of Claims-Based Coordination**:
- Polling overhead (workers check for unclaimed segments)
- Claim thundering herd (multiple workers race to claim)
- Slower failover (must wait for claim timeout)
- Database load from claim extensions

### Axon's Co-located Token Pattern

Axon stores tokens in the SAME database as projections:

```java
@Configuration
public class AxonConfig {

    @Bean
    public TokenStore tokenStore(EntityManagerFactory emf) {
        // Token store uses SAME EntityManager as projections
        return JpaTokenStore.builder()
            .entityManagerProvider(new SimpleEntityManagerProvider(emf))
            .build();
    }
}
```

**How atomic commit works**:
```java
// Inside TrackingEventProcessor
void processEvents() {
    UnitOfWork unitOfWork = new DefaultUnitOfWork();

    unitOfWork.executeWithResult(() -> {
        // 1. Handle event (update projection)
        eventHandler.handle(event);

        // 2. Store token (in same transaction!)
        tokenStore.storeToken(newToken, processorName, segment);

        // 3. Commit happens here - both projection AND token
        return null;
    });
}
```

### Axon Features Comparison

| Feature | Axon Framework | KurrentDB Processors |
|---------|---------------|---------------------|
| **Event Handlers** | `@EventHandler` annotations | `IRecordProcessor` interface |
| **Checkpoint** | TrackingToken | RecordPosition |
| **Checkpoint Store** | TokenStore (JPA/JDBC) | ICheckpointStore |
| **Co-located Checkpoint** | ✅ Yes (same DB transaction) | ❌ Not yet |
| **Partitioning** | Segments | Partitions |
| **Coordination** | Axon Server OR DB claims | Gateway (server-side) |
| **Multi-threading** | Pooled Streaming | Multiple Workers |
| **Replay** | ✅ Built-in reset API | ✅ Via position reset |
| **State Store** | ❌ Not built-in (use external) | ✅ IStateStore (DuckDB) |
| **Sinks with 2PC** | ❌ Not built-in | ✅ ISink planned |
| **Dead Letter Queue** | ✅ Built-in | ❌ Not yet |
| **Error Policies** | ✅ Configurable | ❌ Not yet |
| **Segment Split/Merge** | ✅ Dynamic scaling | ❌ Not yet |

### Axon Features We Don't Have (For Reference)

**Dead Letter Queue**:
```java
@ProcessingGroup("orders")
@DeadLetterQueue(processingGroup = "orders-dlq")
public class OrderProjection { ... }
```

**Error Handling Policies**:
```java
ProcessingGroup.builder()
    .errorHandler(PropagatingErrorHandler.INSTANCE)  // Fail fast
    .errorHandler(LoggingErrorHandler.INSTANCE)      // Log and continue
    .errorHandler(new RetryingErrorHandler(3))       // Retry N times
    .build();
```

**Reset/Replay API**:
```java
eventProcessor.resetTokens();                  // Reset to beginning
eventProcessor.resetTokens(specificToken);     // Reset to position
```

**Segment Splitting/Merging**:
```java
tokenStore.splitSegment(processorName, segmentId);   // Double parallelism
tokenStore.mergeSegments(processorName, seg1, seg2); // Reduce parallelism
```

### Summary: Axon vs KurrentDB Positioning

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    POSITIONING SUMMARY                                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  AXON: Application framework for ES/CQRS                                    │
│  • Opinionated, full-stack (commands, events, queries, sagas)              │
│  • Java ecosystem                                                           │
│  • Mature projection handling with co-located tokens                        │
│  • No built-in state management for stream processing                       │
│                                                                              │
│  KURRENTDB: Stream processing infrastructure                                │
│  • Focused on processing (not full CQRS framework)                          │
│  • .NET ecosystem                                                           │
│  • Built-in state store (DuckDB) for stream processing patterns            │
│  • Sink abstraction with 2PC for exactly-once delivery                      │
│  • Native integration with KurrentDB event store                            │
│                                                                              │
│  LEARNING OPPORTUNITIES (no decisions made):                                │
│  • Co-located checkpoint pattern (Axon's strength)                          │
│  • Claims-based coordination (alternative to Gateway)                       │
│  • Dead Letter Queue pattern                                                │
│  • Error handling policies                                                  │
│  • Segment split/merge for dynamic scaling                                  │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Part 4: Open Questions

### For Design Discussion

1. **Option 1 vs Option 2** - Which model fits KurrentDB best?
   - Option 1 provides maximum flexibility and external source support
   - Option 2 is simpler but may need extension for Kafka-style sources

2. **Do we need external source support (Kafka) now?**
   - If yes: Option 1 is required for 2PC commit
   - If no: Option 2 is sufficient and simpler

3. **Dynamic partition discovery scope?**
   - Static discovery covers most use cases
   - Dynamic adds complexity but enables auto-scaling scenarios

4. **Incremental checkpoints roadmap?**
   - Full snapshots work for now
   - Incremental would reduce checkpoint I/O for large state

### Implementation Considerations

5. **Position serialization format?**
   - Binary (compact, fast)
   - JSON (debuggable)
   - Protobuf (versioned)

6. **Worker failure during checkpoint?**
   - Timeout and reassign (current design)
   - Retry with backoff?

7. **Multi-source support future?**
   - Option 1 naturally extends
   - Option 2 would need redesign

---

## Summary

| Component         | Responsibility                                                      |
|-------------------|---------------------------------------------------------------------|
| **IRecordSource** | Read records, extract positions. Source implementers write this.    |
| **ISource**       | (Option 1) Full source + partition reader with 2PC checkpoint hooks |
| **ProcessorWorker** | Coordinates barriers, tracks positions, executes IRecordProcessor |
| **CheckpointStore** | Store/load positions by barrierId                                 |

**Key Design Principles:**

1. **IAsyncEnumerable everywhere** - natural backpressure, clean async/await
2. **Source owns position format** - opaque bytes, no framework coupling
3. **Worker merges streams** - records from source + barriers from server
4. **Barrier flows through** - snapshots positions when received, checkpoint when it emerges
5. **Single source simplifies** - no barrier alignment, no operator DAGs

---

## References

1. Chandy, K.M. and Lamport, L. (1985). "Distributed Snapshots: Determining Global States of Distributed Systems". ACM TOCS, 3(1), 63-75.

2. Carbone, P., et al. (2015). "Lightweight Asynchronous Snapshots for Distributed Dataflows". arXiv:1506.08603

3. Apache Flink Documentation - Checkpointing: https://nightlies.apache.org/flink/flink-docs-stable/docs/ops/state/checkpoints/

4. Apache Flink Source API: https://nightlies.apache.org/flink/flink-docs-stable/docs/dev/datastream/sources/
