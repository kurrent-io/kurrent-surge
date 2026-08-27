# Architecture

> Two-layer SDK: `Kurrent.Surge.Core` holds the contracts, `Kurrent.Surge` holds the implementation
> (gRPC client **and** stream processing, fused into one package). `Kurrent.Surge.Schema.Catalogs` is a
> standalone message-vocabulary package both depend on.

## Architectural Style

- **Core + implementation split.** `Kurrent.Surge.Core` is a transport-free contract layer (interfaces,
  options, value types, wire contracts). `Kurrent.Surge` is the single implementation package — the gRPC
  client (`Client/`) and the stream-processing runtime (`Streaming/`) live side by side in it. Consumers
  install `Kurrent.Surge`; a second client (e.g. in-proc inside KurrentDB) could depend on `Kurrent.Surge.Core`
  alone.
- **Dual-protocol gRPC.** V1 legacy handles reads, subscriptions, and admin; V2 handles appends, registry,
  indexes, and connectors. Both compile from proto into the `Kurrent.Surge` assembly. The V1/V2 label is the
  protocol version, not the call path — Indexes/Connectors use V2 messages but still route through the V1
  legacy call invoker.
- **Source-generated plumbing.** Discriminated unions (`IVariant`), unique-identifier value types
  (`[UniqueIdentifier]`), message-type catalogs/resolvers, and Avro codecs are generated at compile time.
- **Primary constraint: AOT.** The four packable projects are `IsAotCompatible`. No reflection, no
  `Activator.CreateInstance`, no runtime expression compilation; serialization goes through source generators
  or explicit code. (One documented exception: `ProcessorOptions.FromConfiguration` uses the reflection config
  binder, so `EnableConfigurationBindingGenerator=false` on `Kurrent.Surge` — see that csproj.)

## Components

### Publishable packages

| Package                       | Purpose                                                              | Depends on (NuGet)                          | AOT |
|-------------------------------|----------------------------------------------------------------------|---------------------------------------------|-----|
| `Kurrent.Surge.Core`          | Contract layer: client + streaming interfaces, options, value types, wire contracts | `Kurrent.Surge.Schema.Catalogs`; bundles `Variant` | yes |
| `Kurrent.Surge`               | Implementation: gRPC client (`Client/`) + stream processing (`Streaming/`) | `Kurrent.Surge.Core`, `Kurrent.Surge.Schema.Catalogs` | yes |
| `Kurrent.Surge.Schema.Catalogs` | Message vocabulary: `SchemaName`, catalogs, `MessageResolver`, naming strategies (BCL-only, standalone) | none (bundles its own generator)          | yes |
| `Kurrent.Surge.Codecs.Avro`   | Opt-in Avro codec; packs its own generator                           | `Kurrent.Surge.Core`                        | yes |

### Solution-level libraries (not published)

| Library                             | Purpose                                                              | TFM            |
|-------------------------------------|----------------------------------------------------------------------|----------------|
| `Variant`                           | `IVariant` discriminated-union runtime + `Result<T,E>` (assembly name `Kurrent.Variant`) | net10.0 |
| `Variant.SourceGenerators`          | Generates `IVariant` union plumbing                                  | netstandard2.0 |
| `Kurrent.Surge.SourceGenerators`    | Generates `[UniqueIdentifier]` value-type plumbing                    | netstandard2.0 |
| `Kurrent.Surge.Schema.Catalogs.Generators` | The message-catalog generator (ships bundled inside the Schema.Catalogs package) | netstandard2.0 |
| `Kurrent.Surge.Codecs.Avro.Generators` | The Avro codec generator (ships bundled inside the Codecs.Avro package) | netstandard2.0 |
| `Kurrent.CodeAnalysis`              | Shared Roslyn infrastructure (type pipeline, source writer, symbol model) | netstandard2.0 |
| `Kurrent.Surge.Arrow`               | Arrow Flight SQL query helper over the client                        | net10.0        |

`Variant` is bundled into `Kurrent.Surge.Core` (`Bundle="true"`, not a separate NuGet dependency) and reaches
`Kurrent.Surge` transitively. `Kurrent.Surge.Schema.Catalogs` is referenced with `PrivateAssets="none"` so its
bundled generator flows to downstream consumers.

### Separate implementation package

