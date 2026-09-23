# What Kurrent Surge is

> The scope baseline. Read this first to learn what the SDK offers and where its edges are.
> [Architecture](architecture.md) covers the internal structure. This page covers the surface a
> consumer sees.

Kurrent Surge is the .NET SDK for the Kurrent Platform. Almost everything it reaches is KurrentDB.

It contains the Kurrent client, the successor to the KurrentDB client. On top of that it adds the thing
the client alone does not give you: **processors**.

## The shipping surface

| Package | What it is |
|---|---|
| `Kurrent.Surge` | The client and the processors. This is what a consumer installs. |

`Kurrent.Surge.Core` is a project, not a package. It holds the contracts, value types, message
catalogs, and the JSON, Protobuf, and Avro codecs, with no gRPC and no transport. `Kurrent.Surge`
bundles its DLL.

## The two ways to use it

### 1. The client: call operations

`KurrentClient` exposes five modules and 45 operations.

| Module | Operations | Covers |
|---|---:|---|
| Streams | 14 | append, read, subscribe, manage |
| Connectors | 10 | create, configure, start, stop |
| Registry | 8 | schemas and versions |
| Admin | 7 | scavenge, node priority, shutdown |
| Indexes | 6 | secondary indexes |

Append and the stream-metadata writes run on V2 gRPC. Read, subscribe, and the other manage operations
run on the V1 legacy service. One surface, two protocols.

### 2. Processors: the headline

A processor replaces hand-written subscription code. You do not manage a subscription, a checkpoint, a
retry loop, or a partition assignment. You write the handler:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.AddKurrentSurge(surge => surge.AddProcessor<TemperatureMonitorProcessor>(options => {
    options.Subscription.Filter = RecordFilter.ByExpression("schema_name = 'SensorReading'");
}));

await builder.Build().RunAsync();

sealed class TemperatureMonitorProcessor : IRecordProcessor {
    public ValueTask ProcessRecord(ProcessRecordContext context) { … }
    public ValueTask OnCheckpoint(CheckpointContext context)     { … }
}
```

That is the whole program. The connection comes from configuration. The SDK reads the `Kurrent:Surge`
section first. If that section does not exist, it reads `ConnectionStrings:Kurrent:Surge`, then
`ConnectionStrings:KurrentDB`.

**The contracts.** `IProcessor` supplies three lifecycle hooks, and every one has a default
implementation, so you override only what you need: `OnInitialize`, `OnConnected`, and `OnCheckpoint`.
On top of it you implement one of two shapes. For one record at a time, implement
`IRecordProcessor.ProcessRecord` and register the processor with `AddProcessor<T>`. For a batch,
implement `IRecordBatchProcessor.ProcessBatch` and register the processor with `AddBatchProcessor<T>`.

**What the runtime handles**, configured through `ProcessorOptions`:

| Concern | Where |
|---|---|
| What to read | `Subscription`, which holds one `Filter` and the `InitialPosition` to use when no checkpoint exists |
| Checkpointing | `Checkpoint`, which tracks and commits the position for you |
| Batching | `Batching` |
| Failure handling | `Resilience` |
| Observability | OpenTelemetry, `MeterFactory`, `Observers`, and `TrackSourceLag` |

Partitioning and parallelism are implemented. The SDK has Range and Sticky distribution strategies.
`PartitionKeyExtractor` reads the partition key from a record through a property path.
`PartitionCalculator` hashes that key with XXHash to select a partition.

`MaxParallelism` is `internal` and there is no public partition-key selector, so a processor runs
single-threaded from a consumer's point of view. Do not document these as knobs until they become
public.

### Registering in a host

`AddKurrentSurge` is the single entry point. It opens a registration scope that carries the connection
and every component running on it, on either `IServiceCollection` or `IHostApplicationBuilder`. 18 of
the 22 projects in `examples/` use it. The other four are two libraries, `BikeShare.Rides` and
`Transfermarkt.Domain`, and two package smoke tests, `PackageTest` and `PackageTest.Streaming`.

## Schema

Declare your message types once. The SDK maps CLR types to schema names and handles the registry.

One dial, three presets:

| Preset | Registration | Validation |
|---|---|---|
| `Disabled` (the default) | manual | off |
| `AutoRegister` | automatic for every catalog schema, at client initialization | off |
| `Strict` | manual | on. Every catalog schema must be pre-registered and compatible. |

**JSON, Protobuf, and Avro are all built into `Kurrent.Surge`**, through the codecs in the bundled
`Kurrent.Surge.Core`. None of them is a separate package and none needs an opt-in call. Only JSON can
produce its schema from the CLR type, and schema registration always uses the JSON format. The Avro
schema text comes from `.avsc` files, through the source generator. `AvroSchemaTranscoder` can
transcode a stored Avro payload to JSON from schema text. The Protobuf equivalent,
`ProtobufSchemaTranscoder`, is in the `Kurrent.Surge.Codecs.Protobuf.Experimental.Schema` namespace.
The SDK itself calls neither transcoder.

## Not settled

Stated plainly, because a baseline that hides its gaps is not a baseline.

| Area | State |
|---|---|
| Partitioning and parallelism | Implemented, not exposed. `MaxParallelism` is `internal` and there is no public partition-key selector. |
| Experimental components | `Experimental/Components/{Appender,Reader,Consumer}` compile but are not wired into the processor runtime. Treat them as spikes. |

## Deliberately out of scope

The SDK is pre-release and ships previews only. These are choices, not constraints:

- **Projectors are not part of the SDK.** There is no `KurrentProjector` and no DuckDB projector
  package. Do not document them.
- net10.0 only. Source generators target netstandard2.0 because Roslyn requires it.
- The SDK targets Native AOT, and source generators replace most uses of reflection. Some paths keep
  reflection. `JsonSchemaCodecOptions` adds `DefaultJsonTypeInfoResolver` as a fallback when
  `JsonSerializer.IsReflectionEnabledByDefault` is true. `MessageCatalogDiscovery.Discover` suppresses
  the IL2026 trim warning.
