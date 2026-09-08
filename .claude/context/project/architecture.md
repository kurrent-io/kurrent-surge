# Architecture

> Two packages ship. `Kurrent.Surge.Core` holds the contracts, the schema vocabulary, and the codecs.
> `Kurrent.Surge` holds the gRPC client and the processor runtime. Everything else in `src/` is
> build-time infrastructure that gets bundled, packed as an analyzer, or not shipped at all.
>
> This page covers what the pieces are and why they are split that way. For where a file belongs inside
> a module, read [Module Organization](module-organization.md).

## Architectural style

**Contracts and implementation split across two packages.** `Kurrent.Surge.Core` is transport-free. It
holds interfaces, options, value types, wire contracts, schema vocabulary, and codecs. `Kurrent.Surge`
is the single implementation package: the gRPC client under `Client/` and the processor runtime under
`Processors/`. Consumers install `Kurrent.Surge`. A second implementation could depend on Core alone.

**Dual-protocol gRPC.** V1 legacy handles reads, subscriptions, and admin. V2 handles appends,
registry, indexes, and connectors. Both compile into the `Kurrent.Surge` assembly. The V1 and V2 labels
name the protocol version, not the call path. Indexes and Connectors use V2 messages but route through
the legacy call invoker.

**Source-generated plumbing.** The build generates discriminated unions, unique-identifier value types,
message catalogs, Avro codecs, and options validation.

**AOT is the primary constraint.** Eight projects set `IsAotCompatible`, including both packable ones.
That rules out reflection, `Activator.CreateInstance`, and runtime expression compilation. One
exception is documented: `ProcessorOptions.FromConfiguration` uses the reflection config binder, so
`Kurrent.Surge` sets `EnableConfigurationBindingGenerator=false`. See that csproj.

## Projects

`src/` holds 13 projects. Two of them ship.

### Published

| Package | Contents | Depends on |
|---|---|---|
| `Kurrent.Surge.Core` | Contracts, schema vocabulary, JSON, Protobuf, and Avro codecs, processor contracts | bundles `Variant` and `Kurrent.Surge.RecordFilters`, packs `Kurrent.Surge.Generators` and `Kurrent.Surge.Schema.Catalogs.Generators` |
| `Kurrent.Surge` | gRPC client in `Client/` and processor runtime in `Processors/` | `Kurrent.Surge.Core` with `PrivateAssets="none"` |

That `PrivateAssets="none"` is load-bearing. NuGet excludes `Build,Analyzers` on dependency edges by
default, so without it a consumer who installs only `Kurrent.Surge` would not receive Core's packed
generators.

### Not published

| Project | Role | TFM |
|---|---|---|
| `Variant` | The `IVariant` discriminated-union runtime. Its assembly name is `Kurrent.Variant`. Bundled into Core. | net10.0 |
| `Variant.SourceGenerators` | Generates the union plumbing | netstandard2.0 |
| `Kurrent.Surge.Generators` | Generates `[UniqueIdentifier]` value types, Avro codecs, and `[WithValidation]` options validation. Packed into Core. | netstandard2.0 |
| `Kurrent.Surge.Schema.Catalogs.Generators` | The message-catalog generator. Packed into Core. | netstandard2.0 |
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

Four roots, all deliberate.

| Root | Covers |
|---|---|
| `Kurrent.Surge.*` | Everything public: `Client`, `Processors`, `Codecs`, `Schema`, `AI`, `RecordFilters`, `Experimental`. |
| `Kurrent.Toolkit.*` | Internal infrastructure: `Grpc`, `Diagnostics`, `Http`, `GoogleRpc`. It lives under each package's `Internal/` folder and is not public API. |
| `Kurrent.CodeAnalysis.*` | Generator-side Roslyn helpers. Build-time only. |
| `KurrentDB.Protocol.V*` | Generated from the protos. |

`Kurrent.Variant` is both the assembly name and the namespace of the `Variant` project, set with
`<AssemblyName>` even though the project directory is `Variant`.

`RootNamespace` is `Kurrent.Surge` on both packable projects. The generated message-catalog namespace
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
| Streams | `StreamsClient` | 14 | V2 for append, V1 for read and manage |
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

`Client/Legacy/` is now just `LegacyExceptionExtensions.cs` and `Uuid.cs`. The vendored `RipAndTear`
transport tree is gone.

For the full operation catalogue and the connection-string reference, read
[Kurrent.Surge client](client.md).

## The processor runtime

`KurrentProcessor` in `Processors/KurrentProcessor.cs` is the public entry point. Call
`Create(IRecordProcessor | IRecordBatchProcessor | Action<MessageProcessor>, ProcessorOptions?)`, then
`RunAsync(ct)` or `RunWithGracefulShutdownAsync(timeout)`. It builds a `KurrentClient`, a bounded
channel, a `KurrentDBCheckpointStore` with a `Checkpointer`, a `KdbDataSource`, and a `Drone`, and
returns the `Drone` as an `IDrone`.

```text
KdbDataSource.Connect   (IStreamsClient.Subscribe → partition-assign → prune caught-up → retry)
   → Channel<DataSourceMessage>          (Connected | Record | Checkpoint, an IVariant)
      → Drone.Run                        (batch, then dispatch to IRecordProcessor or IRecordBatchProcessor)
         → Checkpointer                  (auto-track, manual, shutdown, and rebalance triggers)
            → ICheckpointStore           (append to $processors/{processorId}/checkpoints)
```

