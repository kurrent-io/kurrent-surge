# Connectors

<CopyMarkdownButton />

Connectors stream records from KurrentDB into external systems. The `client.Connectors` module covers the full lifecycle: create, reconfigure, start, stop, reset, rename, delete, and inspect.

:::: tip Connector identifiers
`ConnectorId` is a typed wrapper around a non-empty string. It accepts implicit conversion from `string`, so either form is valid wherever an id is expected.

```csharp
ConnectorId id = "orders-to-kafka";

await client.Connectors.Create(id);
await client.Connectors.Create("orders-to-kafka");
```
::::

All command methods return `Success` on completion and throw on failure. See [Error handling](./error-handling.md) for the full exception catalogue.

## Create

A connector needs an id and a settings dictionary. Keys are camelCase, with colons grouping related options.

```csharp
var settings = new Dictionary<string, string?> {
    ["instanceTypeName"]        = "kafka-sink",
    ["topic"]                   = "orders",
    ["bootstrapServers"]        = "kafka.internal:9092",
    ["authentication:username"] = "app",
    ["authentication:password"] = "secret",
    ["compression:type"]        = "gzip"
};

await client.Connectors.Create(
    connectorId: "orders-to-kafka",
    name: "Orders to Kafka",
    settings: settings
);
```

Null values are stored as empty strings. Available keys are documented per connector type. For example, see the [Kafka sink reference](https://docs.kurrent.io/server/v26.1/features/connectors/sinks/kafka.html).

The `name` parameter is optional. Convenience overloads cover the common cases:

```csharp
await client.Connectors.Create("orders-to-kafka");              // empty settings, no name
await client.Connectors.Create("orders-to-kafka", settings);    // settings, no name
```

A `Google.Protobuf.WellKnownTypes.Struct` overload is also available for round-tripping a configuration returned by `GetConfiguration`.

## Reconfigure

`Reconfigure` replaces the connector's settings. The connector keeps its id, name, and current state.

```csharp
await client.Connectors.Reconfigure(
    connectorId: "orders-to-kafka",
    settings: new Dictionary<string, string?> {
        ["topic"] = "orders-v2"
    }
);
```

A `Struct` overload mirrors the one on `Create`.

## Rename

`Rename` updates the human-readable display name. The id itself is immutable.

```csharp
await client.Connectors.Rename("orders-to-kafka", "Orders to Kafka (prod)");
```

## Start, stop, reset

A connector's runtime state moves through four phases. `Start` and `Stop` drive the transitions. `Reset` rewinds the stored position while the connector is stopped.

```csharp
await client.Connectors.Start("orders-to-kafka");
await client.Connectors.Stop("orders-to-kafka");
```

`Start` can resume from a specific log position:

```csharp
await client.Connectors.Start("orders-to-kafka", logPosition: 12_345);
```

`Reset` rewinds the connector's stored position. The connector must be stopped first.

```csharp
await client.Connectors.Stop("orders-to-kafka");
await client.Connectors.Reset("orders-to-kafka");
await client.Connectors.Start("orders-to-kafka");
```

Pass `logPosition` to rewind to a specific offset:

```csharp
await client.Connectors.Reset("orders-to-kafka", logPosition: 0);
```

:::: warning
`Reset` fails if the connector is still running. Always `Stop` before resetting.
::::

## Delete

```csharp
await client.Connectors.Delete("orders-to-kafka");
```

Delete is a soft delete. The record is kept with `DeleteTime` set, and `List` excludes it unless you pass `ShowDeleted = true`.

## Exceptions

| Exception                         | Thrown by                                                                                      | When                                                  |
|-----------------------------------|------------------------------------------------------------------------------------------------|-------------------------------------------------------|
| `ConnectorAlreadyExistsException` | `Create`                                                                                       | A connector with this id already exists.              |
| `ConnectorNotFoundException`      | `Reconfigure`, `Rename`, `Start`, `Stop`, `Reset`, `Delete`, `GetSettings`, `GetConfiguration` | No connector with this id exists.                     |
| `ConnectorDeletedException`       | `Reconfigure`, `Rename`, `Start`, `Stop`, `Reset`, `GetSettings`, `GetConfiguration`           | The connector was soft-deleted.                       |
| `ArgumentException`               | `Create`                                                                                       | `connectorId` is empty or a required argument is null. |

## List

`List` returns one page of connectors. Without options it returns the first page with default page size, excluding deleted connectors.

```csharp
var page = await client.Connectors.List();

foreach (var connector in page.Items)
    Console.WriteLine("{0,-20} {1,-12} {2}", connector.ConnectorId, connector.State, connector.Name);
```

Filter and paginate via `ConnectorListOptions`:

```csharp
var page = await client.Connectors.List(new ConnectorListOptions {
    States            = [ConnectorState.Running],
    InstanceTypeNames = ["kafka-sink", "kafka-source"],
    IncludeSettings   = true,
    PageSize          = 50
});

Console.WriteLine("Showing {0} of {1}", page.Items.Count, page.TotalSize);

if (page.HasMorePages)
    Console.WriteLine("More pages available");
```

| Property               | Type                            | Description                                                          |
|------------------------|---------------------------------|----------------------------------------------------------------------|
| `States`               | `IReadOnlyList<ConnectorState>` | Filter by state. Default: empty (matches all).                       |
| `InstanceTypeNames`    | `IReadOnlyList<string>`         | Filter by connector type, e.g. `kafka-sink`. Default: empty.         |
| `ConnectorIds`         | `IReadOnlyList<string>`         | Filter by specific ids. Default: empty.                              |
| `IncludeSettings`      | `bool`                          | Populate `Settings` on each result. Default: `false`.                |
| `IncludeConfiguration` | `bool`                          | Populate `Configuration` on each result. Default: `false`.           |
| `ShowDeleted`          | `bool`                          | Include soft-deleted connectors. Default: `false`.                   |
| `Page`                 | `int`                           | Page number (1-based). Default: `1`.                                 |
| `PageSize`             | `int`                           | Items per page. Default: server setting.                             |

The result is a `ConnectorListResult`:

```csharp
public record ConnectorListResult {
    public IReadOnlyList<ConnectorDetails> Items     { get; }
    public int                             TotalSize { get; }
    public bool                            HasMorePages => Items.Count > 0 && TotalSize > Items.Count;
}
```

`HasMorePages` is convenience over comparing `TotalSize` to the running count of items you have fetched.

`List` does not throw on empty results. It returns an empty `Items` list with `TotalSize = 0`.

## Get settings

Fetch only the flat settings for a single connector. Cheaper than calling `List` with `IncludeSettings`.

```csharp
var result = await client.Connectors.GetSettings("orders-to-kafka");

foreach (var (key, value) in result.Settings)
    Console.WriteLine("{0} = {1}", key, value);

Console.WriteLine("Updated at {0}", result.SettingsUpdateTime);
```

## Get configuration

Fetch the configuration as a `Struct`. Useful when you want to round-trip the value back into `Reconfigure`.

```csharp
var result = await client.Connectors.GetConfiguration("orders-to-kafka");

var topic = result.Configuration.Fields["topic"].StringValue;
Console.WriteLine("Topic: {0}", topic);
```

## Connector details

Each item returned by `List` is a `ConnectorDetails`:

| Property                              | Type                                  | Description                                                                |
|---------------------------------------|---------------------------------------|----------------------------------------------------------------------------|
| `ConnectorId`                         | `string`                              | Connector identifier.                                                      |
| `Name`                                | `string`                              | Display name.                                                              |
| `InstanceTypeName`                    | `string`                              | Connector implementation type, e.g. `kafka-sink`.                          |
| `State`                               | `ConnectorState`                      | Current state. See [Connector states](#connector-states).                  |
| `StateUpdateTime`                     | `DateTimeOffset`                      | When the state last changed.                                               |
| `Position`                            | `LogPosition`                         | Last processed log position.                                               |
| `PositionUpdateTime`                  | `DateTimeOffset`                      | When the position last advanced.                                           |
| `Settings`                            | `IReadOnlyDictionary<string, string>` | Flat settings. Populated only when listed with `IncludeSettings = true`.   |
| `SettingsUpdateTime`                  | `DateTimeOffset`                      | When the settings last changed.                                            |
| `Configuration`                       | `Struct?`                             | Configuration as a `Struct`. Populated only when listed with `IncludeConfiguration = true`. |
| `ConfigurationUpdateTime`             | `DateTimeOffset`                      | When the configuration last changed.                                       |
| `CreateTime`                          | `DateTimeOffset`                      | When the connector was created.                                            |
| `UpdateTime`                          | `DateTimeOffset`                      | When the connector was last modified.                                      |
| `DeleteTime`                          | `DateTimeOffset`                      | Soft-delete timestamp. Equals `default(DateTimeOffset)` while active.      |
| `ErrorDetails`                        | `string?`                             | Last error message reported by the connector, or `null` if none.           |
| `IsRunning`, `IsStopped`, `IsDeleted` | `bool`                                | Shortcuts for the corresponding state and delete checks.                   |

## Connector states

| State          | Meaning                          |
|----------------|----------------------------------|
| `Unknown`      | State has not been reported.     |
| `Activating`   | Transitioning into `Running`.    |
| `Running`      | Actively processing events.      |
| `Deactivating` | Transitioning into `Stopped`.    |
| `Stopped`      | Idle.                            |
