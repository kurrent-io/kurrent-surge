# Kurrent.Surge.Mcp

A self-contained **DuckDB-backed `IMcpTaskStore`** for the Model Context Protocol C# SDK
(`ModelContextProtocol.Core` 2.0).

Gives an MCP server durable, restart-surviving task storage: create a task, run the work in the
background, store the result — and have `tasks/get` polls resolve even after a process restart.
Everything ships in this one package; there is no separate engine dependency.

## Usage

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddDuckDBMcpTaskStore(
    "Data Source=mcp-tasks.ddb",
    new DuckDBTaskStoreOptions { DefaultTimeToLive = TimeSpan.FromHours(1) });
```

This registers `ModelContextProtocol.Server.IMcpTaskStore` as a **singleton** — required so task
polls on later requests observe tasks created on earlier ones. Or construct it directly:

```csharp
using var store = new DuckDBMcpTaskStore("Data Source=mcp-tasks.ddb");
var task = await store.CreateTaskAsync();
```

## Notes

- The task types are **experimental** in the MCP SDK (`MCPEXP001`); this package tracks the
  `2.0.0-preview` line and may change with it.
- `InputResponseReceived` is an **in-process** event. A multi-instance deployment needs external
  coordination to notify the originating instance of an input response.
- Serialization of results, errors, and input requests uses the SDK's `McpJsonUtilities.DefaultOptions`.
