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

Every example builds a host and registers the SDK through `AddKurrentSurge`. Web examples use
`WebApplication.CreateBuilder`; console examples use `Host.CreateApplicationBuilder`, whether they
run indefinitely or complete a task and exit.

The connection string is never hard-coded. Each example ships an `appsettings.json` holding it under
`ConnectionStrings:kurrentdb`, and reads it with the connection-name overload:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.AddKurrentSurge("kurrentdb", surge => {
    surge.AddProcessor<OrderProcessor>(opts =>
        opts.Subscription.Filter = RecordFilter.ByExpression("schema_name = 'OrderPlaced'"));
});
```

Point an example at a different server by editing its `appsettings.json`, or by setting the
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
