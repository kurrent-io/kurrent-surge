# Feature Spec: Kurrent Processors

---

## Document Control

| Field                | Value                                             |
|----------------------|---------------------------------------------------|
| **Feature**          | Kurrent Processors                                |
| **Author(s)**        | Sergio Silveira                                   |
| **Status**           | Draft                                             |
| **Last Updated**     | January 2026                                      |
| **Architecture Doc** | `_bmad-output/design/processor-gateway.md`        |
| **PRD Reference**    | `product-brief.md` (same directory)               |
| **Related Specs**    | `.project/reference/features/processors.md`       |

---

## 1. Overview

### 1.1 Summary

Kurrent Processors enables distributed, fault-tolerant stream processing over the KurrentDB log. Workers connect to process records in parallel while maintaining exactly-once state semantics through coordinated checkpointing based on the Chandy-Lamport algorithm.

The system follows a **"Hive Mind" model**: workers are autonomous agents that connect and disconnect freely. The processor emerges from workers coordinating through the server. This eliminates the need for a separate job manager or cluster — processing coordination is embedded directly in KurrentDB.

**Key insight**: The Client/SDK becomes server-agnostic through a Ports & Adapters pattern. When the real server protocol lands, users swap `LegacyGateway` for `GrpcGateway` — worker code requires zero changes.

### 1.2 Goals & Non-Goals

See `product-brief.md` for business goals and success metrics.

**Technical goals for this spec:**

| Goal                    | Metric                    | Target                                            |
|-------------------------|---------------------------|---------------------------------------------------|
| Processing Latency      | Record delivery latency   | < 10ms from write to worker receipt               |
| API Simplicity          | Lines of code to start    | < 20 LOC for basic processor                      |
| Protocol Portability    | Code changes on swap      | Zero worker code changes when swapping gateways   |

### 1.3 User Stories

| ID   | User Story                                                                                           | Acceptance Criteria                            |
|------|------------------------------------------------------------------------------------------------------|------------------------------------------------|
| US-1 | As a data engineer, I want to connect a worker to process records so that I can build projections    | Worker receives records after connection       |
| US-2 | As a data engineer, I want automatic checkpoints so that I can recover from failures                 | Recovery resumes from last checkpoint position |
| US-3 | As a data engineer, I want records batched for efficiency so that I can process at high throughput   | Batches respect size/timeout configuration     |
| US-4 | As a data engineer, I want a simple API so that I can start processing in minutes                    | `IRecordProcessor` interface with defaults     |
| US-5 | As a DevOps engineer, I want graceful shutdown so that no progress is lost when scaling down         | `Disconnect` message saves final positions     |

---

## 2. Component Overview

### 2.1 Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    ProcessorWorker (User-Facing)                 │
│  ─────────────────────────────────────────────────────────────  │
│  • Orchestrates connection lifecycle                             │
│  • Tracks positions via PositionTracker                          │
│  • Calls user's IRecordProcessor methods                         │
│  • Handles checkpoints and graceful shutdown                     │
│                                                                  │
│  KNOWS ONLY: IWorkerSession                                      │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                       IWorkerSession                             │
│  ─────────────────────────────────────────────────────────────  │
│  THE COMPLETE ABSTRACTION FOR THE WORKER                         │
│                                                                  │
│  • IAsyncEnumerable<ProcessorControlMessage> (batched records)   │
│  • SendAsync(WorkerMessage) (outbound, delegates to gateway)     │
│  • SessionId (unique identifier for this session)                │
│                                                                  │
│  IMPLEMENTATIONS:                                                │
│    LegacyWorkerSession → batches from channel, delegates send    │
│    GrpcWorkerSession   → bidirectional gRPC stream (future)      │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ (created by)
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      IProcessorGateway                           │
│  ─────────────────────────────────────────────────────────────  │
│  CONNECTION FACTORY                                              │
│                                                                  │
│  • ConnectAsync(settings) → creates IWorkerSession               │
│                                                                  │
│  IMPLEMENTATIONS:                                                │
│    LegacyGateway  → StreamsClient + local channel (V1)           │
│    GrpcGateway    → Bidirectional gRPC (future V2)               │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 Component Responsibilities

