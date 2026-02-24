# Kurrent.Processors

Detailed reference for the `Kurrent.Processors` package. Read
[project.md](project.md) first for SDK-level context.

---

## Purpose

`Kurrent.Processors` is a distributed stream processing framework. It replaces
the Subscribe operation that existed in `KurrentDB.Client` (v1). Instead of
client-managed subscriptions, processors provide: distributed checkpointing,
batching, partition-based parallelism, and a structured lifecycle.

Depends on `Kurrent.Client` for all KurrentDB communication.

---

## Architecture

```
User Code (IRecordProcessor)
    ↓
ProcessorWorker (lifecycle engine)
    ├── IProcessorGateway → LegacyGateway (V1 gRPC subscription)
    ├── CheckpointStore (per-processor stream in KurrentDB)
    ├── PartitionAssigner → PartitionCalculator (XXHash/Murmur)
    └── SafeRecordProcessor (error isolation wrapper)
```

### Entry Point

```csharp
await KurrentProcessors
    .Create(new MyProcessor())
    .ConnectTo("kurrentdb://localhost:2113")
    .WithProcessorId("order-projector")
    .WithFilter(RecordFilter.ByCategory("order"))
    .ConfigureCheckpointing(c => c.WithInterval(TimeSpan.FromSeconds(10)))
    .RunWithGracefulShutdownAsync();
```

`KurrentProcessors.Create(...)` accepts `IRecordProcessor`,
`IRecordProcessorFactory`, or `Action<RecordProcessorBuilder>`.

---

## IRecordProcessor

```csharp
public interface IRecordProcessor {
    ValueTask ProcessRecords(ProcessRecordsContext context);     // Required
    ValueTask OnCheckpoint(CheckpointContext context) => ...;   // Optional
    ValueTask OnError(ErrorContext context) => ...;             // Optional
    ValueTask OnComplete(CompleteContext context) => ...;       // Optional
}
```

`ProcessRecordsContext` provides `RecordBatch Records`, `WorkerProgress Progress`,
and `StopProcessing()`. All context types extend `WorkerContext` (carries
`ProcessorId`, `WorkerId`, `SessionId`, `Logger`, `CancellationToken`).

---

## Gateway

`LegacyGateway` is the only concrete `IProcessorGateway`. It uses the V1 gRPC
streaming read RPC to create a subscription. Records are pumped through a
bounded `Channel<LegacyGatewayMessage>`. A `PeriodicTimer` injects checkpoint
trigger messages at the configured interval.

**Partitioning is computed client-side.** `PartitionCalculator.XXHash` hashes
the stream name to assign partitions. This is a workaround until the V2
protocol sends partition info with records. The V2 processor protocol
(`processors.proto`) is fully designed but entirely commented out.

---

## Checkpointing

`CheckpointStore` uses document-sourcing over a KurrentDB stream:
`$processors/{processorId}/checkpoints`.

- **Load:** reads last record, deserializes `ProcessorCheckpointState`
  (partition to position mapping)
- **Store:** read-modify-write with optimistic concurrency via
  `ConsistencyCheck.Stream(...).AtRevision(r)`. Retries on revision conflict.
- **Retention:** auto-configures `MaxCount` on the checkpoint stream on first use.
- Stream is created implicitly with `ExpectedStreamCondition.NoStream`.

---

## Configuration

| Group        | Key Settings                                                            | Defaults                  |
|--------------|-------------------------------------------------------------------------|---------------------------|
| Core         | `ProcessorId`, `WorkerId`, `MaxParallelism`                             | Auto-generated, auto, 128 |
| Subscription | `Filter`, `FilterExpression`, `PartitionKeySelector`, `InitialPosition` | None, None, None, Latest  |
| Batching     | `MaxSize`, `Timeout`                                                    | 200, 250ms                |
| Checkpoint   | `Interval`, `Timeout`, `RetentionCount`, `FailurePolicy`                | 30s, 30s, 3, Continue     |
| Worker       | `ThrowOnFailure`, `BufferCapacity`, `SkipDecoding`                      | false, 1000, false        |

`ProcessorId` auto-generates from the processor type name (converted to
`snake_case`) if not explicitly set.

Configuration can be loaded from `IConfiguration` via
`.FromConfiguration(IConfiguration)` (reads from `Kurrent:Processors:{TypeName}`).

---

## Partition Strategy

Two distribution strategies:
- `StickyStrategy` (default) — minimizes partition movement on rebalance
- `RangeStrategy` — deterministic contiguous ranges, useful for testing

Two hash algorithms: `XXHash` (default), `MurmurHash3`.

`PartitionAssigner` wraps `PartitionCalculator` with `IDistributionStrategy`.
Currently the server does NOT assign partitions; `LegacyGateway` computes them
client-side by hashing the stream name.

---

## Key Files

| Purpose              | Path                                                                             |
|----------------------|----------------------------------------------------------------------------------|
| Entry point          | `src/Kurrent.Processors/KurrentProcessors.cs`                                    |
| Builder              | `src/Kurrent.Processors/KurrentProcessorBuilder.cs`                              |
| Worker lifecycle     | `src/Kurrent.Processors/ProcessorWorker.cs`                                      |
| IRecordProcessor     | `src/Kurrent.Processors/IRecordProcessor.cs`                                     |
| Gateway (V1)         | `src/Kurrent.Processors/Gateway/LegacyGateway.cs`                                |
| Checkpoint store     | `src/Kurrent.Processors/Checkpointing/CheckpointStore.cs`                        |
| Partition calculator | `src/Kurrent.Processors/Partitioning/PartitionCalculator.cs`                     |
| Settings types       | `src/Kurrent.Processors/Options/`                                                |
| V1 mapper            | `src/Kurrent.Processors/Models/StreamsClientV1Mapper.cs`                         |
| Test fixture         | `test/Kurrent.Processors.Integration.Tests/Fixtures/KurrentClientTestFixture.cs` |
| Example projects     | `examples/processors/`                                                           |
