# Kurrent.Surge.Mcp.Core

DuckDB-backed durable task-storage engine for **Model Context Protocol (MCP)** task stores.

This is the **version-neutral core**. It has no `ModelContextProtocol` dependency: it persists a
canonical task record — scalar columns plus opaque JSON payloads — to DuckDB and exposes primitive
`create` / `get` / `update` / `list` operations with TTL-based expiry. The MCP-facing adapters map
their respective `IMcpTaskStore` contracts onto this engine:

| Package                    | MCP SDK                        | Interface                                    |
|----------------------------|--------------------------------|----------------------------------------------|
| `Kurrent.Surge.Mcp`        | `ModelContextProtocol.Core` 2.0 | `ModelContextProtocol.Server.IMcpTaskStore`  |
| `Kurrent.Surge.Mcp.Legacy` | `ModelContextProtocol.Core` 1.4.1 | `ModelContextProtocol.IMcpTaskStore`       |

## Why a neutral core

The two MCP SDK versions ship **incompatible** task-store contracts (different types, methods, and
even different enum ordinals for `Failed`/`Cancelled`). A shared engine keeps the DuckDB schema and
persistence logic in one place; each adapter only translates types at its boundary. Task **status is
stored by name, never by ordinal**, so the two adapters can never misread each other's rows.

## Usage

```csharp
using Kurrent.Surge.Mcp;

using var store = new DuckDBTaskStore(
    "Data Source=tasks.ddb",
    new DuckDBTaskStoreOptions {
        DefaultTimeToLive  = TimeSpan.FromHours(1),
        MaxTasksPerSession = 1000
    });

var task = await store.CreateAsync(new CreateTaskInput { SessionId = "session-1" });
var page = await store.ListAsync(sessionId: "session-1");
```

DuckDB is a single-writer embedded database; the store serializes mutations in-process and provides
a background sweep for expired tasks. Point it at a file for durability — an in-memory database loses
all tasks on restart.

## Namespaces

- `Kurrent.Surge.Mcp` — the store (`DuckDBTaskStore`), options, the neutral `TaskRecord`/`McpTaskState`
  model, and the DuckDB connection plumbing.
