# Kurrent Processors: Sinks & Collectors

**Status:** Draft  
**Author:** Sérgio Silveira  
**Team:** Developer Experience  
**Created:** 2026-01-16  
**Last Updated:** 2026-01-16

---

## Version History

| Version | Date       | Author          | Changes       |
|---------|------------|-----------------|---------------|
| 0.1     | 2026-01-16 | Sérgio Silveira | Initial draft |

---

## 1. Summary

This specification defines the sink and collector abstractions for Kurrent Processors, enabling users to emit processed records to external systems with configurable delivery guarantees (at-least-once or exactly-once).

**Key design principle:** Users configure sinks at startup and emit records via a collector. The runtime handles all routing and checkpoint coordination transparently.

---

## 2. Problem Statement

Currently, Kurrent Processors handles reading from KurrentDB and checkpointing positions, but provides no abstraction for writing to external systems. Users must:

1. Manually manage connections to external systems (Kafka, databases, etc.)
2. Coordinate writes with the checkpoint lifecycle themselves
3. Implement their own exactly-once semantics if needed
4. Handle recovery of uncommitted writes after failures

This leads to:
- Boilerplate code in every processor
- Error-prone checkpoint coordination
- Inconsistent delivery guarantee implementations
- No standard pattern for exactly-once delivery

---

## 3. Goals

1. **Simple user experience** - Users emit records to a collector; sinks are configured separately at startup
2. **Pluggable sinks** - Support multiple sink types (Kafka, HTTP, databases, etc.)
3. **Configurable guarantees** - At-least-once (flush on checkpoint) and exactly-once (two-phase commit)
4. **Transparent coordination** - Runtime handles all checkpoint coordination with sinks
5. **Recovery handling** - Automatic commit of uncommitted transactions on startup

## 4. Non-Goals

1. **Operator DAGs** - No intermediate operators between processor and sinks (unlike Flink)
2. **Sink-side transformations** - Sinks receive records as-is; transform in processor
3. **Multi-output types** - Single output type per processor (use discriminated union if needed)
4. **Sink discovery** - Sinks are explicitly configured, not auto-discovered

---

## 5. Design Overview

### 5.1 Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  STARTUP                                                                    │
│                                                                             │
│  KurrentProcessors.Create<TOutput>(processor)                               │
│      .AddSink(sink1)    ◄── At-least-once                                   │
│      .AddSink(sink2)    ◄── Exactly-once                                    │
│      .Build()                                                               │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  RUNTIME                                                                    │
│                                                                             │
│  IRecordProcessor<T>  ───►  ICollector<T>  ───►  SinkCoordinator<T>        │
│     (user code)            (emit here)            (internal)                │
│                                                        │                    │
│                                          ┌─────────────┴─────────────┐      │
│                                          ▼                           ▼      │
│                                     ISink<T>              ITransactionalSink│
│                                   (at-least-once)           (exactly-once)  │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 5.2 Checkpoint Flow

```
Barrier arrives from server
         │
         ▼
┌─────────────────────────────────────────┐
│ SinkCoordinator.PrepareCheckpointAsync()│
│                                         │
│  For each ISink:                        │
│    └── FlushAsync()                     │
│                                         │
│  For each ITransactionalSink:           │
│    └── PrepareCommitAsync() ──► committable
└─────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────┐
│ Store checkpoint                        │
│  - Partition positions                  │
│  - Sink committables                    │
└─────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────┐
│ SinkCoordinator.CommitAsync()           │
│                                         │
│  For each ITransactionalSink:           │
│    └── CommitAsync(committable)         │
│        (messages now visible)           │
└─────────────────────────────────────────┘
         │
         ▼
    Continue processing
```

### 5.3 Recovery Flow

On startup, if checkpoint contains uncommitted committables:

```
Load checkpoint
      │
      ▼
┌─────────────────────────────────────────┐
│ SinkCoordinator.CommitAsync(restored)   │
│                                         │
│  Commit any uncommitted transactions    │
│  (idempotent - safe to replay)          │
└─────────────────────────────────────────┘
      │
      ▼
Resume normal processing
```

---

## 6. API Design

### 6.1 User-Facing: IRecordProcessor<TOutput>

```csharp
public interface IRecordProcessor<TOutput> {
    ValueTask OnInitialize(InitializationContext context) => ValueTask.CompletedTask;
    
    /// <summary>
    /// Process records and emit output via collector.
    /// </summary>
    ValueTask ProcessRecords(ProcessRecordsContext<TOutput> context);
    
    ValueTask OnCheckpointComplete(CheckpointCompleteContext context) => ValueTask.CompletedTask;
    ValueTask OnAssignmentChange(AssignmentChangeContext context) => ValueTask.CompletedTask;
    ValueTask OnShutdown(ShutdownContext context) => ValueTask.CompletedTask;
}
```

### 6.2 User-Facing: ICollector<TRecord>