The split follows the package boundary. Contracts and the runtime loop live in Core, and the
KurrentDB-specific pieces live in `Kurrent.Surge`.

| Where | What |
|---|---|
| `Kurrent.Surge.Core/Processors/` | `IProcessor`, `IRecordProcessor`, `IRecordBatchProcessor`, `IDrone` and `Drone`, `IDataSource`, `HandlerRegistry`, `DroneService`, plus `Checkpointing/`, `Partitioning/`, `Resilience/`, `Dispatching/`, `Observers/`, `Diagnostics/`, `Configuration/`, and `Models/` |
| `Kurrent.Surge/Processors/` | `KurrentProcessor`, `KdbDataSource`, `KurrentDBCheckpointStore`, `ProcessorResilienceStrategy`, and the connection extensions |

`ProcessorOptions` carries `Subscription`, `Checkpoint`, `Batching`, `Resilience`, and the client
options. The partitioning contracts live in `Core/Processors/Partitioning/`: `IDistributionStrategy`,
`PartitionAssigner`, `PartitionCalculator`, `XXHash`, and the Range and Sticky strategies.

`KurrentDBCheckpointStore` event-sources checkpoints to `$processors/{id}/checkpoints`. It writes one
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
| Governance | `Kurrent.Surge/Client/Schema/Governance/` | `ISchemaGovernance`, `SchemaGovernance`, `CatalogSchemaMap`, `SystemMessageCatalog` |

There is no `ISchemaSerializer` and no `SchemaManager`. Codecs are the serialization abstraction.

Avro ships inside `Kurrent.Surge.Core`. It is not a separate package and it needs no opt-in call.

## Filters

`RecordFilter` is an abstract record in `Core/Client/Modules/Streams/Filters/`. Concrete filters derive
from it, and its static members are the factory: `None`, `ByStream`, `ByStreamPattern`, `ByCategory`,
`BySchema`, `ByUserIndex`, and `ByExpression`.

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
| Streaming | `proto/kurrent/streaming/` | Checkpoint messages, `csharp_namespace = Kurrent.Surge.Streaming.Checkpointing` |
| Google imports | `proto/google/` | Import targets only, never compiled |

`Kurrent.Surge.csproj` compiles `kurrentdb/protocol/{v1,v2}` plus `kurrent/rpc` and `google`, with
`Access="Internal"` and `GrpcServices="Client"`. `Kurrent.Surge.Core.csproj` compiles the checkpoint
protos with `Access="Public"` and message types only.

The V2 **read** path is commented out because those RPCs do not exist in the proto yet. Do not
uncomment it until they do.

## Source generators

| Generator | Project | Triggers on | Emits |
|---|---|---|---|
| `VariantGenerator` | `Variant.SourceGenerators` | a type implementing `IVariant<...>` | the case enum, `Match` and switch members, implicit operators |
| `UniqueIdentifierGenerator` | `Kurrent.Surge.Generators` | `[Kurrent.Surge.UniqueIdentifier]` on a partial type | value-type members, `Guid` operators, a JSON converter |
| `AvroCodecGenerator` | `Kurrent.Surge.Generators` | Avro schema files and annotated types | Avro types, codecs, equality, schema text |
| `WithValidationGenerator` | `Kurrent.Surge.Generators` | `[Kurrent.Surge.Validation.WithValidation]` | options validation |
| `MessageCatalogGenerator` | `Kurrent.Surge.Schema.Catalogs.Generators` | `[assembly: MessageCatalog]` with `[MessageType]` and `[CustomMessageCatalog]` | `{Assembly}.MessageCatalogs`, the provider attribute, `Use{X}Catalog()` extensions |

`VariantGenerator`, `UniqueIdentifierGenerator`, and `WithValidationGenerator` build on
`Kurrent.CodeAnalysis`, which is included **by source** through `Kurrent.CodeAnalysis.Sources.props` so
that each generator is a single self-contained DLL. `MessageCatalogGenerator` uses raw Roslyn APIs.

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

- **`Kurrent.Toolkit.*` is internal.** It sits under `Internal/` in both packages and is not public
  API, even though it is not named `Kurrent.Surge`.
- **`Variant` builds `Kurrent.Variant.dll`** through `<AssemblyName>`, despite the project directory
  name.
- **`Core/Processors/SubscriptionSource.Messages.cs` no longer defines a `SubscriptionSource`.** It
  holds the drone and server control-message variants: `WorkOrder`, `RecordBatch`,
  `ProcessorControlMessage`, and `DroneMessage`. The old `SubscriptionSource`, `Gateway`, and `Hive`
  classes are gone.
- **The V2 read RPCs are commented out by design.**
- **Never cite anything under `obj/`.** It carries pre-rename generated `.g.cs` files whose namespaces
  no longer exist in source.
- **`Experimental/` is not the supported pipeline.** `Components/{Appender,Reader,Consumer}` compile
  but are not wired into `KurrentProcessor`. Treat them as spikes.
- **Projectors are not part of the SDK.** There is no `KurrentProjector` and no
  `Kurrent.Projectors.DuckDB`. That work is parked on a branch. Do not document it as shipping.
