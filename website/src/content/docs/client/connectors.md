---
title: Connectors
description: Create, configure, run and inspect connectors.
---

Connectors stream records from KurrentDB into external systems. This module covers their whole
lifecycle. The connector implementations themselves live on the server; here you manage them.


## Create

A connector needs an id and a settings dictionary. Keys are camelCase, with colons grouping related
options.

```csharp
Dictionary<string, string> settings = new() {
    ["instanceTypeName"]        = "kafka-sink",
    ["topic"]                   = "orders",
    ["bootstrapServers"]        = "kafka.internal:9092",
    ["authentication:username"] = "app",
    ["authentication:password"] = "secret"
};

await client.Connectors.Create("orders-to-kafka", "Orders to Kafka", settings);
```

Omit the name and the id becomes the display name.

Settings can also come from `IConfiguration`, including an `IConfigurationSection`, or from a JSON
string. Both flatten to `:`-separated keys, and arrays become numeric indices.

```csharp
await client.Connectors.Create("orders-to-kafka", builder.Configuration.GetSection("Connector"));

await client.Connectors.Create("orders-to-kafka", """
    { "instanceTypeName": "kafka-sink", "topic": "orders" }
    """);
```

The available keys are per connector type and documented with the connector itself.

## Reconfigure

`Reconfigure` **replaces** the settings wholesale. It is not a merge. The connector keeps its id,
its name, and its current state. It accepts the same three input shapes as `Create`.

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

Rewinds the stored position so the connector reprocesses from there. This changes data, not
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
| `Configuration` | `Struct?` | Only with `IncludeConfiguration` |
| `ErrorDetails` | `string?` | Last reported error |
| `CreateTime` / `UpdateTime` / `DeleteTime` | `DateTimeOffset` | `DeleteTime` is `default` while active |
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
var flat   = config.Configuration.FlattenStruct();
```

`GetSettings` returns the flat dictionary plus `SettingsUpdateTime`. `GetConfiguration` returns a
Protobuf `Struct`, which preserves nesting and types, plus `ConfigurationUpdateTime`.

## Exceptions

| Exception | Thrown by | When |
|---|---|---|
| `ConnectorAlreadyExistsException` | `Create` | The id is taken |
| `ConnectorNotFoundException` | everything but `Create` | No such connector |
| `ConnectorDeletedException` | everything but `Create` | It was soft-deleted |
| `ArgumentException` | all | Empty id; `Create`/`Rename` also reject a blank name |
| `ArgumentOutOfRangeException` | `Start`, `Reset` | The position is a sentinel or negative |
