# Architecture

> One package ships: `Kurrent.Surge`. It holds the gRPC client and the processor runtime, and it
> bundles `Kurrent.Surge.Core`, the project that holds the contracts, the schema vocabulary, and the
> codecs. Everything else in `src/` is build-time infrastructure that gets bundled, packed as an
> analyzer, or not shipped at all.
>
> This page covers what the pieces are and why they are split that way. For where a file belongs inside
> a module, read [Module Organization](module-organization.md).

## Architectural style

**Contracts and implementation split across two projects.** `Kurrent.Surge.Core` is transport-free. It
holds interfaces, options, value types, wire contracts, schema vocabulary, and codecs. `Kurrent.Surge`
holds the gRPC client under `Client/` and the processor runtime under `Processors/`, and it bundles the
Core DLL. Consumers install `Kurrent.Surge`, the only package.

**Dual-protocol gRPC.** V1 legacy handles reads, subscriptions, and admin. V2 handles appends,
registry, indexes, and connectors. Both compile into the `Kurrent.Surge` assembly. The V1 and V2 labels
name the protocol version, not the call path. Every module calls through the write-side
`FaultGateCallInvoker`. Only the Streams read client uses the read-side invoker.

**Source-generated plumbing.** The build generates discriminated unions, unique-identifier value types,
message catalogs, Avro codecs, and options validation.

**AOT is the primary constraint.** Eight projects set `IsAotCompatible`, `Kurrent.Surge` and
`Kurrent.Surge.Core` among them. Source generators replace most uses of reflection. Some paths keep
reflection. `JsonSchemaCodecOptions` adds `DefaultJsonTypeInfoResolver` as a fallback when
`JsonSerializer.IsReflectionEnabledByDefault` is true. `MessageCatalogDiscovery.Discover` suppresses
the IL2026 trim warning. `KurrentSurgeConfigurationReader` reads configuration key by key through
`ConfigReader` and does not call `Bind`.

## Projects

`src/` holds 13 projects. One of them ships.

### Published

| Package | Contents | Depends on |
|---|---|---|
| `Kurrent.Surge` | gRPC client in `Client/` and processor runtime in `Processors/` | bundles `Kurrent.Surge.Core` with `Bundle="true"` |

The bundle closure in `Directory.Build.targets` carries Core into the package: the Core assembly, the
libraries Core bundles, Core's packed generators under `analyzers/dotnet/cs/`, and Core's `build/*.props`.

### Not published

| Project | Role | TFM |
|---|---|---|
| `Kurrent.Surge.Core` | Contracts, schema vocabulary, JSON, Protobuf, and Avro codecs, processor contracts. Bundled into `Kurrent.Surge`. | net10.0 |
| `Variant` | The `IVariant` discriminated-union runtime. Its assembly name is `Kurrent.Variant`. Bundled into Core. | net10.0 |
| `Variant.SourceGenerators` | Generates the union plumbing | netstandard2.0 |
| `Kurrent.Surge.Generators` | Generates `[UniqueIdentifier]` value types, Avro codecs, and `[WithValidation]` options validation. A packed analyzer of Core, so it ships in `Kurrent.Surge`. | netstandard2.0 |
| `Kurrent.Surge.Schema.Catalogs.Generators` | The message-catalog generator. A packed analyzer of Core, so it ships in `Kurrent.Surge`. | netstandard2.0 |
| `Kurrent.CodeAnalysis` | A shared Roslyn pipeline, included by source into each generator, never as a DLL | netstandard2.0 |
| `Kurrent.Surge.RecordFilters` | The filter engine behind `RecordFilter` and `ExpressionFilter`. Bundled into Core. | net10.0 |
| `Kurrent.Surge.Schema.Validation` | JSON-schema validation | net10.0 |
| `Kurrent.Surge.Arrow` | An Arrow Flight SQL query helper over the client | net10.0 |
| `Kurrent.Surge.Cli` | The `surge` command-line tool | net10.0 |
| `Kurrent.Surge.AI.HuggingFace`, `Kurrent.Surge.AI.Mcp` | AI integrations | net10.0 |

Every project not marked netstandard2.0 inherits net10.0 from the root `Directory.Build.props`. The
netstandard2.0 projects are Roslyn components. That TFM is Roslyn's requirement, not a support target.

For how a reference becomes package content, read [Packaging](packaging.md).

## Namespaces

Each root has one role.