```csharp
public interface ICollector<in TRecord> {
    /// <summary>
    /// Emit a record to all configured sinks.
    /// </summary>
    ValueTask EmitAsync(TRecord record, CancellationToken ct = default);
    
    /// <summary>
    /// Emit multiple records to all configured sinks.
    /// </summary>
    ValueTask EmitAsync(IEnumerable<TRecord> records, CancellationToken ct = default);
}
```

### 6.3 User-Facing: ProcessRecordsContext<TOutput>

```csharp
public sealed record ProcessRecordsContext<TOutput>(
    IReadOnlyList<Record> Records,
    ICollector<TOutput> Collector,
    CancellationToken CancellationToken
);
```

### 6.4 Sink Implementer: ISink<TRecord>

```csharp
/// <summary>
/// At-least-once sink. Flush on checkpoint, may duplicate on replay.
/// </summary>
public interface ISink<in TRecord> : IAsyncDisposable {
    string SinkId { get; }
    ValueTask WriteAsync(TRecord record, CancellationToken ct = default);
    ValueTask FlushAsync(CancellationToken ct = default);
}
```

### 6.5 Sink Implementer: ITransactionalSink<TRecord>

```csharp
/// <summary>
/// Exactly-once sink. Two-phase commit coordinated by runtime.
/// </summary>
public interface ITransactionalSink<in TRecord> : ISink<TRecord> {
    /// <summary>
    /// Called on barrier. Flush and return committable metadata.
    /// </summary>
    ValueTask<SinkCommittable?> PrepareCommitAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Called after checkpoint stored. Finalize staged data.
    /// MUST be idempotent.
    /// </summary>
    ValueTask CommitAsync(SinkCommittable committable, CancellationToken ct = default);
    
    /// <summary>
    /// Called if checkpoint fails.
    /// </summary>
    ValueTask AbortAsync(CancellationToken ct = default);
}
```

### 6.6 SinkCommittable

```csharp
public sealed record SinkCommittable(
    string SinkId,
    ReadOnlyMemory<byte> Payload
);
```

### 6.7 Builder API

```csharp
public static class KurrentProcessors {
    public static KurrentProcessorBuilder<TOutput> Create<TOutput>(
        IRecordProcessor<TOutput> processor);
}

public sealed class KurrentProcessorBuilder<TOutput> {
    public KurrentProcessorBuilder<TOutput> ConnectTo(string connectionString);
    public KurrentProcessorBuilder<TOutput> WithProcessorId(string processorId);
    public KurrentProcessorBuilder<TOutput> WithWorkerId(string workerId);
    public KurrentProcessorBuilder<TOutput> WithSettings(ProcessorSettings settings);
    public KurrentProcessorBuilder<TOutput> WithLoggerFactory(ILoggerFactory loggerFactory);
    
    /// <summary>
    /// Add a sink for output records.
    /// </summary>
    public KurrentProcessorBuilder<TOutput> AddSink(ISink<TOutput> sink);
    
    public ProcessorWorker<TOutput> Build();
}
```

---

## 7. Sink Implementations

### 7.1 Kafka Sink (At-Least-Once)

| Aspect     | Details                                  |
|------------|------------------------------------------|
| Guarantee  | At-least-once                            |
| Behavior   | Fire-and-forget, flush on checkpoint     |
| Duplicates | Possible on replay after failure         |
| Use case   | Audit logs, metrics, non-critical events |

### 7.2 Kafka Transactional Sink (Exactly-Once)

| Aspect       | Details                                         |
|--------------|-------------------------------------------------|
| Guarantee    | Exactly-once                                    |
| Behavior     | Kafka transactions, 2PC with checkpoint         |
| Duplicates   | None (transactional)                            |
| Use case     | Business events, financial transactions         |
| Requirements | Kafka 0.11+, unique `transactional.id` per sink |

### 7.3 Future Sinks (Not in Scope)

- HTTP/Webhook sink (at-least-once)
- Database sink (at-least-once or exactly-once via staging table)
- Outbox sink (exactly-once via outbox pattern)
- File sink (exactly-once via temp file rename)
- KurrentDB sink (exactly-once via idempotent append)

---

## 8. Internal Components

### 8.1 SinkCoordinator<TRecord>

Internal runtime component that:
- Implements `ICollector<TRecord>` for user code
- Routes emitted records to all registered sinks
- Coordinates checkpoint operations across sinks
- Handles recovery of uncommitted transactions

```csharp
internal sealed class SinkCoordinator<TRecord> : ICollector<TRecord>, IAsyncDisposable {
    void AddSink(ISink<TRecord> sink);
    
    // ICollector<TRecord>
    ValueTask EmitAsync(TRecord record, CancellationToken ct = default);
    ValueTask EmitAsync(IEnumerable<TRecord> records, CancellationToken ct = default);
    
    // Checkpoint coordination (called by ProcessorWorker)
    ValueTask<IReadOnlyList<SinkCommittable>> PrepareCheckpointAsync(CancellationToken ct);
    ValueTask CommitAsync(IReadOnlyList<SinkCommittable> committables, CancellationToken ct);
    ValueTask AbortAsync(CancellationToken ct);
}
```

### 8.2 Checkpoint Storage Changes

Checkpoints must now include sink committables:

