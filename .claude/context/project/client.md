# Kurrent.Surge client

> The gRPC client, in `src/Kurrent.Surge/Client/`. It reads and writes streams, and drives the schema
> registry, secondary indexes, connectors, and node administration. Its contracts live in
> `src/Kurrent.Surge.Core/Client/`.

## Entry point

Build a client with `KurrentClient.Create(options)` or
`KurrentClient.Create(connectionString, configure)`. In a host, register it with `AddKurrentSurge`.

Five feature modules hang off it. All five are complete. Streams reads run on the V1 legacy service, as
[Streams](#streams) describes.

| Module | Property | Interface | Operations |
|---|---|---|---:|
| Streams | `client.Streams` | `IStreamsClient` | 14 |
| Registry | `client.Registry` | `IRegistryClient` | 8 |
| Connectors | `client.Connectors` | `IConnectorsClient` | 10 |
| Admin | `client.Admin` | `IAdminClient` | 7 |
| Indexes | `client.Indexes` | `IIndexesClient` | 6 |

Streams, Registry, Connectors, and Indexes inherit `ClientModuleBase`, which supplies `Time`,
`LoggerFactory`, `Logger`, `MetadataDecoder`, and `GetBackdoorClient()`. `AdminClient` does not inherit
it.

## Operations

### Streams

| Group | Operations |
|---|---|
| Append | `AppendRecords` for a multi-stream atomic append, `AppendStream` as single-stream sugar |
| Read | `ReadRecords` over the log, plus `ReadStream`, `InspectRecord`, `FirstRecord`, `LastRecord` |
| Subscribe | `Subscribe` |
| Manage | `GetStreamInfo`, `Delete`, `Tombstone`, `Truncate`, `SetCustomMetadata`, `ConfigureDataRetention` |

`ReadRecords`, `ReadStream`, and `Subscribe` return `IAsyncEnumerable<ReadMessage>` and
`IAsyncEnumerable<SubscribeMessage>`. `AppendRecords` returns `ValueTask<AppendRecordsResponse>`.

Append runs on V2 gRPC. Read, `GetStreamInfo`, `Delete`, and `Tombstone` run on the V1 legacy service.
`Truncate`, `SetCustomMetadata`, and `ConfigureDataRetention` write the metastream through the V2
`AppendStream`. `StreamsClient` therefore holds three service clients: a V2 `ServiceClient`, a
`LegacyServiceClient`, and a `LegacyReadServiceClient` on its own call invoker.

### Registry

`CreateSchema`, `GetSchema`, `GetSchemaVersion`, `GetSchemaVersionById`, `RegisterSchemaVersion`,
`ListSchemas`, `DeleteSchema`, `CheckSchemaCompatibility`.

### Connectors

`Create`, `Reconfigure`, `Rename`, `Reset`, `Start`, `Stop`, `Delete`, `GetSettings`,
`GetConfiguration`, `List`.

### Admin

`StartScavenge`, `StopScavenge`, `GetScavengeDetails`, `ListScavenges`, `ResignNode`,
`SetNodePriority`, `ShutdownServer`.

### Indexes

`Create`, `Get`, `List`, `Start`, `Stop`, `Delete`.

## Options and configuration

`KurrentClientOptions` is the central configuration type, a `sealed partial class` with settable
properties, in `Configuration/`. It is not a record. No public options type in this SDK is a record. Build it
with `.Parse(connectionString)`, with `AddKurrentSurge`, or by configuration binding.

Sub-options hang off it by concern: `Gossip`, `Security`, `Resilience`, `Schema`, `Messages`,
`Compression`, and `Initialization`.

### Connection strings

The parser accepts six schemes. `esdb` and `eventstore` are legacy aliases, kept for migration.

```text
kurrentdb://localhost:2113                          direct
kurrentdb://admin:changeit@localhost:2113           with credentials
kurrentdb+discover://node1:2113,node2:2113          gossip discovery
esdb://…            esdb+discover://…               legacy aliases
eventstore://…      eventstore+discover://…         legacy aliases
```

Query-string keys: `Tls`, `TlsVerifyCert`, `TlsCaFile`, `ConnectionName`, `ReadPreference`,
`NodePreference`, `MaxDiscoverAttempts`, `DiscoveryInterval`, `GossipTimeout`, `DefaultDeadline`,
`ThrowOnAppendFailure`, `KeepAliveInterval`, `KeepAliveTimeout`, `UserCertFile`, `UserKeyFile`,
`UserKeyPassword`.

`ReadPreference` and `NodePreference` take a `NodeReadPreference`: `Leader`, `Follower`, `Random`, or
`ReadOnlyReplica`. If a connection string carries both keys, `ReadPreference` wins.

The parser accepts `ThrowOnAppendFailure`, but the key has no effect.

## Schema governance

`Client/Schema/Governance/` holds `ISchemaGovernance`, `SchemaGovernance`, `CatalogSchemaMap`, and
`CatalogRegistrationFailedException`. Governance owns the registry round-trips: it registers a schema
and checks compatibility.

`KurrentClientSchemaOptions` is the single dial, with three presets:

| Preset | Registration | Validation |
|---|---|---|
| `Disabled` (the default) | manual | off |
| `AutoRegister` | at client initialization | off |
| `Strict` | manual | on |

It carries `RegistrationMode`, which defaults to `Manual`, `ValidationMode`, which defaults to
`Disabled`, `NameStrategy`, which defaults to `SchemaNameStrategies.TypeName()`, and `AppendAsJson`,
which is off by default. When `AppendAsJson` is on, the client appends a record that states no format as JSON.

Codecs live in `Kurrent.Surge.Core/Schema/Codecs/` as `ISchemaCodec` and `SchemaCodecProvider`, with
`Avro/`, `Json/`, and `Protobuf/` implementations. All three are in the Core project, which ships inside
the one `Kurrent.Surge` package, so Avro needs no opt-in package and no opt-in call. There is one codec per `SchemaDataFormat`. Registering two
throws `DuplicateSchemaCodecException`.

## Filters

`RecordFilter` is an abstract record in `Kurrent.Surge.Core/Client/Modules/Streams/Filters/`. Concrete
filters derive from it, and its static members are the factory:

| Factory | Target |
|---|---|
| `RecordFilter.None` | no filtering |
| `RecordFilter.ExcludeSystemRecords` | every record except records whose stream or schema name starts with `$` |
| `Parse`, `TryParse` | a filter expression, the same as `ByExpression` |
| `ByStream`, `ByStreamPattern` | one stream, or a pattern |
| `ByCategory` | a stream category |
| `BySchema` | a schema name |
| `ByUserIndex` | a secondary index |
| `ByExpression(string[, parameters])` | a SQL-like expression |

A Pratt parser parses `ByExpression`. `ExpressionFilter` then extracts a server-side filter for
pushdown and evaluates the full AST on the client.

## Cluster connection

`Client/LoadBalancing/` owns cluster routing. `GossipResolver` and `GrpcGossipClient` discover the
topology, `KurrentDBLoadBalancer` picks a channel, and `LeaderChangeInterceptor` reacts to a leader
change. `ClusterChannel`, `ClusterChannels`, `ClusterEndpoint`, and `ClusterTopology` carry the state.

`Client/Initialization/` runs the startup work: `ClientInitializer` with `IClientInitializationTask`,
plus the `FaultGateCallInvoker` and `FaultGateHttpHandler` pair that fails calls when initialization
faulted.

## Error handling

Operations throw typed exceptions that derive from `KurrentException`, and each one carries an
`ErrorCode.FromType<T>()`. RPC errors map through `ex.ToException(parsers...)`, where each parser comes
from a `Match(...)` factory on the exception type. Many call sites use `ex.AsWellKnownException()`,
which maps the error the same way and then throws it.

## Gotchas

- `ReadRecords` and `Subscribe` return lazy iterators. Each enumeration starts a new RPC.
- Append and the metastream writes are V2. Read, `GetStreamInfo`, `Delete`, and `Tombstone` are V1
  legacy. One module, two protocols.
- A pure-equality expression filter such as `stream = 'X'` routes to a direct stream read rather than a
  `$all` scan. To filter the log, use `LIKE`, `IN`, or a compound expression.
