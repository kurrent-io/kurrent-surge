# Kurrent .NET SDK Examples

Each example is a runnable app demonstrating a slice of the SDK.

## Prerequisites

Every example connects to a local KurrentDB with `kurrentdb://admin:changeit@localhost:2113?tls=false`.
Start one with:

```bash
docker run -d --name kurrentdb -p 2113:2113 \
  -e KURRENTDB_INSECURE=true -e KURRENTDB_MEM_DB=true \
  -e KURRENTDB_RUN_PROJECTIONS=All -e KURRENTDB_START_STANDARD_PROJECTIONS=true \
  docker.kurrent.io/kurrent-latest/kurrentdb:latest
```

The processor examples seed their own input through a hosted service, so `dotnet run` alone produces
output. Examples that need Redis or PostgreSQL say so in their README. `Connector.Management` uses the
Kafka sink connector, which requires a licensed KurrentDB.

## Wireup

Each example makes a host — `Host.CreateApplicationBuilder(args)` for the console examples,
`WebApplication.CreateBuilder(args)` for the web ones — and registers Surge with
`builder.AddKurrentSurge(surge => { ... })`.

- **One processor in each host.** The console processor examples call `surge.AddProcessor<T>(...)`
  once inside `AddKurrentSurge` and set the processor options in the lambda. `AddKurrentSurge` adds
  the `KurrentClient` to the container, and `AddProcessor<T>` adds the processor and its health check.

- **Many components in one host.** The web examples use `WebApplication.CreateBuilder` and
  `AddKurrentSurge`. `Streams.QuickStart` and `Registry.Quickstart` use `AddKurrentSurge` with
  `Host.CreateApplicationBuilder`.

`Processor.QuickStart` shows the minimum code for the first mode. It has one file. The processor
class is below the top-level statements.

`Processor.WordCount` adds a message catalog to the `KurrentClient` connection. The processor and
`FactSeeder` use that connection. To tune the configured connection, `Processor.WordCount` calls
`surge.Connection(...)` in the same `AddKurrentSurge` scope as `AddProcessor<T>`:

```csharp
surge.Connection(client => client.Messages.UseCatalog(catalog));
```

A console processor example has this structure:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.AddKurrentSurge(surge => surge.AddProcessor<OrderProcessor>(options =>
    options.Subscription.Filter = RecordFilter.ByExpression("schema_name = 'OrderPlaced'")));

builder.Services.AddHostedService<OrderSeeder>();

await builder.Build().RunAsync();
```

To give the processor an explicit id, pass the id as the first argument:
`surge.AddProcessor<OrderProcessor>("orders", options => ...)`. `Processor.QuickStart` does this.

The examples do not contain a connection string in code. Each example has an `appsettings.json` file
with the connection string at `ConnectionStrings:kurrentdb`.

`AddKurrentSurge` reads that key without connection code. It looks for two key names in this
sequence: `Kurrent:Surge`, then `KurrentDB`. For `KurrentDB` it ignores the difference between
uppercase and lowercase letters, and the `-` and `_` characters.

Examples that use `AddKurrentSurge` give the key name:

```csharp
builder.AddKurrentSurge("kurrentdb", surge => { ... });
```

To use a different server, change the `appsettings.json` file of the example. You can also set the
`ConnectionStrings__kurrentdb` environment variable.

## Message mapping

Message types are mapped to wire-level schema names through a message catalog. These examples use
one recommended approach consistently: a hand-authored catalog with **pinned names**.

```csharp
[CustomMessageCatalog]
sealed class OrdersMessageCatalog() : MessageCatalog(
    "orders", alwaysActive: true, catalog => catalog
        .Map<OrderPlaced>("OrderPlaced")
        .Map<OrderShipped>("OrderShipped"));
```

Pinned names keep the wire contract stable and decoupled from your .NET namespaces and type names,
so refactoring code never changes a schema name. `Registry.OrderManagement` shows the same approach
with explicit versions (`order.placed.v1`).

Two examples show alternatives the SDK also supports:

- `Streams.QuickStart`: zero-config namespace scan,
  `[assembly: MessageCatalog("Ns.Events", AlwaysActive = true)]`, which derives
  fully-qualified names with no per-type mapping.
- `Processor.WordCount`: a runtime `MessageCatalog` selected per client via `UseCatalog(...)`.

The SDK additionally supports inline `opts.Messages.MapMessage<T>("name")` and custom naming
strategies (for example a short prefix via `NamespaceSchemaNameStrategy`); see the SDK documentation.

Each example's subscription filter and any registry schema name use the same pinned names the
catalog declares.
