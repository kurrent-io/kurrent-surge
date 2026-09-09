---
title: Managing Connectors
description: Create, configure, run and inspect connectors from the SDK.
---

`client.Connectors` manages a connector's lifecycle. It does not implement connectors, and it does
not validate their settings.

For what a connector is and which ones exist, see
[Connectors](/kurrent-surge/client/connectors/).

## Settings every sink takes

These are independent of which sink you chose. Per-connector keys live on the pages linked above,
and there are a lot of those keys, so this page does not mirror them.

| Key | Default | Meaning |
|---|---|---|
| `subscription:filter:scope` | | `stream` or `record` |
| `subscription:filter:filterType` | | `streamId`, `prefix`, `regex` or `jsonPath` |
| `subscription:filter:expression` | `""` | The filter itself |
| `subscription:initialPosition` | `latest` | `latest` or `earliest` |
| `transformer:enabled` | `false` | |
| `transformer:function` | `""` | Base64 JavaScript. Required when the transformer is on |
| `resilience:enabled` | `true` | |
| `autocommit:enabled` | `true` | Whether the connector checkpoints for you |
| `autocommit:interval` | `5000` | Milliseconds |
| `autocommit:recordsThreshold` | `1000` | |
| `logging:enabled` | `true` | |

Retry backoff is three bounds, each a pair: `resilience:firstDelayBound:upperLimitMs` and
`:delayMs`, then `second`, then `third`. The third bound defaults to `-1`, which means retry
forever.

`resilience:enabled` is not universal in practice. The Kafka, Pulsar and RabbitMQ sinks carry their
own retry mechanisms and ignore it, as their own pages say explicitly.

## Create

A connector needs an id and a settings dictionary. Keys are camelCase, with colons grouping related
options.

```csharp
Dictionary<string, string> settings = new() {
    ["instanceTypeName"]               = "kafka-sink",
    ["topic"]                          = "orders",
    ["bootstrapServers"]               = "kafka.internal:9092",
    ["authentication:username"]        = "app",
    ["authentication:password"]        = "secret",

    // shared by every sink, not specific to Kafka
    ["subscription:filter:scope"]      = "stream",
    ["subscription:filter:filterType"] = "prefix",
    ["subscription:filter:expression"] = "order-",
    ["subscription:initialPosition"]   = "latest"
};

await client.Connectors.Create("orders-to-kafka", "Orders to Kafka", settings);
```