| Root | Covers |
|---|---|
| `Kurrent.Surge.*` | Everything public: `Client`, `Processors`, `Codecs`, `Schema`, `AI`, `RecordFilters`, `Experimental`. |
| `Kurrent.Toolkit.*` | Internal infrastructure: `Grpc`, `Diagnostics`, `Http`, `GoogleRpc`. It is not public API. Most files live under the `Internal/` folder of `Kurrent.Surge` and `Kurrent.Surge.Core`. `TelemetryConstants` and `TelemetrySource` live in `Core/Diagnostics/`. |
| `Kurrent.Rpc` | Generated from `proto/kurrent/rpc/`. Public. |
| `Kurrent.CodeAnalysis.*` | Generator-side Roslyn helpers. Build-time only. |
| `KurrentDB.Protocol.*`, `KurrentDB.Connectors.*` | Generated from `proto/kurrentdb/protocol/`. Internal. One hand-written partial, `Client/Modules/Streams/StreamIdentifier.cs`, extends `KurrentDB.Protocol.V1`. |

`Kurrent.Variant` is both the assembly name and the namespace of the `Variant` project, set with
`<AssemblyName>` even though the project directory is `Variant`.

`RootNamespace` is `Kurrent.Surge` on both `Kurrent.Surge` and `Kurrent.Surge.Core`. The generated message-catalog namespace
follows the consuming **assembly** name, `{Assembly}.MessageCatalogs`, not `RootNamespace`.

## The client

`KurrentClient` in `Client/KurrentClient.cs` implements `IKurrentClient`. It has two factories:
`Create(KurrentClientOptions)` and `Create(string connectionString, Action<KurrentClientOptions>?)`.
The options and the DI surface live under `Configuration/`, not under `Client/`.

The constructor wires `ClusterChannels`, two `FaultGateCallInvoker` instances for write and read, the
codec provider, `SchemaGovernance`, both record pipelines, and the five modules. The order matters,
because a module captures the invoker at construction.

**Initialization is passive.** The constructor starts a watcher off-thread that waits for the first
connection before it runs `ClientInitializer`. A client that never makes a call never initializes.

Five modules, 45 operations:

| Module | Impl | Operations | Protocol |
|---|---|---:|---|
| Streams | `StreamsClient` | 14 | V2 for append and the metadata writes, V1 for read, subscribe, and the other manage operations |
| Registry | `RegistryClient` | 8 | V2 |
| Connectors | `ConnectorsClient` | 10 | V2 |
| Admin | `AdminClient` | 7 | V1 |
| Indexes | `IndexesClient` | 6 | V2 |

Contract types for every module live in `Kurrent.Surge.Core/Client/Modules/{Module}/`. The
implementation holds only the concrete client and its RPC glue. `ClientModuleBase` supplies `Time`,
`LoggerFactory`, `Logger`, `MetadataDecoder`, and `GetBackdoorClient()`.

Streams is the largest module. It is split across many partial `StreamsClient` files and holds three
service clients: a V2 `ServiceClient`, a `LegacyServiceClient`, and a `LegacyReadServiceClient` on its
own call invoker.

`Client/Legacy/` holds only `LegacyExceptionExtensions.cs` and `Uuid.cs`.

For the full operation catalogue and the connection-string reference, read
[Kurrent.Surge client](client.md).

## The processor runtime

The public way to host a processor is `AddKurrentSurge(s => s.AddProcessor<T>(...))`, or
`AddBatchProcessor<T>` for an `IRecordBatchProcessor`. `KurrentSurgeBuilder` registers the processor as
a keyed singleton, with a hosted `DroneService`, an `IDrone`, and a health check under the same id. The
`DroneService` factory calls the internal `KurrentProcessor.Build()` in `Processors/KurrentProcessor.cs`.
`Build()` creates a `KurrentClient`, a bounded channel, a `KdbCheckpointStore` with a `Checkpointer`, a
`KdbDataSource`, and a `Drone`, and returns the `Drone` as an `IDrone`.

```text
KdbDataSource.Connect   (IStreamsClient.Subscribe → partition-assign → prune caught-up → retry)
   → Channel<DataSourceMessage>          (Connected | Record | Checkpoint, an IVariant)
      → Drone.Run                        (batch, then dispatch to IRecordProcessor or IRecordBatchProcessor)
         → Checkpointer                  (auto-track, manual, shutdown, and rebalance triggers)
            → ICheckpointStore           (append to $processors/{processorId}/checkpoints)
```

The split follows the project boundary. Contracts and the runtime loop live in Core, and the
KurrentDB-specific pieces live in `Kurrent.Surge`.