| Component             | Responsibility                                                              |
|-----------------------|-----------------------------------------------------------------------------|
| **ProcessorWorker**   | High-level orchestrator; manages lifecycle, position tracking, user callbacks |
| **IWorkerSession**    | Bidirectional communication interface; inbound messages + outbound sends    |
| **IProcessorGateway** | Connection factory; establishes sessions, handles connection errors         |
| **LegacyGateway**     | V1 implementation; uses StreamsClient, simulates server protocol locally    |
| **LegacyWorkerSession** | Batches records from channel, handles WorkOrder/WorkerReady handshake     |
| **PositionTracker**   | Tracks processed positions per partition for checkpointing                  |
| **PartitionCalculator** | Deterministic stream→partition hash (XXHash-based)                        |
| **IRecordProcessor**  | User-implemented interface for processing logic                             |

---

## 3. Detailed Design

### 3.1 Gateway & Session Architecture

The server abstraction layer uses a two-part interface:

1. **IProcessorGateway** — Connection factory that establishes sessions
2. **IWorkerSession** — Runtime communication interface for message exchange

This separation mirrors the typical connection lifecycle: connect once, then communicate many times. The gateway handles connection concerns (authentication, endpoint discovery, subscription setup), while the session handles the streaming protocol.

**Gateway implementations are swappable:**
- `LegacyGateway` + `LegacyWorkerSession` — Uses V1 gRPC Streams API, simulates server locally
- `GrpcGateway` + `GrpcWorkerSession` (future) — Uses V2 bidirectional gRPC with real server

### 3.2 Message Protocol

Messages use **IVariant** discriminated unions for exhaustive pattern matching.

#### Server → Worker Messages (ProcessorControlMessage)

| Message             | Purpose                                    | Sent When                     |
|---------------------|--------------------------------------------|-------------------------------|
| `WorkOrder`         | Assigns partitions to worker               | After connection established  |
| `RecordBatch`       | Batch of records to process                | After WorkerReady received    |
| `CheckpointBarrier` | Signals checkpoint; worker must respond    | Trigger condition met         |

#### Worker → Server Messages (WorkerMessage)

| Message              | Purpose                                   | Sent When                     |
|----------------------|-------------------------------------------|-------------------------------|
| `WorkerReady`        | Signals ready to receive records          | After loading checkpoint      |
| `CheckpointComplete` | Acknowledges checkpoint with positions    | After state snapshot          |
| `Disconnect`         | Requests graceful disconnect              | Shutdown or error             |

### 3.3 Checkpoint Protocol (Chandy-Lamport)

The checkpoint mechanism is based on the Chandy-Lamport distributed snapshot algorithm:

1. **Barrier injection**: Gateway emits `CheckpointBarrier` based on triggers
2. **Flush-then-barrier**: Pending records are flushed before barrier is yielded
3. **Worker snapshot**: Worker saves state when barrier arrives
4. **Acknowledgment**: Worker sends `CheckpointComplete` with positions

**Supported triggers (LegacyGateway):**
- **Interval** — Time-based (default: 30 seconds)
- **Threshold** — Record count (default: 10,000 records)
- **CaughtUp** — Stream reached live edge (once per lifecycle)
- **Rebalance** — No-op in single worker mode

**NOT supported (requires server):**
- **Savepoint** — Manual checkpoint via server RPC

### 3.4 Position Model

Every record has both global and stream-local positions:

| Position Type      | Scope        | Description                                    |
|--------------------|--------------|------------------------------------------------|
| **LogPosition**    | Global       | Absolute position in $all log                  |
| **StreamRevision** | Stream-local | Sequential position within a specific stream   |

**Key principle**: `LogPosition` is the universal cursor. Stream selection is handled via filters, not position types.

**Checkpoint positions**: Workers report per-partition positions. The barrier position is the max of all partition positions (the watermark).

```
Positions = {0: 1050, 1: 2030, 2: 850}
BarrierId = max(1050, 2030, 850) = 2030  // The watermark
```

---

