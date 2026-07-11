# Kurrent.Surge.Mcp.Legacy

A **DuckDB-backed `IMcpTaskStore`** for the **stable** Model Context Protocol C# SDK
(`ModelContextProtocol.Core` **1.4.1**).

This is the richer, session-scoped 1.4.1 task-store contract: create-with-request, per-session
isolation, paginated listing, and separate result storage/retrieval. Built on the version-neutral
[`Kurrent.Surge.Mcp.Core`](../Kurrent.Surge.Mcp.Core) engine.

> Tracking the `ModelContextProtocol.Core` **2.0** preview contract instead? Use
> [`Kurrent.Surge.Mcp`](../Kurrent.Surge.Mcp).

## Usage

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddDuckDBMcpTaskStore(
    "Data Source=mcp-tasks.ddb",
    new DuckDBTaskStoreOptions {
        DefaultTimeToLive  = TimeSpan.FromHours(1),
        MaxTimeToLive      = TimeSpan.FromDays(1),
        MaxTasksPerSession = 1000
    });
```

Registers `ModelContextProtocol.IMcpTaskStore` as a **singleton**. Or construct it directly:

```csharp
using var store = new DuckDBMcpTaskStore("Data Source=mcp-tasks.ddb");
```

## Behaviour

Exception and session semantics mirror the reference in-memory store exactly:

- **Sessions** — a non-null `sessionId` scopes access; a null caller session bypasses isolation on
  every method **except** `GetTaskResultAsync`, which compares sessions unconditionally.
- **Result storage** — `StoreTaskResultAsync` accepts only `Completed`/`Failed` and refuses to
  overwrite a terminal task.
- **Cancellation** is idempotent — cancelling a terminal task returns it unchanged.
- **TTL** — a requested TTL is clamped to `MaxTimeToLive`; expired tasks drop out of listings and are
  swept in the background.
- **Listing** is keyset-paginated with an opaque cursor and filtered by session.

The task types are **experimental** in the MCP SDK (`MCPEXP001`).