| Package                   | Purpose                                                         | Depends on      |
|---------------------------|-----------------------------------------------------------------|-----------------|
| `Kurrent.Projectors.DuckDB` | DuckDB-backed projection client + checkpoint store (uses `kurrent.quack` + `DuckDB.NET`) | `Kurrent.Surge` |

### Test infrastructure

| Project                    | Purpose                                                                          |
|----------------------------|----------------------------------------------------------------------------------|
| `Aspire.Hosting.KurrentDB` | Aspire integration: container lifecycle, health checks, TLS cert generation      |
| `Kurrent.Surge.Testkit`    | SDK fixtures: `KurrentDBFixture`, `*InsecureFixture`, `*ScavengeFixture`, `*ClusterFixture` (namespace `Kurrent.Sdk.Testkit`) |
| `Kurrent.Testkit`          | Reusable datasets (ArcadeRacer, HomeAutomation), TUnit extensions, Bogus wrapper |

## Namespace map

Code namespaces follow the package family, with known orphans left on the old root by decision:

| Area                              | Namespace root                        |
|-----------------------------------|---------------------------------------|
| Client (contracts + impl)         | `Kurrent.Surge.Client.*`              |
| Streaming (contracts + impl)      | `Kurrent.Surge.Streaming.*`           |
| Schema vocabulary                 | `Kurrent.Surge.Schema.*`              |
| **Orphans (not yet renamed)**     | `Kurrent.Processors.*` (`ProcessorOptions`, locks), `Kurrent.Projectors` (projector types), `Kurrent.Appender`, bare `Kurrent`, and infra `Kurrent.Grpc/.Rpc/.Http/.GoogleRpc/.Diagnostics/.Validation` |
| Legacy transport (vendored)       | `KurrentDB.Client` (under `Client/Legacy/RipAndTear/`) |
| Proto-generated                   | `KurrentDB.Protocol.*` (V1/V2), `Kurrent.Surge.Streaming.Checkpointing` (streaming) |

`RootNamespace` is `Kurrent.Surge` on both `Kurrent.Surge` and `Kurrent.Surge.Core`. The generated
message-catalog namespace follows the consuming **assembly** name (`{Assembly}.MessageCatalogs`), not
`RootNamespace`.

## Kurrent.Surge.Core (contracts)

Transport-free. Folders: `Client/` (client interfaces, module contracts, schema serializer contracts,
configuration contracts), `Processors/` (streaming contracts), `Projectors/` (projection base types),
`Diagnostics/`, `Errors/` (`KurrentException` base), `Internal/`. Also compiles the streaming checkpoint
proto (`proto/kurrent/streaming/`, `csharp_namespace = Kurrent.Surge.Streaming.Checkpointing`) and pins the
checkpoint wire names in `MessageCatalog.cs` (`StreamingMessageCatalog`).

Key contracts: `IKurrentClient` and the five module interfaces (`IStreamsClient`, `IRegistryClient`,
`IIndexesClient`, `IConnectorsClient`, `IAdminClient`); `IKurrentClientOptions` / `IKurrentBuilder`; the
processor surface (`IDrone`, `Drone`, `IDataSource`, `ICheckpointStore`, `ProcessorOptions`); `Projection<TClient>`.

## Kurrent.Surge — Client (`src/Kurrent.Surge/Client/`)

### Entry point

`KurrentClient` (`Client/KurrentClient.cs`, namespace `Kurrent.Surge.Client`) implements `IKurrentClient`.
Factories: `Create(KurrentClientOptions)` and `Create(string connectionString, Action<KurrentClientOptions>?)`.
Construction wires the `KurrentDBLegacyCallInvoker` (transport), a frozen `MessageResolver` from
`options.Messages.BuildResolver()`, a `SchemaManager`, and an `ISchemaSerializerProvider` (Bytes + Json +
Protobuf). `KurrentClientOptions` and the DI surface (`AddKurrent`, `KurrentBuilder`) live under
`Configuration/`, not `Client/`.

### Modules

Five module clients, each wrapping the shared legacy call invoker:

| Module     | Property            | Impl              | Protocol |
|------------|---------------------|-------------------|----------|
| Streams    | `client.Streams`    | `StreamsClient`   | V1 + V2  |
| Registry   | `client.Registry`   | `RegistryClient`  | V2       |
| Indexes    | `client.Indexes`    | `IndexesClient`   | V2       |
| Connectors | `client.Connectors` | `ConnectorsClient`| V2       |
| Admin      | `client.Admin`      | `AdminClient`     | V1       |

