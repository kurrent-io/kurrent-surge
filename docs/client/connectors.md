# Connectors

<CopyMarkdownButton />

Connectors stream records from KurrentDB into external systems. The connectors module covers the full lifecycle: create, reconfigure, start, stop, reset, rename, delete, and inspect.

Commands throw on failure. See [Error handling](./error-handling.md) for the full exception catalogue.

## Create

A connector needs an id and a settings dictionary. Keys are camelCase, with colons grouping related options.

```csharp
var settings = new Dictionary<string, string> {
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

Available keys are documented per connector type. For example, see the [Kafka sink reference](https://docs.kurrent.io/server/v26.1/features/connectors/sinks/kafka.html).

::: tip
Omit the name to let the server use the connector id as the display name. Both forms exist as short extensions:

```csharp
await client.Connectors.Create("orders-to-kafka");           // empty settings, server names it after the id
await client.Connectors.Create("orders-to-kafka", settings); // settings, server names it after the id
```
:::

### From IConfiguration

Create accepts any `IConfiguration` (including an `IConfigurationSection`) and flattens it to settings using `:` as the key separator.

```csharp
var section = builder.Configuration.GetSection("Connector");

await client.Connectors.Create(
    connectorId: section["Id"]!,
    configuration: section.GetSection("Settings")
);
```

### From JSON

Pass a JSON string directly. Nested objects produce colon-separated keys, arrays use numeric indices.

```csharp
const string json = """
    {
        "instanceTypeName": "kafka-sink",
        "topic": "orders",
        "bootstrapServers": "kafka.internal:9092",
        "authentication": { "username": "app", "password": "secret" }
    }
    """;

await client.Connectors.Create(
    connectorId: "orders-to-kafka",
    json: json
);
```

## Reconfigure

Reconfigure replaces the connector's settings. The connector keeps its id, name, and current state.

```csharp
await client.Connectors.Reconfigure(
    connectorId: "orders-to-kafka",
    settings: new Dictionary<string, string> {
        ["topic"] = "orders-v2"
    }
);
```

Reconfigure accepts the same input shapes as Create: a dictionary, an `IConfiguration`, or a JSON string.

## Rename

Rename updates the human-readable display name. The id itself is immutable.

```csharp
await client.Connectors.Rename("orders-to-kafka", "Orders to Kafka (prod)");
```

## Start, stop, reset

A connector's runtime state moves through four phases. Start and Stop drive the transitions. Reset rewinds the stored position while the connector is stopped.

```csharp
await client.Connectors.Start("orders-to-kafka");
await client.Connectors.Stop("orders-to-kafka");
```

Start can resume from a specific log position:

```csharp
await client.Connectors.Start("orders-to-kafka", logPosition: 12_345);
```

Reset rewinds the connector's stored position. The connector must be stopped first.

```csharp
await client.Connectors.Stop("orders-to-kafka");
await client.Connectors.Reset("orders-to-kafka");
await client.Connectors.Start("orders-to-kafka");
```

Pass a log position to rewind to a specific offset:

```csharp
await client.Connectors.Reset("orders-to-kafka", logPosition: 0);
```

:::: warning
Reset fails if the connector is still running. Always stop it before resetting.
::::

## Delete

```csharp
await client.Connectors.Delete("orders-to-kafka");
```

Delete is a soft delete. The record is kept with `DeleteTime` set, and List excludes it unless you pass `ShowDeleted = true`.

## Exceptions

| Exception                         | Thrown by                                                                       | When                                               |
|-----------------------------------|---------------------------------------------------------------------------------|----------------------------------------------------|
| `ConnectorAlreadyExistsException` | `Create`                                                                        | A connector with this id already exists.           |
| `ConnectorNotFoundException`      | `Reconfigure`, `Rename`, `Start`, `Stop`, `Reset`, `Delete`, `GetSettings`      | No connector with this id exists.                  |
| `ConnectorDeletedException`       | `Reconfigure`, `Rename`, `Start`, `Stop`, `Reset`, `GetSettings`                | The connector was soft-deleted.                    |
| `ArgumentException`               | `Create`, `Rename`                                                              | `connectorId` is empty or `name` is null or blank. |

## List

List returns one page of connectors. Without options it returns the first page with default page size, excluding deleted connectors.

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

| Property            | Type                            | Description                                                  |
|---------------------|---------------------------------|--------------------------------------------------------------|
| `States`            | `IReadOnlyList<ConnectorState>` | Filter by state. Default: empty (matches all).               |
| `InstanceTypeNames` | `IReadOnlyList<string>`         | Filter by connector type, e.g. `kafka-sink`. Default: empty. |
| `ConnectorIds`      | `IReadOnlyList<string>`         | Filter by specific ids. Default: empty.                      |
| `IncludeSettings`   | `bool`                          | Populate `Settings` on each result. Default: `false`.        |
| `ShowDeleted`       | `bool`                          | Include soft-deleted connectors. Default: `false`.           |
| `Page`              | `int`                           | Page number (1-based). Default: `1`.                         |
| `PageSize`          | `int`                           | Items per page. Default: server setting.                     |

The result is a `ConnectorListResult`:

```csharp
public record ConnectorListResult {
    public IReadOnlyList<ConnectorDetails> Items     { get; }
    public int                             TotalSize { get; }
    public bool                            HasMorePages => Items.Count > 0 && TotalSize > Items.Count;
}
```

`HasMorePages` is convenience over comparing `TotalSize` to the running count of items you have fetched.

List does not throw on empty results. It returns an empty `Items` list with `TotalSize = 0`.

## Get settings

Fetch the settings for a single connector. Cheaper than calling List with `IncludeSettings`.

```csharp
var result = await client.Connectors.GetSettings("orders-to-kafka");