## 4. Sequence Diagrams

### 4.1 Connection Flow

```
┌────────┐     ┌──────────────┐     ┌──────────────────┐
│ Worker │     │ LegacyGateway│     │ LegacyWorkerSession │
└───┬────┘     └──────┬───────┘     └────────┬─────────┘
    │                 │                      │
    │ ConnectAsync()  │                      │
    │────────────────►│                      │
    │                 │                      │
    │                 │ Create subscription  │
    │                 │ Start PumpPump       │
    │                 │ Start BarrierTimer   │
    │                 │                      │
    │                 │ Create session       │
    │                 │─────────────────────►│
    │                 │                      │
    │◄────────────────│                      │
    │ IWorkerSession  │                      │
    │                 │                      │
    │ GetEnumerator() │                      │
    │─────────────────┼─────────────────────►│
    │                 │                      │
    │◄────────────────┼──────────────────────│
    │ WorkOrder       │                      │
    │                 │                      │
    │ (load checkpoint)                      │
    │                 │                      │
    │ SendAsync(WorkerReady)                 │
    │─────────────────┼─────────────────────►│
    │                 │                      │
    │                 │ (unblock enumeration)│
    │                 │                      │
    │◄────────────────┼──────────────────────│
    │ RecordBatch     │                      │
```

### 4.2 Record Processing Flow

```
┌────────┐     ┌──────────────────┐     ┌──────────────┐
│ Worker │     │ LegacyWorkerSession │   │ LegacyGateway│
└───┬────┘     └────────┬─────────┘     └──────┬───────┘
    │                   │                      │
    │                   │◄─────────────────────│
    │                   │ Record (via channel) │
    │                   │◄─────────────────────│
    │                   │ Record               │
    │                   │◄─────────────────────│
    │                   │ Record               │
    │                   │                      │
    │                   │ (batch size/timeout) │
    │                   │                      │
    │◄──────────────────│                      │
    │ RecordBatch([r1,r2,r3])                  │
    │                   │                      │
    │ processor.ProcessAsync()                 │
    │ Track positions   │                      │
    │                   │                      │
    │◄──────────────────│                      │
    │ RecordBatch([r4,r5])                     │
    │                   │                      │
```

### 4.3 Checkpoint Barrier Flow

```
┌────────┐     ┌──────────────────┐     ┌──────────────┐
│ Worker │     │ LegacyWorkerSession │   │ LegacyGateway│
└───┬────┘     └────────┬─────────┘     └──────┬───────┘
    │                   │                      │
    │                   │                      │ Timer fires
    │                   │◄─────────────────────│
    │                   │ CheckpointBarrier    │
    │                   │                      │
    │                   │ (flush pending first)│
    │                   │                      │
    │◄──────────────────│                      │
    │ RecordBatch([...]) (if any pending)      │
    │                   │                      │
    │◄──────────────────│                      │
    │ CheckpointBarrier(id=1000, INTERVAL)     │
    │                   │                      │
    │ processor.OnCheckpointAsync()            │
    │ Snapshot state    │                      │
    │ Store checkpoint  │                      │
    │ Flush tracker     │                      │
    │                   │                      │
    │ SendAsync(CheckpointComplete)            │
    │──────────────────►│                      │
    │                   │─────────────────────►│
    │                   │                      │
```

### 4.4 Graceful Disconnect Flow

```
┌────────┐     ┌──────────────────┐     ┌──────────────┐
│ Worker │     │ LegacyWorkerSession │   │ LegacyGateway│
└───┬────┘     └────────┬─────────┘     └──────┬───────┘
    │                   │                      │
    │ SIGTERM received  │                      │
    │                   │                      │
    │ processor.OnShutdownAsync()              │
    │ Store final checkpoint                   │
    │ Flush tracker     │                      │
    │                   │                      │
    │ SendAsync(Disconnect)                    │
    │──────────────────►│                      │
    │                   │─────────────────────►│
    │                   │                      │ Cancel pumps
    │                   │                      │ Complete channel
    │                   │                      │
    │                   │ (enumeration ends)   │
    │                   │                      │
    │ DisposeAsync()    │                      │
    │──────────────────►│                      │
```

