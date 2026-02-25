# Kurrent.Processors

Detailed reference for the `Kurrent.Processors` package. Read
[project.md](project.md) first for SDK-level context.

## Contents

- [Purpose](#purpose)
- [Architecture](#architecture)
- [IRecordProcessor](#irecordprocessor)
- [Gateway](#gateway)
- [Checkpointing](#checkpointing)
- [Configuration](#configuration)
- [Partition Strategy](#partition-strategy)
- [Key Files](#key-files)

---

## Purpose

Distributed stream processing framework replacing the Subscribe operation
from `KurrentDB.Client` (v1). Provides: distributed checkpointing, batching,
partition-based parallelism, and a structured lifecycle.

---

## Architecture

```
User Code (IRecordProcessor)
    |
ProcessorWorker (lifecycle engine)
    +-- IProcessorGateway -> LegacyGateway (V1 gRPC subscription)
    +-- CheckpointStore (per-processor stream in KurrentDB)
    +-- PartitionAssigner -> PartitionCalculator (XXHash/Murmur)
    +-- SafeRecordProcessor (error isolation wrapper)
```

Entry point: `KurrentProcessors.Create(...)` at
`src/Kurrent.Processors/KurrentProcessors.cs:73`. Accepts `IRecordProcessor`,
`IRecordProcessorFactory`, or `Action<RecordProcessorBuilder>`. See
`examples/processors/Processor.WordCount/Program.cs:16` for a working example.

---

## IRecordProcessor

`ProcessRecords(ProcessRecordsContext)` is the only required method. Optional:
`OnCheckpoint`, `OnError`, `OnComplete`. All context types extend `WorkerContext`
(carries `ProcessorId`, `WorkerId`, `SessionId`, `Logger`, `CancellationToken`).

---

## Gateway

`LegacyGateway` uses the V1 gRPC streaming read RPC. Records pump through a
bounded `Channel<LegacyGatewayMessage>`. A `PeriodicTimer` injects checkpoint
triggers. Partitioning is computed client-side by hashing the stream name
(workaround until V2 protocol sends partition info). The V2 processor protocol
(`processors.proto`) is fully designed but entirely commented out.

---

## Checkpointing

`CheckpointStore` uses document-sourcing over a KurrentDB stream:
`$processors/{processorId}/checkpoints`. Load reads last record; store uses
optimistic concurrency via `ConsistencyCheck.Stream(...).AtRevision(r)`.
Auto-configures `MaxCount` retention on first use.

## Configuration

| Group        | Key Settings                                                            | Defaults                  |
|--------------|-------------------------------------------------------------------------|---------------------------|
| Core         | `ProcessorId`, `WorkerId`, `MaxParallelism`                             | Auto-generated, auto, 128 |
| Subscription | `Filter`, `FilterExpression`, `PartitionKeySelector`, `InitialPosition` | None, None, None, Latest  |
| Batching     | `MaxSize`, `Timeout`                                                    | 200, 250ms                |
| Checkpoint   | `Interval`, `Timeout`, `RetentionCount`, `FailurePolicy`                | 30s, 30s, 3, Continue     |
| Worker       | `ThrowOnFailure`, `BufferCapacity`, `SkipDecoding`                      | false, 1000, false        |

`ProcessorId` auto-generates from processor type name (converted to
`snake_case`) if not explicitly set.

## Partition Strategy

`StickyStrategy` (default) minimizes partition movement on rebalance.
`RangeStrategy` gives deterministic contiguous ranges for testing.
Hash algorithms: `XXHash` (default), `MurmurHash3`.
