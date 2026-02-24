# Kurrent.Client

Detailed reference for the `Kurrent.Client` package. Read [project.md](project.md)
first for SDK-level context.

---

## Module Architecture

`KurrentClient` is the entry point. It exposes feature modules as properties:

| Module                  | Property            | Status                              |
|-------------------------|---------------------|-------------------------------------|
| Streams                 | `client.Streams`    | Active, near-final                  |
| Registry                | `client.Registry`   | Active, in progress                 |
| Connectors              | `client.Connectors` | Exists in code, fully commented out |
| Users                   | —                   | Commented out, future               |
| PersistentSubscriptions | —                   | Commented out, future               |
| Admin                   | —                   | Commented out, future               |
| Projections             | —                   | Commented out, future               |

All modules inherit from `ClientModuleBase`, which provides: `Logger`,
`SerializerProvider`, `ServerCapabilities`, `TypeMapper`, `MetadataDecoder`,
`TimeProvider`, `LoggerFactory`.

---

## Streams Module

The most mature module. Split across 15+ partial class files under
`Modules/Streams/Operations/`.

### Operations

**Append** (V2 gRPC protocol):
- `AppendRecords(AppendRecordsRequest)` — unary, multi-stream atomic write
- `AppendSession(IAsyncEnumerable<AppendStreamRequest>)` — client-streaming
- `AppendAdapter(...)` — bridges V1-style (stream + expectedState + records)
  through V2 session, converting errors

**Read** (V1 legacy gRPC protocol):
- `InspectRecord(LogPosition)` — single record lookup
- `ReadStreamEdge(StreamName, ReadDirection)` — first/last record
- `ReadRecords(ReadOptions)` — global log read
- `ReadRecords(ReadStreamOptions)` — named stream read
- Returns `RecordStream` (lazy `Channel<ReadRecordsResult>`, single-consumer)

**Manage** (V1 legacy gRPC protocol):
- `GetStreamInfo`, `Delete`, `Tombstone`, `SetStreamMetadata`, `Truncate`,
  `ConfigureStreamDataRetention`

### Transport Duality

V2 gRPC handles all appends. V1 legacy gRPC handles all reads and management.
`StreamsClientV2Mapper` read path is entirely commented out; V2 read RPCs do
not exist in the proto yet.

### Key Model Types

All in `namespace Kurrent.Client.Streams`.

| Type                                | Kind                                                                    | Purpose                                                              |
|-------------------------------------|-------------------------------------------------------------------------|----------------------------------------------------------------------|
| `Record`                            | `record`                                                                | Read-side record with lazy decode via `IRecordDecoder`               |
| `AppendRecord`                      | `record`                                                                | Write-side record with `Validate()`/`EnsureValid()`                  |
| `StreamName`                        | `readonly record struct`                                                | Parses category, partition, metastream from string                   |
| `Revision`                          | `record`                                                                | Stream revision (`long`). Sentinels: `Min`, `Max`, `Unset`           |
| `LogPosition`                       | `readonly partial record struct`                                        | Global log offset. Sentinels: `Earliest`, `Latest`, `Unset`          |
| `RecordPosition`                    | `readonly record struct`                                                | `(LogPosition, StreamName, Revision, Partition)`                     |
| `StreamRevision`                    | `readonly record struct`                                                | Simple `(StreamName, Revision)` pairing                              |
| `ExpectedStreamState`               | `IVariant<ExpectedStreamCondition, Revision>`                           | Write precondition                                                   |
| `ActualStreamState`                 | `IVariant<ActualStreamCondition, Revision>`                             | Actual stream state from server                                      |
| `ConsistencyCheck`                  | `IVariant<StreamConsistencyCheck, QueryConsistencyCheck>`               | Fluent: `ConsistencyCheck.Stream(name).AtRevision(r)`                |
| `ConsistencyCheckFailure`           | `IVariant<StreamConsistencyCheckFailure, QueryConsistencyCheckFailure>` | Failed check details                                                 |
| `StreamInfo`                        | `record`                                                                | Stream metadata, last revision, state                                |
| `StreamMetadata`                    | `record`                                                                | MaxCount, MaxAge, TruncateBefore, CacheControl, Acl                  |
| `StartPosition`                     | `IVariant<LogPosition, Revision>`                                       | Used by processor subscriptions                                      |
| `ReadOptions` / `ReadStreamOptions` | `record`                                                                | Read configuration with builder pattern via `ReadLog` / `ReadStream` |
| `RecordStream`                      | `record`                                                                | Lazy channel-backed `IAsyncEnumerable<ReadRecordsResult>`            |