---

## 5. API Contracts

### 5.1 Core Interfaces

#### IProcessorGateway

```csharp
/// <summary>
/// Connection factory for processor gateway implementations.
/// </summary>
public interface IProcessorGateway : IAsyncDisposable {
    /// <summary>
    /// Establishes a bidirectional session with the gateway.
    /// </summary>
    ValueTask<IWorkerSession> ConnectAsync(ProcessorSettings settings, CancellationToken ct);
}
```

#### IWorkerSession

```csharp
/// <summary>
/// Bidirectional session between worker and gateway.
/// </summary>
public interface IWorkerSession : IAsyncEnumerable<ProcessorControlMessage>, IAsyncDisposable {
    /// <summary>Unique identifier for this session.</summary>
    SessionId SessionId { get; }

    /// <summary>Sends a message to the gateway.</summary>
    ValueTask SendAsync(WorkerMessage message);
}
```

#### IRecordProcessor

```csharp
/// <summary>
/// User-implemented interface for processing records.
/// </summary>
public interface IRecordProcessor {
    /// <summary>Called when processor starts.</summary>
    ValueTask OnInitialize(InitializationContext context) => ValueTask.CompletedTask;

    /// <summary>Called when records are available. MUST implement.</summary>
    ValueTask ProcessRecords(ProcessRecordsContext context);

    /// <summary>Called when checkpoint occurs.</summary>
    ValueTask OnCheckpoint(CheckpointContext context) => ValueTask.CompletedTask;

    /// <summary>Called when partitions change.</summary>
    ValueTask OnPartitionsAssigned(PartitionsAssignedContext context) => ValueTask.CompletedTask;

    /// <summary>Called on shutdown.</summary>
    ValueTask OnShutdown(ShutdownContext context) => ValueTask.CompletedTask;
}
```

### 5.2 Message Types

```csharp
// Server → Worker
public readonly record struct WorkOrder(SessionId SessionId, ImmutableArray<Partition> Partitions);
public readonly record struct RecordBatch : IReadOnlyList<Record>;
public readonly record struct CheckpointBarrier(LogPosition BarrierId, CheckpointBarrierTrigger Trigger, DateTimeOffset Timestamp);

public readonly partial record struct ProcessorControlMessage : IVariant<WorkOrder, RecordBatch, CheckpointBarrier>;

// Worker → Server
public readonly record struct WorkerReady(LogPosition BarrierId, PartitionPositions Positions);
public readonly record struct CheckpointComplete(LogPosition BarrierId, PartitionPositions Positions);
public readonly record struct Disconnect(DisconnectReason Reason, PartitionPositions Positions);

public readonly partial record struct WorkerMessage : IVariant<Connect, WorkerReady, CheckpointComplete, Disconnect>;
```

### 5.3 Builder API

```csharp
public static class KurrentProcessors {
    public static KurrentProcessorBuilder Create(IRecordProcessor processor);
}

public sealed class KurrentProcessorBuilder {
    // Connection
    public KurrentProcessorBuilder ConnectTo(string connectionString);
    public KurrentProcessorBuilder UseClient(KurrentClient client);

    // Identity
    public KurrentProcessorBuilder WithProcessorId(string processorId);
    public KurrentProcessorBuilder WithWorkerId(string workerId);

    // Subscription
    public KurrentProcessorBuilder WithFilter(IRecordFilter filter);
    public KurrentProcessorBuilder WithInitialPosition(SubscriptionInitialPosition position);

    // Configuration
    public KurrentProcessorBuilder WithBatching(BatchingSettings settings);
    public KurrentProcessorBuilder WithCheckpoint(CheckpointSettings settings);
    public KurrentProcessorBuilder WithMaxParallelism(int maxParallelism);

    // Execution
    public ValueTask RunAsync(CancellationToken stoppingToken = default);
    public IProcessorWorker Build();
}
```

### 5.4 Error Responses

