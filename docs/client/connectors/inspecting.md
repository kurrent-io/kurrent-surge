---
order: 2
---

# Inspecting connectors

Inspection covers listing connectors, reading their configuration, and watching their state. The query API is paginated and filterable.

```csharp
using Kurrent.Client.Connectors;

var page = await client.Connectors.List();

foreach (var connector in page.Items)
    Console.WriteLine("{0,-20} {1,-12} {2}", connector.ConnectorId, connector.State, connector.Name);
```

## List

`List` returns one page of connectors. Without options it returns the first page with default page size, excluding deleted connectors.

```csharp
var page = await client.Connectors.List(new ConnectorListOptions {
    States               = [ConnectorState.Running],
    InstanceTypeNames    = ["kafka-sink", "kafka-source"],
    IncludeSettings      = true,
    IncludeConfiguration = false,
    Page                 = 1,
    PageSize             = 50
});

Console.WriteLine("Showing {0} of {1}", page.Items.Count, page.TotalSize);

if (page.HasMorePages)
    Console.WriteLine("More pages available");
```

### Options

| Property | Type | Default | Description |
|---|---|---|---|
| `States` | `IReadOnlyList<ConnectorState>` | empty | Filter by operational state. Empty matches all. |
| `InstanceTypeNames` | `IReadOnlyList<string>` | empty | Filter by connector type (e.g. `kafka-sink`). |
| `ConnectorIds` | `IReadOnlyList<string>` | empty | Filter by specific ids. |
| `IncludeSettings` | `bool` | `false` | Populate `ConnectorDetails.Settings`. |
| `IncludeConfiguration` | `bool` | `false` | Populate `ConnectorDetails.Configuration`. |
| `ShowDeleted` | `bool` | `false` | Include soft-deleted connectors. |
| `Page` | `int` | `1` | 1-based page number. |
| `PageSize` | `int` | server default | Items per page. |

### Result shape

```csharp
public record ConnectorListResult {
    public IReadOnlyList<ConnectorDetails> Items     { get; }
    public int                             TotalSize { get; }
    public bool                            HasMorePages => Items.Count > 0 && TotalSize > Items.Count;
}
```

`HasMorePages` is convenience over comparing `TotalSize` to the running count of items you have fetched.

## Connector details

Each item is a `ConnectorDetails`:

| Property | Type | Description |
|---|---|---|
| `ConnectorId` | `string` | Connector identifier. |
| `Name` | `string` | Display name. |
| `InstanceTypeName` | `string` | The connector implementation type. |
| `State` | `ConnectorState` | Current state. |
| `StateUpdateTime` | `DateTimeOffset` | When the state last changed. |
| `Position` | `LogPosition` | Last processed log position. |
| `PositionUpdateTime` | `DateTimeOffset` | When the position last advanced. |
| `Settings` | `IReadOnlyDictionary<string, string>` | Flat settings (only populated when `IncludeSettings`). |
| `SettingsUpdateTime` | `DateTimeOffset` | When the settings last changed. |
| `Configuration` | `Struct?` | Hierarchical config (only populated when `IncludeConfiguration`). |
| `ConfigurationUpdateTime` | `DateTimeOffset` | When the configuration last changed. |
| `CreateTime` | `DateTimeOffset` | When the connector was created. |
| `UpdateTime` | `DateTimeOffset` | When the connector was last modified. |
| `DeleteTime` | `DateTimeOffset` | When the connector was deleted (`default` if not deleted). |
| `ErrorDetails` | `string?` | Last error message, if any. |
| `IsRunning` | `bool` | Shortcut for `State == Running`. |
| `IsStopped` | `bool` | Shortcut for `State == Stopped`. |
| `IsDeleted` | `bool` | Shortcut for `DeleteTime != default`. |

## Connector states

| State | Meaning |
|---|---|
| `Unknown` | State has not been reported. |
| `Activating` | Transitioning into `Running`. |
| `Running` | Actively processing events. |
| `Deactivating` | Transitioning into `Stopped`. |
| `Stopped` | Idle. |

## Get settings

Fetch only the flat settings for a single connector. Cheaper than calling `List` with `IncludeSettings`.

```csharp
var result = await client.Connectors.GetSettings("orders-to-kafka");

foreach (var (key, value) in result.Settings)
    Console.WriteLine("{0} = {1}", key, value);

Console.WriteLine("Updated at {0}", result.SettingsUpdateTime);
```

## Get configuration

Fetch the hierarchical configuration as a `Struct`:

```csharp
var result = await client.Connectors.GetConfiguration("orders-to-kafka");

var topic = result.Configuration.Fields["topic"].StringValue;
Console.WriteLine("Topic: {0}", topic);
```

## Errors

`GetSettings` and `GetConfiguration` throw `ConnectorNotFoundException` for unknown ids and `ConnectorDeletedException` for soft-deleted connectors. `List` does not throw on empty results — it returns an empty `Items` list with `TotalSize = 0`.