`ClientModuleBase` (`Client/Modules/ClientModuleBase.cs`) provides shared plumbing — `Time`, `LoggerFactory`,
`Logger`, `SerializerProvider`, `ServerCapabilities`, `MetadataDecoder`, `GetBackdoorClient()`. (Streams and
Registry derive from it directly; the leaner modules construct their service client without it.) Public
contract types for each module live in `Kurrent.Surge.Core/Client/Modules/{Module}/`; the impl holds only the
concrete client + RPC glue.

**Per-module file convention** — `{Module}Client.cs` holds the ctor/fields; each RPC area is a partial
`{Module}Client.{Area}.cs`, with wire↔model mapping split into a sibling `.Mapping.cs`/`.Mappers.cs`. Streams
is the most elaborate: `.Append`, `.Read`, `.Subscribe`, `.Manage`, `.Resolve`, `.Folding`, plus `Metadata/`,
`Models/`, `RecordDecoder`, `RecordMapper`.

### Legacy layer

`Client/Legacy/` bridges the modern client onto the original EventStore-style gRPC/gossip machinery
(`KurrentDBLegacyCallInvoker`, settings/error converters). `Client/Legacy/RipAndTear/` is vendored legacy
transport (gossip discovery, channel selection, interceptors, `Uuid`/`X509` helpers) still under
`namespace KurrentDB.Client` — internal-only, slated for removal once the .NET48-era discovery code is retired.

### Schema system (three layers)

`CLR type → MessageResolver/MessageTypeMapper → SchemaManager → ISchemaSerializer → bytes`, split across the
three packages:

| Layer                                   | Where                                              | Key types                                                                 |
|-----------------------------------------|----------------------------------------------------|---------------------------------------------------------------------------|
| Vocabulary (naming + catalogs)          | `Kurrent.Surge.Schema.Catalogs`                    | `SchemaName`, `MessageCatalog`, `MessageResolver`, `MessageTypeDescriptor`, naming strategies, `SchemaUrn` |
| Serializer contracts + options/context  | `Kurrent.Surge.Core/Client/Schema[/Serialization]` | `ISchemaSerializer`, `SchemaSerializerProvider`, Json/Protobuf options + `JsonSerializerContext`s, `BytesPassthroughSerializer`, `ISchemaExporter`, `SystemJsonSchemaExporter`, `SchemaRegistrationOptions` |
| Governance + codecs                     | `Kurrent.Surge/Client/Schema`                      | `ISchemaGovernance` / `SchemaGovernance` (talks to `RegistryClient`), `CatalogSchemaMap`, `SystemMessageCatalog` (always-active `$metadata`/link/scavenge mappings). Codecs live in `Kurrent.Surge.Core/Schema/Codecs` — `ISchemaCodec`, `SchemaCodecProvider`, `Json/`, `Protobuf/` |

### Error handling

Exception types derive from `KurrentException` (the pure-data base, in
`Kurrent.Surge.Core/Errors/KurrentException.cs`) and carry an `ErrorCode.FromType<T>()`. RPC faults are
mapped through `ex.ToException(parsers...)`, where each parser is a static `From` method group on the
exception type.

### Filters

`RecordFilter` is an abstract record; concrete filters derive from it and its static members are the
factory (`None`, `ByStream`, `ByStreamPattern`, `ByCategory`, `BySchema`, `ByUserIndex`,
`ByExpression`). `ByExpression` parses a SQL-like DSL and extracts a server-side
`ServerFilter` for pushdown, evaluating the full AST client-side via `IsMatch`. (Pure-equality `stream = 'X'`
routes to a direct stream read, not a `$all` scan — use `LIKE`/`IN`/compound for log filtering.)

## Kurrent.Surge — Streaming (`src/Kurrent.Surge/Streaming/`)

### Entry point and pipeline

`KurrentProcessor` (`Streaming/Processors/KurrentProcessor.cs`, namespace `Kurrent.Surge.Streaming`) is the
public entry: `Create(IRecordProcessor | IRecordBatchProcessor | Action<MessageProcessor>, ProcessorOptions?)`,
then `RunAsync(ct)` / `RunWithGracefulShutdownAsync(timeout)`. It builds a `KurrentClient`, a bounded
`Channel<DataSourceMessage>`, a `KurrentDBCheckpointStore` + `Checkpointer`, a `KdbDataSource`, and a `Drone`,
returning the `Drone` as `IDrone`.