| Error                          | Condition                      | Recovery                        |
|--------------------------------|--------------------------------|---------------------------------|
| `StreamNotFoundException`      | Target stream doesn't exist    | Create stream or change filter  |
| `StreamTombstonedException`    | Stream was hard-deleted        | Cannot recover                  |
| `LogPositionNotFoundException` | Start position invalid         | Use Earliest/Latest             |
| `OperationCanceledException`   | Cancellation requested         | Retry if appropriate            |

---

## 6. Data Models

### 6.1 Position Types

```csharp
public readonly record struct LogPosition {
    public long Value { get; init; }
    public static readonly LogPosition Earliest = new(0);
    public static readonly LogPosition Latest = new(long.MaxValue);
    public static readonly LogPosition Unset = new(-1000);
}

public readonly record struct Partition {
    public int Value { get; init; }
    public static readonly Partition Any = new(-1);
}

public readonly record struct PartitionPositions : IReadOnlyDictionary<Partition, LogPosition> {
    public static readonly PartitionPositions Empty;
    public LogPosition MaxPosition { get; }  // The watermark
}
```

### 6.2 Configuration

```csharp
public record ProcessorSettings {
    public required string ProcessorId { get; init; }
    public required string WorkerId { get; init; }
    public IRecordFilter Filter { get; init; } = RecordFilter.None;
    public SubscriptionInitialPosition InitialPosition { get; init; } = SubscriptionInitialPosition.Earliest;
    public BatchingSettings Batching { get; init; } = BatchingSettings.Default;
    public CheckpointSettings Checkpoint { get; init; } = CheckpointSettings.Default;
    public int MaxParallelism { get; init; } = 128;
}

public record BatchingSettings(int MaxSize = 1000, TimeSpan MaxDelay = default);

public record CheckpointSettings(
    TimeSpan Interval = default,       // Default: 30s
    int Threshold = 10000,             // Records between checkpoints
    TimeSpan Timeout = default,        // Default: 60s
    int RetentionCount = 3             // Checkpoints to keep
);
```

---

## 7. Dependencies

### 7.1 Internal Dependencies

| Dependency          | Type    | Purpose                          |
|---------------------|---------|----------------------------------|
| StreamsClient       | Client  | Reads from $all via gRPC V1      |
| PositionTracker     | Helper  | Tracks positions for checkpoint  |
| PartitionCalculator | Helper  | Stream → partition hash          |
| PartitionAssigner   | Helper  | Partition → worker assignment    |

### 7.2 External Dependencies

| Dependency     | Type    | Purpose                      | Fallback                |
|----------------|---------|------------------------------|-------------------------|
| KurrentDB gRPC | Service | Record subscription          | None (required)         |
| User storage   | Storage | Checkpoint persistence       | In-memory (testing)     |

---

## 8. Error Handling

### 8.1 Connection-Time Errors

| Error                    | Cause                     | Behavior                    |
|--------------------------|---------------------------|-----------------------------|
| Stream Not Found         | Target stream missing     | Throw `StreamNotFoundException` |
| Stream Tombstoned        | Stream hard-deleted       | Throw `StreamTombstonedException` |
| Invalid Position         | Start position invalid    | Throw `LogPositionNotFoundException` |
| Cancelled                | Token cancelled           | Throw `OperationCanceledException` |

### 8.2 Runtime Errors

| Error              | Cause                    | Behavior                    |
|--------------------|--------------------------|-----------------------------|
| gRPC Cancelled     | Server disconnected      | Channel completes normally  |
| Operation Cancelled| CancellationToken fired  | Channel completes normally  |
| Generic Exception  | Unexpected pump error    | Channel completes with error|

### 8.3 Checkpoint Failure Policies

| Policy           | Behavior                                    |
|------------------|---------------------------------------------|
| **Continue**     | Disconnect failed worker, resume from last good checkpoint |
| **FailProcessor**| Disconnect ALL workers, requires manual restart |

---

## 9. Testing Strategy

### 9.1 Test Coverage Goals

| Test Type   | Coverage Target | Focus Areas                                |
|-------------|-----------------|-------------------------------------------|
| Unit        | 80%             | Message types, position tracking, batching |
| Integration | Key paths       | Gateway connection, checkpoint lifecycle  |
| E2E         | Critical flows  | Full processing with real KurrentDB       |

