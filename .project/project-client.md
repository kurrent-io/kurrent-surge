# Kurrent.Client

Detailed reference for the `Kurrent.Client` package. Read [project.md](project.md) first for SDK-level context.

## Contents

- [Module Architecture](#module-architecture)
- [Streams Module](#streams-module)
- [Registry Module](#registry-module)
- [Schema System](#schema-system)
- [Options and Configuration](#options-and-configuration)
- [Connection Strings](#connection-strings)

## Module Architecture

`KurrentClient` is the entry point. It exposes feature modules as properties:

| Module                                             | Property            | Notes                               |
|----------------------------------------------------|---------------------|-------------------------------------|
| Streams                                            | `client.Streams`    |                                     |
| Registry                                           | `client.Registry`   |                                     |
| Connectors                                         | `client.Connectors` | Exists in code, fully commented out |
| Users, PersistentSubscriptions, Admin, Projections | —                   | Commented out, future               |

All modules inherit from `ClientModuleBase`, which provides: `Logger`, `SerializerProvider`, `ServerCapabilities`,
`TypeMapper`, `MetadataDecoder`, `TimeProvider`, `LoggerFactory`.

## Streams Module

The most mature module. Split across 15+ partial class files under `Modules/Streams/Operations/`.
See `StreamsClient.Append.cs` for the canonical file naming pattern.

### Operations

**Append** (V2 gRPC): `AppendRecords` (unary, multi-stream atomic), `AppendSession` (client-streaming),
`AppendAdapter` (bridges V1-style through V2 session).

**Read** (V1 legacy gRPC): `InspectRecord`, `ReadStreamEdge`, `ReadRecords` (global log + named stream). Returns
`RecordStream` (lazy `Channel<ReadRecordsResult>`, single-consumer).

**Manage** (V1 legacy gRPC): `GetStreamInfo`, `Delete`, `Tombstone`, `SetStreamMetadata`, `Truncate`,
`ConfigureStreamDataRetention`.

### Error Types

All decorated with `[KurrentOperationError]`, source-generated. Each operation returns `Result<TSuccess, TError>`
where `TError` is an `IVariantException`. See `src/Kurrent.Client/Modules/Streams/Exceptions.cs:14` for the full set.

### Filter System

Filters implement `IRecordFilter`. Factory: `RecordFilter`. `ByStream`, `ByStreamPrefix`, `ByCategory`, `BySchema`,
`BySchemaPrefix`, `ByExpression(string)` (SQL-like DSL parsed by a Pratt parser). `ExpressionFilter` extracts a
`ServerFilter` for server-side pushdown and evaluates the full AST client-side via `IsMatch`.

## Registry Module

`RegistryClient` provides schema management against the V2 `SchemaRegistryService` gRPC endpoint. Operations:
`CreateSchema`, `GetSchema`, `GetSchemaVersion`, `DeleteSchema`, `CheckSchemaCompatibility`.

## Schema System

Located in `src/Kurrent.Client/Schema/`. Type-safe serialization with optional registry governance.

`CLR Type -> MessageTypeMapper -> SchemaManager -> Serializer -> Bytes`. The active path is local `AutoMapMessages`:
maps CLR types to schema names via the configured name strategy without server round-trips.

Serializers: `JsonSchemaSerializer` (System.Text.Json), `ProtobufSchemaSerializer` (Google.Protobuf),
`BytesPassthroughSerializer` (direct passthrough).

## Options and Configuration

`KurrentClientOptions` is the central configuration record. Created via `KurrentClient.New` (fluent),
`.Parse(connectionString)`, `.FromConfiguration(prefix)`, or `AddKurrentClient()` DI extension.

## Connection Strings

```
kurrentdb://localhost:2113                            # Single node, insecure
kurrentdb://admin:changeit@localhost:2113             # With credentials
kurrentdb+discover://node1:2113,node2:2113            # Cluster discovery
kurrents://localhost:2113?tls=true&tlsVerifyCert=true # TLS
```

Node preference: `Leader`, `Follower`, `Random`, `ReadOnlyReplica`.
