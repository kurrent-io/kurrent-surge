# Architecture

> Layered monorepo where each package is independently installable; `Kurrent.Client` is the foundation.

## Architectural Style

- **Feature-modular with layered dependencies** — each publishable package owns a vertical slice of
  functionality. Within `Kurrent.Client`, modules (Streams, Registry, Indexes, Connectors, Admin) are
  isolated behind their own client type, sharing only the base infrastructure in `ClientModuleBase`.
- **Dual-protocol gRPC** — V1 legacy handles reads, subscriptions, and admin; V2 handles appends,
  registry, indexes, and connectors. Both are compiled from proto definitions into the same assembly.
- **Source-generated plumbing** — discriminated unions (`IVariant`), exception constructors
  (`[KurrentOperationError]`), and message type resolvers are all generated at compile time. No
  reflection.
- **Primary constraint**: AOT compatibility — all serialization uses source generators; no reflection,
  no `Activator.CreateInstance`, no runtime expression compilation.

## Components

### Publishable Packages

| Package             | Purpose                                                     | Depends on       |
|---------------------|-------------------------------------------------------------|------------------|
| `Kurrent.Client`    | gRPC client (streams, registry, connectors, indexes, admin) | None             |
| `Kurrent.Streaming` | Stream processing, projections, checkpointing               | `Kurrent.Client` |

### Solution-Level Shared Libraries (not published)

| Library                           | Purpose                                                                   | TFM            |
|-----------------------------------|---------------------------------------------------------------------------|----------------|
| `Variant`                         | Discriminated union interfaces + `Result<T,E>`                            | net10.0        |
| `Variant.SourceGenerators`        | Generates `IVariant` union plumbing                                       | netstandard2.0 |
| `Kurrent.Client.SourceGenerators` | Generates `[KurrentOperationError]` exception types + message resolvers   | netstandard2.0 |
| `Kurrent.CodeAnalysis`            | Shared Roslyn infrastructure (type pipeline, source writer, symbol model) | netstandard2.0 |

`Variant` is embedded into consuming packages via `PrivateAssets=all` (not a separate NuGet dependency).

### Test Infrastructure

| Project                    | Purpose                                                                                      |
|----------------------------|----------------------------------------------------------------------------------------------|
| `Aspire.Hosting.KurrentDB` | Aspire integration: container lifecycle, health checks, TLS certs                            |
| `Kurrent.Sdk.Testkit`      | Test fixtures: `KurrentDBFixture`, `*InsecureFixture`, `*ScavengeFixture`, `*ClusterFixture` |
| `Kurrent.Testkit`          | Reusable datasets (ArcadeRacer, HomeAutomation), TUnit extensions, Bogus wrapper             |

## Kurrent.Client Internals

### Entry Point

`KurrentClient` (`src/Kurrent.Client/KurrentClient.cs`) implements `IKurrentClient`. Exposes five
module clients as properties:

| Module                                      | Property            | Protocol | Status                |
|---------------------------------------------|---------------------|----------|-----------------------|
| Streams                                     | `client.Streams`    | V1 + V2  | Active                |
| Registry                                    | `client.Registry`   | V2       | Active                |
| Indexes                                     | `client.Indexes`    | V2       | Active                |
| Connectors                                  | `client.Connectors` | V2       | Active                |
| Admin                                       | `client.Admin`      | V1       | Active                |
| Users, PersistentSubscriptions, Projections | —                   | —        | Commented out, future |

All modules inherit from `ClientModuleBase` (`src/Kurrent.Client/Modules/ClientModuleBase.cs`), which
provides: `KurrentClient`, `Logger`, `SerializerProvider`, `ServerCapabilities`, `TypeMapper`,
`MetadataDecoder`, `TimeProvider`, `LoggerFactory`, `GetBackdoorClient()`.

### Module Structure

Each module lives under `src/Kurrent.Client/Modules/{Module}/`. The Streams module — the most mature —
is split across 15+ partial class files:

| File pattern                                   | Content                |
|------------------------------------------------|------------------------|
| `StreamsClient.{Operation}.cs`                 | Core operation method  |
| `StreamsClient.{Operation}.Models.cs`          | Request/response types |
| `StreamsClient.{Operation}.Mappers.cs`         | Proto ↔ domain mapping |
| `StreamsClient.{Operation}.Extensions.cs`      | Convenience overloads  |
| `StreamsClient.{Operation}.Models.Builders.cs` | Builder types          |