```csharp
public sealed record Checkpoint(
    LogPosition CheckpointId,
    IReadOnlyDictionary<int, LogPosition> PartitionPositions,
    IReadOnlyList<SinkCommittable> Committables  // NEW
);
```

---

## 9. Usage Examples

### 9.1 Basic Usage

```csharp
// Processor - just emit to collector
public class OrderProcessor : IRecordProcessor<OrderEvent> {
    public async ValueTask ProcessRecords(ProcessRecordsContext<OrderEvent> ctx) {
        foreach (var record in ctx.Records) {
            var order = record.GetData<OrderPlaced>();
            await ctx.Collector.EmitAsync(new OrderEvent(order.Id, "processed"));
        }
    }
}

// Startup - configure sinks
var worker = KurrentProcessors
    .Create<OrderEvent>(new OrderProcessor())
    .ConnectTo("esdb://localhost:2113")
    .WithProcessorId("order-processor")
    .AddSink(new KafkaSink<OrderEvent>(options))
    .Build();

await worker.RunAsync();
```

### 9.2 Multiple Sinks (Fan-Out)

```csharp
var worker = KurrentProcessors
    .Create<OrderEvent>(new OrderProcessor())
    .ConnectTo("esdb://localhost:2113")
    .WithProcessorId("order-processor")
    .AddSink(new KafkaSink<OrderEvent>(auditOptions))              // Audit log (ALO)
    .AddSink(new KafkaTransactionalSink<OrderEvent>(businessOpts)) // Business events (EO)
    .Build();
```

---

## 10. Testing Strategy

### 10.1 Unit Tests

- `SinkCoordinator` routes to all sinks
- `SinkCoordinator` calls correct methods on checkpoint
- `ISink` implementations flush correctly
- `ITransactionalSink` implementations handle 2PC correctly

### 10.2 Integration Tests

- End-to-end with real Kafka (testcontainers)
- Checkpoint + recovery with uncommitted transactions
- Multiple workers with transactional sinks (unique `transactional.id`)

### 10.3 Failure Scenarios

| Scenario                           | Expected Behavior                                     |
|------------------------------------|-------------------------------------------------------|
| Crash before checkpoint            | Replay from last checkpoint, duplicates for ALO sinks |
| Crash after prepare, before commit | Recovery commits on startup                           |
| Crash after commit, before ack     | Recovery commits again (idempotent)                   |
| Sink write failure                 | Propagate error, abort checkpoint                     |
| Sink flush timeout                 | Configurable: fail or warn                            |

---

## 11. Migration & Compatibility

### 11.1 Breaking Changes

- `IRecordProcessor` split into `IRecordProcessor` (no output) and `IRecordProcessor<TOutput>` (with output)
- `ProcessRecordsContext` now has generic variant with `Collector`

### 11.2 Migration Path

Existing processors without sinks continue to work with `IRecordProcessor` (non-generic).

To add sink output:
1. Change `IRecordProcessor` to `IRecordProcessor<TOutput>`
2. Update `ProcessRecords` signature to accept `ProcessRecordsContext<TOutput>`
3. Add `.AddSink(...)` calls to builder

---

## 12. Open Questions

1. **Sink failure policy** - Should sink write failures abort the entire batch, or allow partial success?

2. **Backpressure** - Should the collector apply backpressure if sinks are slow? Or buffer unboundedly?

3. **Sink metrics** - Should we expose sink-level metrics (records written, flush latency, etc.)?

4. **Conditional routing** - Should we support routing to different sinks based on record content? (e.g., `AddSink(sink, predicate)`)

5. **Sink ordering** - Does sink execution order matter? Currently parallel, should we support sequential?

---

## 13. Future Considerations

1. **Operator DAGs** - If needed later, collectors provide the foundation for operator chaining

2. **State + Sinks** - When `IStateStore` is added, both state and sinks participate in checkpoint

3. **Sink-side batching** - Sinks could batch internally for efficiency (e.g., Kafka batch producer)

4. **Dead letter sink** - Failed records could be routed to a DLQ sink

---

## 14. References

- [Apache Flink Sink Architecture (FLIP-143)](https://cwiki.apache.org/confluence/display/FLINK/FLIP-143)
- [Kafka Transactions](https://kafka.apache.org/documentation/#semantics)
- [Kurrent Processors Architecture](./architecture.md)
- [Kurrent Processors Feature Spec](./feature-spec.md)

---

## Appendix A: Comparison with Flink

| Aspect        | Flink                                                  | Kurrent Processors                      |
|---------------|--------------------------------------------------------|-----------------------------------------|
| Sink creation | `Sink` factory creates `SinkWriter` per subtask        | Single sink instance per worker         |
| Committables  | `Committer` runs separately, possibly at parallelism=1 | Commit in same worker, after checkpoint |
| Topology      | `SupportsPreCommitTopology` for aggregation            | No topology, direct sink                |
| Complexity    | High (operator DAGs, barrier alignment)                | Low (single source log, no DAGs)        |

We borrowed the two-phase commit model but dropped the complexity that comes from Flink's distributed operator DAGs.