| Where | What |
|---|---|
| `Kurrent.Surge.Core/Processors/` | `IProcessor`, `IRecordProcessor`, `IRecordBatchProcessor`, `IDrone` and `Drone`, `IDataSource`, `HandlerRegistry`, `DroneService`, plus `Checkpointing/`, `Partitioning/`, `Resilience/`, `Dispatching/`, `Observers/`, `Diagnostics/`, `Configuration/`, and `Models/` |
| `Kurrent.Surge/Processors/` | the internal `KurrentProcessor`, `KdbDataSource`, `KdbCheckpointStore`, `ProcessorResilienceStrategy`, the connection extensions, and `Hosting/` with the health check and the failure types |

`ProcessorOptions` carries `Subscription`, `Checkpoint`, `Batching`, `Resilience`, and the client
options. The partitioning contracts live in `Core/Processors/Partitioning/`: `IDistributionStrategy`,
`PartitionAssigner`, `PartitionCalculator`, `XXHash`, and the Range and Sticky strategies.

`KdbCheckpointStore` event-sources checkpoints to `$processors/{id}/checkpoints`. It writes one
`PartitionsCheckpointed` per cycle, uses a `CheckpointReset` barrier, and appends with
`ExpectedStreamState.Any`.

## Schema

A record does not pass through a serializer object. It passes through a **pipeline** that the client
builds once, from three collaborators:

```text
KurrentClient ctor
  ├── MessageResolver          maps CLR type to schema name, frozen from options.Messages
  ├── SchemaCodecProvider      Json, Protobuf, and Avro codecs, plus any from options.Messages.Codecs
  └── SchemaGovernance         registry round-trips: register, and check compatibility
          │
          ├── WriteRecordPipeline.Create(resolver, schemaOptions, codecs, governance)
          └── ReadRecordPipeline.Create (resolver, schemaOptions, codecs, governance)
```

Both pipelines live in `Kurrent.Surge/Client/Modules/Streams/Pipeline/` as a composed chain of steps,
not as a stateful mapper.

| Layer | Where | Key types |
|---|---|---|
| Vocabulary | `Core/Schema/` with its `Catalogs/` and `Naming/` | `SchemaName`, `MessageCatalog`, `MessageResolver`, `MessageTypeDescriptor`, the naming strategies, `SchemaUrn` |
| Codecs | `Core/Schema/Codecs/` | `ISchemaCodec`, `SchemaCodecProvider`, and the `Avro/`, `Json/`, and `Protobuf/` implementations. One codec per `SchemaDataFormat`. Registering two throws `DuplicateSchemaCodecException`. |
| Export | `Core/Schema/ISchemaExporter.cs` | `ISchemaExporter`, `SystemJsonSchemaExporter` |
| Governance | `Kurrent.Surge/Client/Schema/Governance/`, with `SystemMessageCatalog` one level up in `Client/Schema/` | `ISchemaGovernance`, `SchemaGovernance`, `CatalogSchemaMap`, `SystemMessageCatalog` |

There is no `ISchemaSerializer` and no `SchemaManager`. Codecs are the serialization abstraction.

Avro lives in `Kurrent.Surge.Core` and ships inside `Kurrent.Surge`. It is not a separate package and it
needs no opt-in call.

## Filters

`RecordFilter` is an abstract record in `Core/Client/Modules/Streams/Filters/`. Concrete filters derive
from it, and its static members are the factory: `None`, `ExcludeSystemRecords`, `ByStream`,
`ByStreamPattern`, `ByCategory`, `BySchema`, `ByUserIndex`, and `ByExpression`. It implements
`IParsable<RecordFilter>`, so `Parse` and `TryParse` are static members too.

`ByExpression` parses a SQL-like DSL, extracts a server-side filter for pushdown, and evaluates the
full AST on the client. The engine is `Kurrent.Surge.RecordFilters`, bundled into Core.

**Hazard:** a pure-equality expression such as `stream = 'X'` routes to a direct stream read, not a
`$all` scan. To filter the log, use `LIKE`, `IN`, or a compound expression.

## Protocols

| Layer | Path | Status |
|---|---|---|
| V0 draft | `proto/kurrentdb/protocol/v0/` | Excluded from the build |
| V1 legacy | `proto/kurrentdb/protocol/v1/` | Active. Reads, subscriptions, admin, gossip. |
| V2 | `proto/kurrentdb/protocol/v2/` | Active. Appends, registry, indexes, connectors. |
| Shared RPC | `proto/kurrent/rpc/` | Public, compiled without gRPC services |
| Streaming | `proto/kurrent/streaming/` | Checkpoint messages, `csharp_namespace = Kurrent.Surge.Processors.Checkpointing` |
| Google imports | `proto/google/` | Import targets only, never compiled |