### 9.2 Key Test Scenarios

| Scenario                                | Type        | Priority |
|-----------------------------------------|-------------|----------|
| Connect and receive records             | E2E         | High     |
| Checkpoint saves and recovers position  | Integration | High     |
| Batching respects size and timeout      | Unit        | High     |
| Graceful disconnect preserves progress  | Integration | High     |
| Connection error handling               | Integration | Medium   |
| Position tracker accuracy               | Unit        | Medium   |

---

## 10. Rollout Plan

### 10.1 Phase 1: V2 Preview (Single Worker)

| Milestone              | Description                                    | Target       |
|------------------------|------------------------------------------------|--------------|
| LegacyGateway complete | Full gateway + session implementation          | January 2026 |
| Internal testing       | Team uses for internal projections             | January 2026 |
| Documentation          | API docs, getting started guide                | February 2026|
| Preview release        | Available in SDK, marked preview               | February 2026|

**Scope:**
- Single worker per processor
- LegacyGateway (client-side protocol simulation)
- Checkpointing (interval, threshold, caught-up)
- Position tracking and recovery
- Fluent builder API

### 10.2 Phase 2: Full V2 (Multi-Worker)

| Milestone               | Description                                   | Target    |
|-------------------------|-----------------------------------------------|-----------|
| Server protocol design  | Define gRPC bidirectional stream protocol     | Q1 2026   |
| GrpcGateway impl        | Real server-side coordination                 | Q2 2026   |
| Multi-worker testing    | Elastic scaling, rebalancing                  | Q2 2026   |
| GA release              | Full feature set, production ready            | Q3 2026   |

**Scope:**
- Multiple workers per processor
- GrpcGateway (real server protocol)
- Elastic scaling (workers join/leave)
- Rebalance checkpoints
- Shared state store
- Savepoint API

---

## 11. Open Questions

See `product-brief.md` for product-level open questions and risks.

**Technical open questions:**

| Question                                                  | Owner  | Status |
|-----------------------------------------------------------|--------|--------|
| Checkpoint storage interface design for multi-worker?     | Team   | Open   |
| Should `IWorkerSession` expose raw `ChannelReader<T>`?    | Team   | Open   |
| Backpressure strategy when worker processing is slow?     | Team   | Open   |

---

## 12. Related ADRs

| ADR | Decision | Date |
|-----|----------|------|
| (To be created) | Gateway abstraction pattern | January 2026 |
| (To be created) | Chandy-Lamport simplification for single-source | January 2026 |

---

## 13. References

| Document                           | Location                                           |
|------------------------------------|----------------------------------------------------|
| Product Brief                      | `product-brief.md` (same directory)                |
| LegacyGateway Design               | `_bmad-output/design/processor-gateway.md`         |
| Full Feature Design                | `.project/reference/features/processors.md`        |
| Checkpointing Research             | `_bmad-output/research/checkpointing-guide.md`     |
| Flink Comparison                   | `_bmad-output/research/flink-comparison.md`        |
| Parallelism & Key Groups           | `_bmad-output/research/parallelism-key-groups.md`  |
| Position Model                     | `_bmad-output/research/position-model.md`          |
| Design Overview                    | `_bmad-output/research/design.md`                  |

---

## Appendix A: Comparison with Apache Flink

| Aspect                | Apache Flink                    | Kurrent Processors              |
|-----------------------|---------------------------------|---------------------------------|
| Deployment            | Standalone cluster              | Embedded in KurrentDB           |
| Worker management     | Server controls workers         | Workers are autonomous          |
| Data source           | Multiple (Kafka, files, etc.)   | Single source ($all log)        |
| Processing model      | Operator DAGs                   | Simple worker model             |
| Barrier alignment     | Required for exactly-once       | Not needed (single source)      |
| State backend         | RocksDB, heap, etc.             | Pluggable shared store          |

**Key insight**: KurrentDB has a simpler topology than general distributed systems. Single source log means no barrier alignment complexity.