### Error Types

All decorated with `[KurrentOperationError]`, source-generated. Each operation
returns a `Result<TSuccess, TError>` where `TError` is an `IVariantException`
discriminated union over specific exception types.

Stream exceptions (in `Modules/Streams/Exceptions.cs`):
`StreamNotFoundException`, `StreamAlreadyExistsException`,
`StreamDeletedException`, `StreamTombstonedException`,
`StreamRevisionConflictException`, `ConsistencyViolationException`,
`MaxAppendRecordSizeExceededException`,
`MaxAppendTransactionSizeExceededException`,
`StreamAlreadyInAppendSessionException`,
`AppendSessionNoRequestsException`, `LogPositionNotFoundException`.

Server exceptions (in `Errors/Exceptions.cs`):
`LeaderForwardingTimeoutException`, `ServerNotReadyException`,
`ServerOverloadedException`, `NotLeaderNodeException`.

### Filter System

Filters implement `IRecordFilter`. Factory: `RecordFilter`.

- `ByStream(...)`, `ByStreamPrefix(...)`, `ByCategory(...)` — stream filters
- `BySchema(...)`, `BySchemaPrefix(...)` — schema name filters
- `ByExpression(string)` — SQL-like DSL parsed by a Pratt parser

Expression filter DSL supports: `stream`, `schema_name`, `timestamp`,
`position`, `stream_revision`, `record_id`, `properties[key]`, operators
`=`, `!=`, `>`, `<`, `>=`, `<=`, `BETWEEN`, `IN`, `LIKE`, `ILIKE`,
`IS NULL`, `IS NOT NULL`, `AND`, `OR`, `NOT`.

`ExpressionFilter` extracts a `ServerFilter` for server-side pushdown and
evaluates the full AST client-side via `IsMatch`.

### Metadata

`Metadata` is a mutable dictionary wrapper with fluent `.With()` chains and
lock/copy semantics (`.Lock()`, `.CreateUnlockedCopy()`). Retrieval via
`TryGet<T>` handles `JsonValue`, `TypeDescriptor`, enum, Guid, DateTime
conversion. Schema info is stored in reserved keys (`$schema_name`,
`$schema_format`, `$schema_id`).

---

## Registry Module

`RegistryClient` provides schema management operations against the V2
`SchemaRegistryService` gRPC endpoint (routed via the V1 call invoker).

**Operations:** `CreateSchema`, `GetSchema`, `GetSchemaVersion`,
`GetSchemaVersionById`, `DeleteSchema`, `CheckSchemaCompatibility`.

**Model types:** `Schema`, `SchemaVersion`, `SchemaVersionDescriptor`,
`SchemaVersionId`, `SchemaName`, `CompatibilityMode`, `SchemaIdentifier`
(DU of `SchemaName` | `SchemaVersionId`).

---

## Schema System (partially implemented)

The schema system provides type-safe serialization with optional registry
governance. Located in `src/Kurrent.Client/Schema/`.

### Components

- **`MessageTypeMapper`** — bidirectional CLR Type to SchemaName mapping
- **`SchemaManager`** — registration and validation orchestrator
- **`SchemaSerializerProvider`** — routes to format-specific serializers
- **`SchemaRegistryPolicy`** — merges client options with server requirements

### Serializers

| Serializer                   | Format   | Implementation                |
|------------------------------|----------|-------------------------------|
| `JsonSchemaSerializer`       | JSON     | System.Text.Json              |
| `ProtobufSchemaSerializer`   | Protobuf | Google.Protobuf               |
| `BytesPassthroughSerializer` | Bytes    | Direct passthrough, no schema |

Avro is defined in the proto schema format enum but has no serializer.

### Name Strategies

`SchemaNameStrategies.TypeName()` (default), `.Category()`, `.Namespace(prefix)`,
`.NamespaceCategory(prefix)`. Determines how CLR types map to schema names.

### Registration Flow

```
CLR Type → MessageTypeMapper → SchemaManager → Serializer → Bytes
                                    ↓
                          SchemaRegistryPolicy
                       (local mapping or server)
```

Server-side registration and validation paths exist in the architecture but
are not yet fully wired. The active path is local `AutoMapMessages`: maps CLR
types to schema names via the configured name strategy without server
round-trips.