`Kurrent.Surge.csproj` compiles `kurrentdb/protocol/{v1,v2}` with `Access="Internal"` and
`GrpcServices="Client"`, except `v1/code.proto` and `v1/status.proto`. It compiles `kurrent/rpc` with
`Access="Public"`. It lists `google` with `ProtoCompile="false"`. `Kurrent.Surge.Core.csproj` compiles
the checkpoint protos with `Access="Public"` and message types only.

The V2 **read** path is commented out because those RPCs do not exist in the proto yet. Do not
uncomment it until they do.

## Source generators

| Generator | Project | Triggers on | Emits |
|---|---|---|---|
| `VariantGenerator` | `Variant.SourceGenerators` | a type implementing `IVariant<...>` | the case enum, `Match` and switch members, implicit operators |
| `UniqueIdentifierGenerator` | `Kurrent.Surge.Generators` | `[Kurrent.Surge.UniqueIdentifier]` on a partial type | value-type members, `Guid` conversion methods, a `[JsonConverter]` attribute |
| `AvroCodecGenerator` | `Kurrent.Surge.Generators` | `.avsc` files in `AdditionalFiles` | Avro types, codecs, equality, schema text |
| `WithValidationGenerator` | `Kurrent.Surge.Generators` | `[Kurrent.Surge.Validation.WithValidation]` | options validation |
| `MessageCatalogGenerator` | `Kurrent.Surge.Schema.Catalogs.Generators` | `[assembly: MessageCatalog]` with `[MessageType]` and `[CustomMessageCatalog]` | `{Assembly}.MessageCatalogs`, the provider attribute, `Use{X}Catalog()` extensions |

`VariantGenerator`, `UniqueIdentifierGenerator`, and `MessageCatalogGenerator` build on
`Kurrent.CodeAnalysis`, which is included **by source** through `Kurrent.CodeAnalysis.Sources.props` so
that each generator is a single self-contained DLL. `WithValidationGenerator` and `AvroCodecGenerator`
use raw Roslyn APIs.

Both packed generators reach NuGet consumers through `analyzers/dotnet/cs/`. In-repo projects that
reference the SDK by `ProjectReference` need separate wiring, because Roslyn analyzers are not
transitive through a project reference. `Directory.Build.targets` handles that. See
[Packaging](packaging.md).

## Test infrastructure

| Project | Role |
|---|---|
| `Aspire.Hosting.KurrentDB` | Aspire integration: container lifecycle, health checks, TLS cert generation |
| `Kurrent.Surge.Testkit` | SDK fixtures: `KurrentDBFixture`, `*InsecureFixture`, `*ScavengeFixture`, `*ClusterFixture`. Namespace `Kurrent.Surge.Testkit`. |
| `Kurrent.Testkit` | Reusable datasets (ArcadeRacer, HomeAutomation), TUnit extensions, a Bogus wrapper |

## Gotchas

- **`Kurrent.Toolkit.*` is internal.** It is not public API, even though it is not named
  `Kurrent.Surge`. Most of it sits under `Internal/` in `Kurrent.Surge` and `Kurrent.Surge.Core`. Two
  files, `TelemetryConstants.cs` and `TelemetrySource.cs`, sit in `Core/Diagnostics/`.
- **`Variant` builds `Kurrent.Variant.dll`** through `<AssemblyName>`, despite the project directory
  name.
- **`Core/Processors/SubscriptionSource.Messages.cs` defines no `SubscriptionSource`.** It holds the
  drone and server control-message types, among them `WorkOrder`, `RecordBatch`,
  `ProcessorControlMessage`, and `DroneMessage`, in `Kurrent.Surge.Processors.Gateway.Messages`. The
  `Gateway` namespace is live: `IDataSource` and `KdbDataSource` are in `Kurrent.Surge.Processors.Gateway`.
  No `SubscriptionSource`, `Gateway`, or `Hive` class exists.
- **The V2 read RPCs are commented out by design.**
- **Never cite anything under `obj/`.** It carries stale generated `.g.cs` files whose namespaces do not
  exist in source.
- **`Experimental/` is not the supported pipeline.** `Components/{Appender,Reader,Consumer}` compile
  but are not wired into the processor runtime. Treat them as spikes.
- **Projectors are not part of the SDK.** There is no `KurrentProjector` and no
  `Kurrent.Projectors.DuckDB`. That work is parked on a branch. Do not document it as shipping.