**Streams operations**: AppendRecords (V2), AppendStream (V2), ReadRecords (V1), ReadStreamEdge (V1),
InspectRecord (V1), Subscribe (V1), Query, Manage (Delete, Tombstone, SetStreamMetadata, Truncate,
ConfigureStreamDataRetention, GetStreamInfo).

### Schema System

Located at `src/Kurrent.Client/Schema/`. Type-safe serialization with optional registry governance.

Pipeline: `CLR Type → MessageTypeMapper → SchemaManager → Serializer → Bytes`.

| Component                   | Path                                    | Role                                                                   |
|-----------------------------|-----------------------------------------|------------------------------------------------------------------------|
| `MessageTypeMapper`         | `Schema/MessageTypeMapper.cs`           | Maps CLR types to schema names                                         |
| `SchemaRegistrationOptions` | `Schema/SchemaRegistrationOptions.cs`   | Policy: AutoMap, ManualMap, AutoRegister, ManualRegister, ServerPolicy |
| Name strategies             | `Schema/NameStrategies/`                | Message, Category, Namespace, NamespaceCategory                        |
| `SchemaUrn`                 | `Schema/NameStrategies/SchemaUrn.cs`    | Format: `urn:schemas-kurrent:{ns}:{name}[:{guid}]`                     |
| `SchemaManager`             | `Schema/Serialization/SchemaManager.cs` | Orchestrates schema resolution                                         |
| `SchemaSerializerProvider`  | `Schema/Serialization/`                 | Routes to Json, Protobuf, or Bytes serializer                          |
| `NJsonSchemaExporter`       | `Schema/NJsonSchemaExporter.cs`         | Exports JSON Schema for registry                                       |

Serializers: `JsonSchemaSerializer` (System.Text.Json), `ProtobufSchemaSerializer` (Google.Protobuf),
`BytesPassthroughSerializer`.

### Error Handling

Exception types decorated with `[KurrentOperationError(ErrorCode)]` are source-generated by
`KurrentOperationErrorGenerator`. Each generated type implements `IKurrentOperationException<T>` with
`TryParseRpcStatus`/`From` factory methods. Operations throw these directly; RPC errors are parsed via
`ex.ToException(parsers...)` where parsers are static `From` method groups.

### Filter System

Filters implement `IRecordFilter`. Factory: `RecordFilter`. Available: `ByStream`, `ByStreamPrefix`,
`ByCategory`, `BySchema`, `BySchemaPrefix`, `ByExpression(string)`.

`ByExpression` uses a SQL-like DSL parsed by a Pratt parser. `ExpressionFilter` extracts a `ServerFilter`
for server-side pushdown and evaluates the full AST client-side via `IsMatch`.

### Legacy Layer

`src/Kurrent.Client/Legacy/` wraps the original EventStore-style V1 gRPC channel management:
`KurrentDBLegacyCallInvoker`, gossip-based `LegacyClusterClient`, `ChannelSelector` hierarchy (single-node
and gossip variants), `AuthenticationInterceptor`, `LeaderNotFoundInterceptor`, `TypedExceptionInterceptor`,
server capabilities detection. The `RipAndTear/` subfolder marks code intended for eventual removal.

### Internal Utilities

`src/Kurrent.Client/Internal/` provides cross-cutting infrastructure:

