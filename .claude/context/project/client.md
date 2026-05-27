# Kurrent.Client

> gRPC client library for reading and writing streams to KurrentDB; the foundation package of the SDK.

## Overview

`KurrentClient` is the entry point. It exposes feature modules as properties. All modules inherit from `ClientModuleBase`, which provides: `Logger`, `SerializerProvider`, `ServerCapabilities`, `TypeMapper`, `MetadataDecoder`, `TimeProvider`, `LoggerFactory`.

| Module                                             | Property            | Notes                 |
|----------------------------------------------------|---------------------|-----------------------|
| Streams                                            | `client.Streams`    |                       |
| Registry                                           | `client.Registry`   | V2 gRPC               |
| Indexes                                            | `client.Indexes`    | V2 gRPC, active       |
| Connectors                                         | `client.Connectors` | V2 gRPC, active       |
| Users, PersistentSubscriptions, Admin, Projections | —                   | Commented out, future |

## Key Concepts

### Streams Module

The most mature module. Split across 15+ partial class files under `Modules/Streams/`. See [Module Organization](module-organization.md) for file layout conventions.

**Append** (V2 gRPC): `AppendRecords` (unary, multi-stream atomic), `AppendSession` (client-streaming), `AppendAdapter` (bridges V1-style through V2 session).

**Read** (V1 legacy gRPC): `InspectRecord`, `ReadStreamEdge`, `ReadRecords` (global log + named stream). Returns `RecordStream` — a lazy `Channel<ReadRecordsResult>`, single-consumer.

**Manage** (V1 legacy gRPC): `GetStreamInfo`, `Delete`, `Tombstone`, `SetStreamMetadata`, `Truncate`, `ConfigureStreamDataRetention`.

### Error Handling

Exception types decorated with `[KurrentOperationError]` are source-generated. Operations throw these typed exceptions directly. RPC errors are parsed via `ex.ToException(parsers...)` where each parser is a static `From` method group on the exception type.

### Filter System

Filters implement `IRecordFilter`. Factory: `RecordFilter`. Available filters: `ByStream`, `ByStreamPrefix`, `ByCategory`, `BySchema`, `BySchemaPrefix`, `ByExpression(string)`.

`ByExpression` uses a SQL-like DSL parsed by a Pratt parser. `ExpressionFilter` extracts a `ServerFilter` for server-side pushdown and evaluates the full AST client-side via `IsMatch`.

### Schema System

Located in `src/Kurrent.Client/Schema/`. Type-safe serialization with optional registry governance.

Pipeline: `CLR Type → MessageTypeMapper → SchemaManager → Serializer → Bytes`.

Active path: `AutoMapMessages` — maps CLR types to schema names via the configured name strategy without server round-trips.

**Registration modes** (`SchemaRegistrationOptions`): `AutoMap`, `ManualMap`, `AutoRegister`, `ManualRegister`, `ServerPolicy`.

**Name strategies**: `MessageSchemaNameStrategy` (default), `CategorySchemaNameStrategy`, `NamespaceSchemaNameStrategy`, `NamespaceCategorySchemaNameStrategy`.

**SchemaUrn** format: `urn:schemas-kurrent:{namespace}:{messageName}[:{guid}]`

Serializers: `JsonSchemaSerializer` (System.Text.Json), `ProtobufSchemaSerializer` (Google.Protobuf), `BytesPassthroughSerializer`.

Use `MessageTypeMapper` registration, not reflection.

### Registry Module

`RegistryClient` provides schema management against the V2 `SchemaRegistryService` gRPC endpoint. Operations: `CreateSchema`, `GetSchema`, `GetSchemaVersion`, `DeleteSchema`, `CheckSchemaCompatibility`.

### Options and Configuration

`KurrentClientOptions` is the central configuration record. Created via:
- `KurrentClient.New` (fluent builder)
- `.Parse(connectionString)`
- `.FromConfiguration(prefix)`
- `AddKurrentClient()` DI extension

### Connection Strings

```
kurrentdb://localhost:2113                               # Single node, insecure
kurrentdb://admin:changeit@localhost:2113                # With credentials
kurrentdb+discover://node1:2113,node2:2113               # Cluster discovery
kurrents://localhost:2113?tls=true&tlsVerifyCert=true    # TLS
```

Node preference options: `Leader`, `Follower`, `Random`, `ReadOnlyReplica`.

## Gotchas

- `RecordStream` is single-consumer (backed by `Channel<T>`); do not enumerate it multiple times.
- `Indexes` and `Connectors` use V2 protos but still route through `LegacyCallInvoker` — do not assume V2 means a separate call path.
- Public API surface is locked: do not rename, remove, or change signatures of `public` members without explicit approval.