```text
KdbDataSource.Connect  (IStreamsClient.Subscribe → partition-assign → prune caught-up → Polly retry)
   → Channel<DataSourceMessage>   (Connected | Record | Checkpoint; IVariant)
      → Drone.Run  (batches via ReadBatched → dispatch to IRecordProcessor / IRecordBatchProcessor)
         → Checkpointer  (auto-track / manual / shutdown / rebalance triggers)
            → ICheckpointStore   (append to $processors/{processorId}/checkpoints)
```

| Type                    | Where                                              | Role                                                        |
|-------------------------|----------------------------------------------------|-------------------------------------------------------------|
| `KurrentProcessor`      | `Kurrent.Surge/Streaming/Processors`               | Public entry; builds and runs the pipeline                  |
| `IDataSource` / `DataSourceMessage` | `Kurrent.Surge.Core/Processors/IDataSource.cs` (ns `Kurrent.Surge.Streaming.Gateway`) | Data-source contract; feeds a `ChannelWriter<DataSourceMessage>` |
| `KdbDataSource`         | `Kurrent.Surge/Streaming/Processors`               | KurrentDB data source — wraps `IStreamsClient.Subscribe`, partitions, prunes, retries |
| `IDrone` / `Drone`      | `Kurrent.Surge.Core/Processors`                    | Runtime loop: batch, dispatch, checkpoint; `DroneStatus`    |
| `Checkpointer`, `ICheckpointStore`, `CheckpointStoreBase` | `Kurrent.Surge.Core/Processors/Checkpointing` | Checkpoint orchestration + store contract (`ProcessorCheckpoint`, position trackers, `HybridLogicalClock`) |
| `KurrentDBCheckpointStore` | `Kurrent.Surge/Streaming/Processors/Checkpointing` | Event-sources checkpoints to `$processors/{id}/checkpoints` (one `PartitionsCheckpointed` per cycle; `CheckpointReset` barrier; `ExpectedStreamState.Any`) |
| `DroneService<T>`       | `Kurrent.Surge.Core/Processors/DroneService.cs` (ns `Kurrent.Surge.Streaming.Hosting`) | `BackgroundService` for the DI path (`AddKurrentProcessor<T>`) |

`ProcessorOptions` (namespace `Kurrent.Processors.Configuration` — an orphan not yet renamed) carries
`Subscription`, `Checkpoint`, `Batching`, `Resilience`, and the client options. Partitioning contracts
(`IDistributionStrategy`, `PartitionAssigner`, `PartitionCalculator`, `XXHash`, Range/Sticky strategies) live
in `Kurrent.Surge.Core/Processors/Partitioning/`.

### Projectors

A projector is a `KurrentProcessor` running a `Projection<TClient>` as its `IRecordProcessor`.
`KurrentProjector` (impl, ns `Kurrent.Projectors`) wraps a `KurrentProcessor`; `Projection<TClient>` (Core,
`: IRecordProcessor`) provides fluent per-type dispatch (`Project<T>`, `Fallback`, `Setup`) with a lazily
resolved client. `Kurrent.Projectors.DuckDB` supplies the DuckDB `TClient` closures (`DuckDBProjection`,
`DuckDBProjectorOptions`) and an alternate `DuckDBCheckpointStore` (one row per `(processor_id, partition)`,
optimistic-concurrency fenced on `log_position`).

### Experimental (`Streaming/Experimental/`) — not the supported pipeline

Staged/incomplete work that compiles but is not wired into `KurrentProcessor`: `Components/Appender/`
(buffered producer), `DroneCommander/` (parent/child drone control-plane), `Leases/` (`ILeaseManager`
interface only — `LeaseManager` is commented out), `Locks/` (`ServiceLocker`, compiles against the lease
interface), `Stuff/` (agnostic/pragmatic pipelines, `ISink`, a commented-out `IProjection`). Treat as spikes.

## Protocols

| Layer              | Path                           | Status                                         |
|--------------------|--------------------------------|------------------------------------------------|
| V0 (draft)         | `proto/kurrentdb/protocol/v0/` | Excluded from build                            |
| V1 (legacy gRPC)   | `proto/kurrentdb/protocol/v1/` | Active: reads, subscriptions, admin, gossip    |
| V2 (new gRPC)      | `proto/kurrentdb/protocol/v2/` | Active: appends, registry, indexes, connectors |
| Shared RPC types   | `proto/kurrent/rpc/`           | Public, compiled without gRPC services         |
| Streaming contracts| `proto/kurrent/streaming/`     | Checkpoint messages (`csharp_namespace = Kurrent.Surge.Streaming.Checkpointing`) |
| Google imports     | `proto/google/`                | Import targets only, not compiled              |