A working version of this, with partition-key extraction and compression, is in
[`examples/processors/Connector.Management`](https://github.com/kurrent-io/kurrent-surge/tree/main/examples/processors/Connector.Management).

Omit the name and the id becomes the display name.

Settings can also come from `IConfiguration`, including an `IConfigurationSection`, or from a JSON
string. Both flatten to `:`-separated keys, and arrays become numeric indices.

```csharp
await client.Connectors.Create("orders-to-kafka", builder.Configuration.GetSection("Connector"));

await client.Connectors.Create("orders-to-kafka", """
    { "instanceTypeName": "kafka-sink", "topic": "orders" }
    """);
```

**The SDK does not validate any of these settings.** It copies them into the request as you wrote
them, so a misspelled key is not rejected here. The key reaches the server, the connector fails to
start, and the reason arrives in `ErrorDetails` on the next `List`. Check the key against the
connector's own page before you deploy it.

## Reconfigure

`Reconfigure` **replaces** the settings wholesale. It is not a merge. The connector keeps its id,
its name, and its current state. `Reconfigure` accepts the same three input shapes as `Create`.

```csharp
await client.Connectors.Reconfigure("orders-to-kafka", new() { ["topic"] = "orders-v2" });
```

## Rename

Changes the display name only. The id is immutable, and nothing about the connector's settings or
state moves.

```csharp
await client.Connectors.Rename("orders-to-kafka", "Orders → Kafka Sink");
```

## Start and stop

Move the connector between `Running` and `Stopped`. Both are reversible, and neither touches the
stored position.

```csharp
await client.Connectors.Start("orders-to-kafka");
await client.Connectors.Stop("orders-to-kafka");
```

`Start` can resume from a specific log position instead of the stored one:

```csharp
await client.Connectors.Start("orders-to-kafka", 12_345);
```

## Reset

Rewinds the stored position so the connector reprocesses from there. `Reset` changes data, not
lifecycle. It is the one operation here that can cause records to be delivered twice.

**The connector must be stopped first.** Reset fails on a running connector.

```csharp
await client.Connectors.Stop("orders-to-kafka");
await client.Connectors.Reset("orders-to-kafka", 0);
await client.Connectors.Start("orders-to-kafka");
```

Positions must be concrete and non-negative. A sentinel throws `ArgumentOutOfRangeException`, and
`Unset`, `Earliest` and `Latest` are all sentinels.

## Delete

```csharp
await client.Connectors.Delete("orders-to-kafka");
```

Soft delete. The record survives with `DeleteTime` set, and `List` excludes it unless you ask for it.

## List

```csharp
var page = await client.Connectors.List(new ConnectorListOptions {
    States            = [ConnectorState.Running],
    InstanceTypeNames = ["kafka-sink"],
    IncludeSettings   = true,
    PageSize          = 50
});

Console.WriteLine("Showing {0} of {1}", page.Items.Count, page.TotalSize);
```

**Returns `ConnectorListResult`:** `Items`, `TotalSize`, and `HasMorePages`. An empty result is not
an error. You get an empty `Items` and `TotalSize = 0`.

| Option | Default | Meaning |
|---|---|---|
| `States` | empty, meaning all | Filter by state |
| `InstanceTypeNames` | empty, meaning all | Filter by connector type |
| `ConnectorIds` | empty, meaning all | Filter by id |
| `IncludeSettings` | `false` | Populate flat `Settings` |
| `IncludeConfiguration` | `false` | Populate hierarchical `Configuration` |
| `ShowDeleted` | `false` | Include soft-deleted |
| `Page` | `1` | 1-based |
| `PageSize` | `50` | |

### ConnectorDetails

| Member | Type | Meaning |
|---|---|---|
| `ConnectorId` | `ConnectorId` | Identifier |
| `Name` | `string` | Display name |
| `InstanceTypeName` | `string` | e.g. `kafka-sink` |
| `State` | `ConnectorState` | See below |
| `Position` | `LogPosition` | Last processed |
| `Settings` | `IReadOnlyDictionary<string,string>` | Only with `IncludeSettings` |
| `Configuration` | `JsonObject?` | Only with `IncludeConfiguration` |
| `ErrorDetails` | `string?` | Last reported error |
| `CreateTime` / `UpdateTime` / `DeleteTime` | `DateTimeOffset` | `DeleteTime` is `default` while active |
| `StateUpdateTime` / `SettingsUpdateTime` / `ConfigurationUpdateTime` / `PositionUpdateTime` | `DateTimeOffset` | When that field last changed |
| `IsRunning` / `IsStopped` / `IsDeleted` | `bool` | Shortcuts |

### States

| State | Meaning |
|---|---|
| `Unknown` | Not yet reported |
| `Activating` | Moving to `Running` |
| `Running` | Processing |
| `Deactivating` | Moving to `Stopped` |
| `Stopped` | Idle |

## Get settings or configuration

Cheaper than listing with the include flags when you want one connector.

```csharp
var settings = await client.Connectors.GetSettings("orders-to-kafka");
foreach (var (key, value) in settings.Settings)
    Console.WriteLine("{0} = {1}", key, value);

var config = await client.Connectors.GetConfiguration("orders-to-kafka");
Console.WriteLine(config.Configuration.ToJsonString());
```

`GetSettings` returns the flat dictionary plus `SettingsUpdateTime`. `GetConfiguration` returns a
`JsonObject`, which preserves nesting and types, plus `ConfigurationUpdateTime`.

## Exceptions

| Exception | Thrown by | When |
|---|---|---|
| `ConnectorAlreadyExistsException` | `Create` | The id is taken |
| `ConnectorNotFoundException` | everything but `Create` | No such connector |
| `ConnectorDeletedException` | everything but `Create` | It was soft-deleted |
| `ArgumentException` | all | Empty id; `Create`/`Rename` also reject a blank name |
| `ArgumentOutOfRangeException` | `Start`, `Reset` | The position is a sentinel or negative |
