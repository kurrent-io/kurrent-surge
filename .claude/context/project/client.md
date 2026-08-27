# Kurrent.Surge

> The gRPC client, in `src/Kurrent.Surge/Client/`. Reads and writes streams, and drives the schema
> registry, secondary indexes, connectors, and node administration. Its contracts live in
> `src/Kurrent.Surge.Core/Client/`.

## Overview

`KurrentClient` is the entry point, built with `KurrentClient.Create(options)` or
`KurrentClient.Create(connectionString, configure)`. It exposes five feature modules as properties.
Modules inherit `ClientModuleBase`, which provides `Logger`, `LoggerFactory`, `Time`,
`MetadataDecoder`, and `GetBackdoorClient()`.

| Module     | Property            | Interface           |
|------------|---------------------|---------------------|
| Streams    | `client.Streams`    | `IStreamsClient`    |
| Registry   | `client.Registry`   | `IRegistryClient`   |
| Indexes    | `client.Indexes`    | `IIndexesClient`    |
| Connectors | `client.Connectors` | `IConnectorsClient` |
| Admin      | `client.Admin`      | `IAdminClient`      |

Users, PersistentSubscriptions, and Projections are commented out and future.

## What the client can do

### Streams

| Group     | Operations                                                                    |
|-----------|-------------------------------------------------------------------------------|
| Append    | `AppendRecords` (multi-stream atomic), `AppendStream` (single-stream sugar)   |
| Read      | `ReadRecords` (log), `ReadStream`, `InspectRecord`, `FirstRecord`, `LastRecord` |
| Subscribe | `Subscribe`                                                                   |
| Manage    | `GetStreamInfo`, `Delete`, `Tombstone`, `Truncate`, `SetCustomMetadata`, `ConfigureDataRetention` |

`ReadRecords`, `ReadStream`, and `Subscribe` return `IAsyncEnumerable<ReadMessage>` and
`IAsyncEnumerable<SubscribeMessage>`. `AppendRecords` returns `ValueTask<AppendRecordsResponse>`.

Append runs on V2 gRPC. Read and manage run on the V1 legacy service — `StreamsClient` holds both a
`ServiceClient` (V2) and a `LegacyServiceClient`, plus a separate `LegacyReadServiceClient` on its own
call invoker.

### Registry

`CreateSchema`, `GetSchema`, `GetSchemaVersion`, `GetSchemaVersionById`, `RegisterSchemaVersion`,
`ListSchemas`, `DeleteSchema`, `CheckSchemaCompatibility`.

### Indexes

`Create`, `Get`, `List`, `Start`, `Stop`, `Delete`.

### Connectors

`Create`, `Reconfigure`, `Rename`, `Reset`, `Start`, `Stop`, `Delete`, `GetSettings`,
`GetConfiguration`, `List`.

### Admin

`StartScavenge`, `StopScavenge`, `GetScavengeDetails`, `ListScavenges`, `ResignNode`,
`SetNodePriority`, `ShutdownServer`.

## Key Concepts

### Module file layout

The Streams module is the largest, split across 16 partial files under `Modules/Streams/`. See
[Module Organization](module-organization.md) for the conventions.

### Record pipelines

`Modules/Streams/Pipeline/` holds `ReadRecordPipeline` and `WriteRecordPipeline` with their step
files. Records move through a composed step chain rather than a stateful mapper.

### Schema governance

`Client/Schema/Governance/` holds `ISchemaGovernance`, `SchemaGovernance`, `CatalogSchemaMap`, and
`CatalogRegistrationFailedException`. Governance owns registry round-trips: registering a schema and
checking compatibility.

`KurrentClientSchemaOptions` is the single dial, with three presets:

| Preset         | Meaning                       |
|----------------|-------------------------------|
| `Disabled`     | default                       |
| `AutoRegister` | register on first use         |
| `Strict`       | registration plus validation  |

It carries `RegistrationMode` (`SchemaRegistrationMode`, default `Manual`), `ValidationMode`
(`SchemaValidationMode`, default `Disabled`), and `NameStrategy` (`ISchemaNameStrategy`, default
`SchemaNameStrategies.TypeName()`).

Codecs live in `Kurrent.Surge.Core/Schema/Codecs/` — `ISchemaCodec` and `SchemaCodecProvider`, with
`Json/` and `Protobuf/` implementations. One codec per `SchemaDataFormat`; registering two throws
`DuplicateSchemaCodecException`.

### Filters

`RecordFilter` is an abstract record — concrete filters derive from it, and its static members are
the factory:

| Factory                                | Target                        |
|----------------------------------------|-------------------------------|
| `RecordFilter.None`                    | no filtering                  |
| `ByStream`, `ByStreamPattern`          | one stream, or a pattern      |
| `ByCategory`                           | a stream category             |
| `BySchema`                             | a schema name                 |
| `ByUserIndex`                          | a secondary index             |
| `ByExpression(string[, parameters])`   | a SQL-like expression         |

`ByExpression` is parsed by a Pratt parser. `ExpressionFilter` extracts a server-side filter for
pushdown and evaluates the full AST client-side.

### Cluster connection

`Client/LoadBalancing/` owns cluster routing: `GossipResolver` and `GrpcGossipClient` discover
topology, `KurrentDBLoadBalancer` picks a channel, and `LeaderChangeInterceptor` reacts to a leader
change. `ClusterChannels`, `ClusterEndpoint`, and `ClusterTopology` carry the state.

`Client/Initialization/` runs startup work: `ClientInitializer` with `IClientInitializationTask`, and
the `FaultGateCallInvoker` / `FaultGateHttpHandler` pair that fails calls when initialization faulted.

### Options and configuration

`KurrentClientOptions` is the central configuration record. Created via `.Parse(connectionString)` or
the `AddKurrentClient()` DI extension.

```
kurrentdb://localhost:2113                               # Single node, insecure
kurrentdb://admin:changeit@localhost:2113                # With credentials
kurrentdb+discover://node1:2113,node2:2113               # Cluster discovery
kurrents://localhost:2113?tls=true&tlsVerifyCert=true    # TLS
```

`NodeReadPreference` selects the node: `Leader`, `Follower`, `Random`, `ReadOnlyReplica`. The
connection-string parameter is `ReadPreference`; `NodePreference` is still accepted as an alias.

### Error handling

Operations throw typed exceptions deriving from `KurrentException`, each carrying an
`ErrorCode.FromType<T>()`. RPC errors are mapped through `ex.ToException(parsers...)`, where each
parser is a static `From` method group on the exception type.

## Gotchas

- Read and subscribe results are `IAsyncEnumerable<T>` over a channel — enumerate once.
- Append is V2; read and manage are V1 legacy. One module, two protocols.
- Public API surface is locked: do not rename, remove, or change signatures of `public` members
  without explicit approval.