| Area        | Key types                                                                                            |
|-------------|------------------------------------------------------------------------------------------------------|
| Diagnostics | `ActivityExtensions`, `TelemetryTags`, `TracingConstants`                                            |
| Extensions  | `AsyncEnumerableExtensions`, `ChannelExtensions`, `GuidExtensions`, `StringExtensions` (C# 14 style) |
| GoogleRpc   | `StatusExtensions`, `DebugInfoExtensions`, exception mappers                                         |
| gRPC        | `GrpcServerStream`, `CompressionInterceptor`, `HeadersInterceptor`                                   |
| HTTP        | `HttpSecurity`, `EnsureSuccessStatusHandler`                                                         |
| Validation  | `SelfValidating`, `Validator`, `ValidationException`                                                 |
| Core        | `FluentBuilder`, `ConcurrentBidirectionalDictionary`, `TokenCompletionSource`                        |

## Kurrent.Streaming Internals

Stream processing framework built on top of `Kurrent.Client`. Key dependency graph:

```text
KurrentProcessor → Drone → SubscriptionSource → StreamsClient.Subscribe
                 → Checkpointer → KurrentDBCheckpointStore
                 → PartitionAssigner → XXHash
```

### Core Abstractions

| Type                    | Path                       | Role                                               |
|-------------------------|----------------------------|----------------------------------------------------|
| `KurrentProcessor`      | `KurrentProcessor.cs`      | Public entry point; `Create()`, `RunAsync()`       |
| `IRecordProcessor`      | `IRecordProcessor.cs`      | Per-record handler                                 |
| `IRecordBatchProcessor` | `IRecordBatchProcessor.cs` | Batch handler                                      |
| `IDrone`                | `IDrone.cs`                | Runtime control surface for a running processor    |
| `Drone`                 | `Drone.cs`                 | Connects subscription → channel → processor loop   |
| `MessageProcessor`      | `MessageProcessor.cs`      | Fluent `IRecordProcessor` via handler registration |

### Subsystems

| Subsystem     | Path             | Purpose                                                                                                                |
|---------------|------------------|------------------------------------------------------------------------------------------------------------------------|
| Checkpointing | `Checkpointing/` | `ICheckpointStore`, `KurrentDBCheckpointStore` (event-sourced to `$processors/{id}/checkpoints`), `HybridLogicalClock` |
| Partitioning  | `Partitioning/`  | `IDistributionStrategy`, `PartitionAssigner`, `XXHash`, Range and Sticky strategies                                    |
| Gateway       | `Gateway/`       | `ISubscriptionSource`, `SubscriptionSource` (wraps `StreamsClient.Subscribe` into a channel)                           |
| Hive          | `Hive/`          | Runs N Drones against a shared `MasterSubscriptionSource`                                                              |
| Projections   | `Projections/`   | `KurrentProjector`, `RecordProjector`, DuckDB projection store                                                         |
| Hosting       | `Hosting/`       | `DroneService<T>` (IHostedService), `AddKurrentProcessor<T>` DI extension                                              |
| Leases        | `Leases/`        | `ILeaseManager` for distributed processor coordination                                                                 |
| Locks         | `Locks/`         | `ServiceLocker` for exclusive access                                                                                   |
| Pipeline      | `Pipeline/`      | `SinkCoordinator`, Kafka sink                                                                                          |
| Configuration | `Configuration/` | `ProcessorOptions`, `CheckpointOptions`, `SubscriptionOptions`                                                         |
| Diagnostics   | `Diagnostics/`   | `ProcessorMetrics`, `DroneTelemetry`, OTel integration                                                                 |
| Experimental  | `Experimental/`  | Alternative `Consumer`/`Drone`/`Subscription` implementations                                                          |

## Protocols

Proto layout has three layers plus shared namespaces:

| Layer              | Path                           | Status                                         |
|--------------------|--------------------------------|------------------------------------------------|
| V0 (draft)         | `proto/kurrentdb/protocol/v0/` | Excluded from build                            |
| V1 (legacy gRPC)   | `proto/kurrentdb/protocol/v1/` | Active: reads, subscriptions, admin, gossip    |
| V2 (new gRPC)      | `proto/kurrentdb/protocol/v2/` | Active: appends, registry, indexes, connectors |
| Shared error types | `proto/kurrent/rpc/`           | Public, compiled without GrpcServices          |
| Google imports     | `proto/google/`                | Not compiled, import targets only              |

V1 service: `KurrentDB.Protocol.V1.Streams.Streams.StreamsClient` — reads, subscriptions, management.
V2 service: `KurrentDB.Protocol.V2.Streams.StreamsService.StreamsServiceClient` — appends.

Both are compiled from `Kurrent.Client.csproj` via `<Protobuf>` item groups with `Access="Internal"` and
`GrpcServices="Client"`. V0 protos are excluded via glob. The `kurrent/rpc/` protos are compiled as
`Access="Public"` without gRPC service generation (message types only).

V2 read path is entirely commented out; those RPCs do not exist yet. Despite using V2 protos, Connectors
and Indexes still route through `LegacyCallInvoker` — the V1/V2 label describes protocol version, not
call path.

## Data Flow

### Append (V2 gRPC)

```text
AppendRecords(request) → EnsureValid() → StreamsProtocolMapper.ToContract()
  → SchemaSerializerProvider.GetSerializer(format).Serialize(value, context)
  → StreamsServiceClient.AppendRecordsAsync(V2 proto request)
  → MapToAppendRecordsResponse() → AppendRecordsResponse { StreamPosition[], LogPosition }
```

On error: `ex.AsWellKnownException(parsers...)` maps gRPC status to typed SDK exceptions.

### Read (V1 legacy gRPC)

```text
ReadRecords(request) → EnsureValid() → CreateReadRecordsRequest(V1 ReadReq)
  → LegacyStreamsServiceClient.Read(request) [server-streaming]
  → for each ReadResp:
      StreamNotFound → throw typed exception
      CaughtUp → yield Heartbeat
      Event → MapToRecordAsync(deserialize, decode metadata) → apply filter → yield Record
```

Returns `IAsyncEnumerable<ReadRecordsResult>` (variant of `Record | Heartbeat`).

### Subscribe (V1 legacy gRPC)

```text
Subscribe(filter, startPosition) → build V1 ReadReq (stream or log subscription)
  → LegacyServiceClient.Read(request)
  → first message: Confirmation → yield Connected(sessionId)
  → loop:
      Checkpoint → yield IntervalCheckpoint or CaughtUpCheckpoint
      Event → deserialize → filter.IsMatch → yield Record
```

Returns `IAsyncEnumerable<SubscribeMessage>` — a source-generated variant of
`Connected | Record | Checkpoint`.

### Streaming Processor

```text
KurrentProcessor.RunAsync()
  → Drone.RunAsync()
    → SubscriptionSource.Subscribe() wraps StreamsClient.Subscribe into Channel<SubscribeMessage>
    → for each message:
        compute partition key via XXHash
        dispatch to IRecordProcessor.ProcessRecord or IRecordBatchProcessor.ProcessBatch
        checkpoint via Checkpointer → KurrentDBCheckpointStore (appends to $processors/{id}/checkpoints)
```

## Source Generators

| Generator                              | Project                           | Trigger                        | Generates                                                             |
|----------------------------------------|-----------------------------------|--------------------------------|-----------------------------------------------------------------------|
| `VariantGenerator`                     | `Variant.SourceGenerators`        | `IVariant<T1,...,Tn>`          | Case enum, `IsX`/`AsX`, implicit ops, `Switch`/`Match` (sync + async) |
| `VariantResultsGenerator`              | `Variant.SourceGenerators`        | Result variant types           | Result-specific operator overloads                                    |
| `KurrentOperationErrorGenerator`       | `Kurrent.Client.SourceGenerators` | `[KurrentOperationError]`      | Exception ctors, `TryParseRpcStatus`, `From` factory, `ErrorCode`     |
| `MessageDiscoveryGenerator`            | `Kurrent.Client.SourceGenerators` | `[MessageDiscovery("prefix")]` | `MessageResolver` with `string → Type` switch                         |
| `ResultBaseImplicitOperatorsGenerator` | `Kurrent.Client.SourceGenerators` | Result base types              | Implicit conversion operators                                         |

All generators share `Kurrent.CodeAnalysis` for Roslyn pipeline helpers, symbol model wrappers, and
source emission utilities.

## Gotchas

- The `Result` type in `src/Variant/` is intentionally unused by client/processor code — do not wire it back in.
- V2 read code is commented out by design; do not uncomment it until the proto RPCs are defined.
- Never reach from a higher-level package into `Kurrent.Client` internals.
- `Kurrent.Toolkit` is referenced in CLAUDE.md but does not exist as a project — do not create it.
- `src/Shared/` exists as an empty directory; no shared source code lives there.
- The `Legacy/RipAndTear/` folder marks V1 channel infrastructure slated for removal.
- `Variant` assembly is named `Kurrent.Variant` (via `<AssemblyName>`) despite the project directory name.