---

## Options and Configuration

`KurrentClientOptions` is the central configuration record. Created via:
- `KurrentClient.New` (fluent builder)
- `KurrentClientOptions.Parse(connectionString)`
- `KurrentClientOptions.FromConfiguration(prefix)` (IConfiguration binding)
- `AddKurrentClient()` DI extension on `IServiceCollection`

Sub-options: `KurrentClientGossipOptions`, `KurrentClientSecurityOptions`
(Transport + ClientCredentials), `KurrentClientResilienceOptions`,
`KurrentClientSchemaOptions`. All validated with FluentValidation.

---

## Internal Infrastructure

| Directory               | Purpose                                                       |
|-------------------------|---------------------------------------------------------------|
| `Internal/Diagnostics/` | OTel Activity extensions, tracing constants                   |
| `Internal/Extensions/`  | AsyncEnumerable, Channel, Guid, etc. helpers                  |
| `Internal/GoogleRpc/`   | Google RPC Status detail extraction                           |
| `Internal/Grpc/`        | gRPC compression (Brotli), headers interceptor                |
| `Internal/Http/`        | HTTP security, success handler                                |
| `Internal/Options/`     | `OptionsBase<T>`, `OptionsBuilder<T>`, FluentOptionsValidator |
| `Internal/Validation/`  | `ValidationResult`, `ValidationProblem`                       |
| `Legacy/`               | V1 call invoker, cluster client, gossip, error codes          |
| `Legacy/RipAndTear/`    | Ported V1 client internals                                    |

---

## Partial Class File Organization

Operations follow this naming convention:

```
{Module}Client.{Operation}.cs              Core logic
{Module}Client.{Operation}.Models.cs       DTOs
{Module}Client.{Operation}.Mappers.cs      Protocol mapping
{Module}Client.{Operation}.Extensions.cs   Result-returning overloads
{Module}Client.{Operation}.IdiomaticExtensions.cs  Throwing overloads
{Module}Client.{Operation}.Errors.cs       Error variant types
{Module}Client.{Operation}.Models.Builders.cs  Builder patterns
```

---

## Key Files

| Purpose            | Path                                                                           |
|--------------------|--------------------------------------------------------------------------------|
| Client entry point | `src/Kurrent.Client/KurrentClient.cs`                                          |
| Module base class  | `src/Kurrent.Client/Modules/ClientModuleBase.cs`                               |
| StreamsClient root | `src/Kurrent.Client/Modules/Streams/StreamsClient.cs`                          |
| RegistryClient     | `src/Kurrent.Client/Modules/Registry/RegistryClient.cs`                        |
| All stream models  | `src/Kurrent.Client/Modules/Streams/Models/`                                   |
| Stream exceptions  | `src/Kurrent.Client/Modules/Streams/Exceptions.cs`                             |
| Server exceptions  | `src/Kurrent.Client/Errors/Exceptions.cs`                                      |
| Schema system      | `src/Kurrent.Client/Schema/`                                                   |
| MessageTypeMapper  | `src/Kurrent.Client/Schema/MessageTypeMapper.cs`                               |
| V1 mapper          | `src/Kurrent.Client/Modules/Streams/Models/Maps/StreamsClientV1Mapper.cs`      |
| V2 mapper (append) | `src/Kurrent.Client/Modules/Streams/StreamsProtocolMapper.cs`                  |
| Filter parser      | `src/Kurrent.Client/Modules/Streams/Models/Filters/ExpressionFilter.Parser.cs` |
| Options            | `src/Kurrent.Client/Options/KurrentClientOptions.cs`                           |
| DI extensions      | `src/Kurrent.Client/Options/KurrentClientServiceCollectionExtensions.cs`       |
| Error generator    | `src/Kurrent.Client.SourceGenerators/KurrentOperationErrorGenerator.cs`        |
| Variant generator  | `src/Variant.SourceGenerators/VariantGenerator.cs`                             |
| Connection string  | `src/Kurrent.Client/KurrentConnectionString.cs`                                |
| Error messages     | `src/Kurrent.Client/Resources/ErrorMessages.resx`                              |
| Proto v2 streams   | `proto/kurrentdb/protocol/v2/streams/streams.proto`                            |
| Proto v2 registry  | `proto/kurrentdb/protocol/v2/registry/service.proto`                           |