foreach (var (key, value) in result.Settings)
    Console.WriteLine("{0} = {1}", key, value);

Console.WriteLine("Updated at {0}", result.SettingsUpdateTime);
```

## Connector details

Each item returned by List is a `ConnectorDetails`:

| Property                              | Type                                  | Description                                                              |
|---------------------------------------|---------------------------------------|--------------------------------------------------------------------------|
| `ConnectorId`                         | `string`                              | Connector identifier.                                                    |
| `Name`                                | `string`                              | Display name.                                                            |
| `InstanceTypeName`                    | `string`                              | Connector implementation type, e.g. `kafka-sink`.                        |
| `State`                               | `ConnectorState`                      | Current state. See [Connector states](#connector-states).                |
| `StateUpdateTime`                     | `DateTimeOffset`                      | When the state last changed.                                             |
| `Position`                            | `LogPosition`                         | Last processed log position.                                             |
| `PositionUpdateTime`                  | `DateTimeOffset`                      | When the position last advanced.                                         |
| `Settings`                            | `IReadOnlyDictionary<string, string>` | Flat settings. Populated only when listed with `IncludeSettings = true`. |
| `SettingsUpdateTime`                  | `DateTimeOffset`                      | When the settings last changed.                                          |
| `CreateTime`                          | `DateTimeOffset`                      | When the connector was created.                                          |
| `UpdateTime`                          | `DateTimeOffset`                      | When the connector was last modified.                                    |
| `DeleteTime`                          | `DateTimeOffset`                      | Soft-delete timestamp. Equals `default(DateTimeOffset)` while active.    |
| `ErrorDetails`                        | `string?`                             | Last error message reported by the connector, or `null` if none.         |
| `IsRunning`, `IsStopped`, `IsDeleted` | `bool`                                | Shortcuts for the corresponding state and delete checks.                 |

## Connector states

| State          | Meaning                       |
|----------------|-------------------------------|
| `Unknown`      | State has not been reported.  |
| `Activating`   | Transitioning into `Running`. |
| `Running`      | Actively processing events.   |
| `Deactivating` | Transitioning into `Stopped`. |
| `Stopped`      | Idle.                         |
