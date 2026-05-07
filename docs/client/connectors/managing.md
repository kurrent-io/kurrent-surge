---
order: 1
---

# Managing connectors

Connectors stream events from KurrentDB into external systems. The `client.Connectors` module covers the connector lifecycle: create, reconfigure, start, stop, reset, rename, and delete.

```csharp
using Kurrent.Client.Connectors;

var connectorId = ConnectorId.From("orders-to-kafka");
```

`ConnectorId` is a typed wrapper around a non-empty string. It accepts implicit conversion from `string`, so any of these work:

```csharp
ConnectorId id = "orders-to-kafka";
await client.Connectors.Create(id, settings);
await client.Connectors.Create("orders-to-kafka", settings);
```

All command methods return `Success` on completion and throw on failure. See [Error handling](../error-handling.md) for the exception catalogue.

## Create

A connector needs an id and a configuration. Two configuration shapes are supported: a flat dictionary of key-value strings, and a hierarchical Protobuf `Struct`.

### Flat settings

```csharp
var settings = new Dictionary<string, string?> {
    ["instanceTypeName"] = "kafka-sink",
    ["topic"]            = "orders",
    ["bootstrap.servers"] = "kafka.internal:9092"
};

await client.Connectors.Create(
    connectorId: "orders-to-kafka",
    name: "Orders → Kafka",
    settings: settings
);
```

Null values are stored as empty strings.

### Hierarchical configuration

For nested settings, use a `Google.Protobuf.WellKnownTypes.Struct`:

```csharp
using Google.Protobuf.WellKnownTypes;

var configuration = new Struct {
    Fields = {
        ["instanceTypeName"] = Value.ForString("kafka-sink"),
        ["topic"]            = Value.ForString("orders"),
        ["kafka"] = Value.ForStruct(new Struct {
            Fields = {
                ["bootstrap.servers"] = Value.ForString("kafka.internal:9092"),
                ["acks"]              = Value.ForString("all")
            }
        })
    }
};

await client.Connectors.Create("orders-to-kafka", "Orders → Kafka", configuration);
```

The `name` parameter is optional. Convenience overloads cover the common cases:

```csharp
await client.Connectors.Create(connectorId);                       // empty settings, no name
await client.Connectors.Create(connectorId, settings);             // settings, no name
await client.Connectors.Create(connectorId, configuration);        // struct config, no name
```

| Exception | When |
|---|---|
| `ConnectorAlreadyExistsException` | A connector with the same id already exists. |
| `ArgumentException` | `connectorId` is unset or required parameters are null. |

## Reconfigure

`Reconfigure` replaces the connector's configuration. The connector keeps its id, name, and current state; only the settings or struct change.

```csharp
await client.Connectors.Reconfigure(
    connectorId: "orders-to-kafka",
    settings: new Dictionary<string, string?> {
        ["topic"] = "orders-v2"
    }
);
```

The `Struct` overload is symmetrical to `Create`:

```csharp
await client.Connectors.Reconfigure(connectorId, configuration);
```

| Exception | When |
|---|---|
| `ConnectorNotFoundException` | No connector exists with the given id. |
| `ConnectorDeletedException` | The connector exists but was deleted. |

## Start, stop, reset

The runtime state machine is `Stopped → Activating → Running → Deactivating → Stopped`.

```csharp
await client.Connectors.Start("orders-to-kafka");
await client.Connectors.Stop("orders-to-kafka");
```

Start can resume from a specific log position by passing `logPosition`:

```csharp
await client.Connectors.Start("orders-to-kafka", logPosition: 12_345);
```

`Reset` rewinds the connector's stored position. The connector must be stopped first.

```csharp
await client.Connectors.Stop("orders-to-kafka");
await client.Connectors.Reset("orders-to-kafka");                   // reset to start
await client.Connectors.Reset("orders-to-kafka", logPosition: 0);   // explicit position
await client.Connectors.Start("orders-to-kafka");
```

Both `Start` and `Reset` throw `ConnectorNotFoundException` for unknown ids and `ConnectorDeletedException` for deleted connectors.

## Rename

`Rename` updates the human-readable display name. The id is immutable.

```csharp
await client.Connectors.Rename("orders-to-kafka", "Orders → Kafka (prod)");
```

## Delete

```csharp
await client.Connectors.Delete("orders-to-kafka");
```

Delete is a soft delete. The connector record is kept with `DeleteTime` set, and `List` will exclude it unless you pass `ShowDeleted = true`. Subsequent `Reconfigure`, `Start`, `Stop`, `Reset`, or `Rename` calls on a deleted connector throw `ConnectorDeletedException`.