`Kurrent.Surge.csproj` compiles the `kurrentdb/protocol/{v1,v2}` + `kurrent/rpc` + `google` protos
(`Access="Internal"`, `GrpcServices="Client"`, V0 excluded). `Kurrent.Surge.Core.csproj` compiles the
`kurrent/streaming` checkpoint protos (`Access="Public"`, message types only). The V2 **read** path is
commented out — those RPCs do not exist in the proto yet.

## Source generators

| Generator                        | Project                              | Trigger                                             | Generates                                                     |
|----------------------------------|--------------------------------------|-----------------------------------------------------|---------------------------------------------------------------|
| `VariantGenerator`               | `Variant.SourceGenerators`           | type implementing `Kurrent.Variant.IVariant<...>`   | union case enum, `Match`/switch, implicit ops                 |
| `UniqueIdentifierGenerator`      | `Kurrent.Surge.SourceGenerators`     | `[Kurrent.Surge.UniqueIdentifierAttribute]` on a partial type | value-type members, explicit `Guid` operators, JSON converter |
| `AvroCodecGenerator`             | `Kurrent.Surge.Codecs.Avro.Generators` | Avro schema files and annotated types             | Avro types, codecs, equality, schema text                     |
| `MessageCatalogGenerator`        | `Kurrent.Surge.Schema.Catalogs.Generators` | `[assembly: MessageCatalog(...)]` + `[MessageType]` + `[CustomMessageCatalog]` | `{Assembly}.MessageCatalogs`, provider attribute, `Use{X}Catalog()` extensions |

`VariantGenerator` and `UniqueIdentifierGenerator` build on `Kurrent.CodeAnalysis` (shared Roslyn pipeline
+ source writer); `MessageCatalogGenerator` uses raw Roslyn APIs. The catalog generator is wired onto
`Kurrent.Surge` and `Kurrent.Surge.Core` compilations via `Directory.Build.targets` (`_SdkSourceRef`,
keyed on the `Kurrent.Surge.Schema.Catalogs` project filename) and ships bundled in the Schema.Catalogs
package so it also runs in consumer projects.

## Gotchas

- **Namespace inconsistency (intentional, for now).** Processor/projector/appender/infra namespaces
  (`Kurrent.Processors.*`, `Kurrent.Projectors`, `Kurrent.Appender`, bare `Kurrent`, `Kurrent.Grpc/.Rpc/...`)
  were **not** renamed to `Kurrent.Surge.*` in the namespace sweep — left frozen by decision. Expect
  `Kurrent.Surge.Streaming` types sitting next to `Kurrent.Processors.Configuration` options.
- **Preserved dead code.** `#if false` / fully-commented blocks kept for reference during the projector
  refactor: `Kurrent.Surge.Core/Projectors/Projector.cs` and `RecordProjector.cs`,
  `Kurrent.Projectors.DuckDB/DuckDBCheckpointStoreOld.cs`, `Experimental/Leases/LeaseManager.cs`,
  `Experimental/Stuff/IProjection.cs`. Do not treat as current API.
- **Misleading filename.** `Kurrent.Surge.Core/Processors/SubscriptionSource.Messages.cs` no longer defines a
  `SubscriptionSource` — it holds the drone/server control-message variants (`WorkOrder`, `RecordBatch`,
  `ProcessorControlMessage`, `DroneMessage`, …). The old `SubscriptionSource`/`Gateway`/`Hive` classes are gone.
- **Legacy transport.** `Client/Legacy/RipAndTear/` is vendored `KurrentDB.Client` code; never treat it as
  public API.
- **`Variant` assembly is `Kurrent.Variant`** (via `<AssemblyName>`), despite the project directory name.
- **`src/Kurrent.Surge.Streaming/` and `src/untitled folder/` are on-disk cruft** — not projects, not in the
  solution (only stale `obj/` residue). Candidates for deletion; do not document as packages.
- V2 read RPCs are commented out by design — do not uncomment until the proto defines them.
- Never cite anything under `obj/`: it contains pre-rename generated `.g.cs` (old `Kurrent.Client.*` /
  `Kurrent.Streaming.*` namespaces) that no longer exist in source.
