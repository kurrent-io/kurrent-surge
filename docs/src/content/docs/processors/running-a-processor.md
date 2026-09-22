---
title: Running a Processor
description: Run processors in the .NET host that you already use, and set their connection, options, identity, health check, and failure behaviour.
---

A processor runs inside the .NET host that you already use. `AddKurrentSurge` registers the
processor as a hosted service. It also registers the processor class in the container and adds a
health check for the processor. The host gives you configuration, logging, dependency injection,
and graceful shutdown. You write the processor class.

This page shows how to write the program, configure it, and read its health and its failures.

## Write the smallest program

This program runs `OrderProcessor`, the class from
[Writing a Processor](/kurrent-surge/processors/writing-a-processor/):

```csharp title="Program.cs"
using Kurrent.Surge;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.AddKurrentSurge(surge => surge.AddProcessor<OrderProcessor>());

await builder.Build().RunAsync();
```

`RunAsync` is an extension method in `Microsoft.Extensions.Hosting`, so the program must have that
`using`.

The program gets its connection from configuration. Add the connection string to
`appsettings.json`:

```json title="appsettings.json"
{
  "ConnectionStrings": {
    "KurrentDB": "kurrentdb://localhost:2113"
  }
}
```

Register an `IRecordProcessor` with `AddProcessor<T>`. Register an `IRecordBatchProcessor` with
`AddBatchProcessor<T>`. The two methods take the same arguments.

To set an option in code, give `AddProcessor` a delegate:

```csharp title="Program.cs"
using Kurrent.Surge;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.AddKurrentSurge(surge => surge.AddProcessor<OrderProcessor>(options =>
    options.Subscription.Filter = RecordFilter.ByStream("orders")));

await builder.Build().RunAsync();
```

## Build and run the application

A program goes through these steps:

1. Create the builder with `Host.CreateApplicationBuilder(args)`. For a web application, use
   `WebApplication.CreateBuilder(args)`.
2. Call `builder.AddKurrentSurge(surge => surge.AddProcessor<T>("processor-id", options => ...))`.
   The processor id and the delegate are optional.
3. Register the dependencies of the processor on `builder.Services`.
4. Call `builder.Build()`, then call `RunAsync()` on the host.

When the host starts, the scope reads the options from configuration, runs the delegate, validates
the result, and starts the processor.

The QuickStart passes the processor id `live-transfers`, sets its options in the delegate, and
registers a seeder:

```csharp title="Program.cs"
var builder = Host.CreateApplicationBuilder(args);

builder.AddKurrentSurge(surge => surge.AddProcessor<LiveTransferProcessor>("live-transfers", options => {
    options.Subscription.Filter          = RecordFilter.ByExpression("stream like 'player-%'");
    options.Subscription.InitialPosition = SubscriptionInitialPosition.Latest;

    // Tailing live data only, so there is nothing to resume after a restart — checkpoint
    // rarely rather than never, so the drone never wastes writes catching up to itself.
    options.Checkpoint.Interval = TimeSpan.FromDays(1);
}));

builder.Services.AddSerilog();
builder.Services.AddHostedService<TransferSeeder>();

await builder.Build().RunAsync();
```

Its processor is a `MessageProcessor` subclass. The constructor registers one handler for each
message type:

```csharp
sealed class LiveTransferProcessor : MessageProcessor {
    public LiveTransferProcessor() {
        Process<PlayerTransferred>((transfer, ctx) =>
            ctx.Logger.LogInformation(
                "Transfer: {PlayerName} from {FromClub} to {ToClub} for {Fee:C}",
                transfer.PlayerName, transfer.FromClubId.ToString(), transfer.ToClubId.ToString(), transfer.Fee.Amount));

        Process<PlayerRegistered>((registered, ctx) =>
            ctx.Logger.LogInformation(
                "Registration: {PlayerName} ({Nationality}) {Position}",
                registered.Name.ToString(), registered.Nationality.ToString(), registered.MainPosition.ToString()));
    }
}
```

**The whole program runs:**
[`examples/processors/Processor.QuickStart`](https://github.com/kurrent-io/kurrent-surge/tree/main/examples/processors/Processor.QuickStart)
seeds player transfers and logs the events that the processor receives.

## Configure the processor

The scope reads the options once, from the section `Kurrent:Surge:Processors:{id}`. `{id}` is the
processor id. Then the scope runs the delegate on the values that it read. A value that the delegate
sets replaces the configured value.

The QuickStart passes the id `live-transfers`, so it reads this section:

```json title="appsettings.json"
{
  "Kurrent": {
    "Surge": {
      "Processors": {
        "live-transfers": {
          "Batching": { "MaxBatchSize": 1000 },
          "Checkpoint": { "Interval": "00:00:10" }
        }
      }
    }
  }
}
```

Its delegate sets `Checkpoint.Interval` to `TimeSpan.FromDays(1)` and does not set
`Batching.MaxBatchSize`. The processor runs with these values:

| Option | Section | Delegate | Result |
|---|---|---|---|
| `Batching.MaxBatchSize` | `1000` | Not set | `1000` |
| `Checkpoint.Interval` | `00:00:10` | `TimeSpan.FromDays(1)` | One day |

The section follows these rules:

- When the scope compares the section name with the id, it ignores case, `-`, and `_`.
  `live-transfers`, `live_transfers`, and `LiveTransfers` reach the same section.
- The scope reads no other section for the processor.
- The scope validates the options. If a value is incorrect, the host does not start.

To change an option with no rebuild, add its key to the section.

### Keys that have no effect

Configuration cannot set the processor id. The scope logs a `ProcessorId` key in the section as an
unrecognised setting. The processor id does not change.

| Key in the section | Warning that the scope logs |
|---|---|
| A key with a spelling error | `Unrecognised setting. Check the spelling — it had no effect.` |
| `ProcessorId` | `Unrecognised setting. Check the spelling — it had no effect.` |
| `Observers`, `MeterFactory`, or `Time` | `This setting is not configurable and had no effect. Set it in code.` |

`DroneId`, the id of this instance, can come from the section.

## Configure the application

The application reads the configuration sources of its host builder. For
`Host.CreateApplicationBuilder` and `WebApplication.CreateBuilder`, these sources include:

- `appsettings.json`
- Environment variables
- The command line, from `args`

### Set the connection

The application uses the first source in this table that has a value.

| # | Source |
|---|---|
| 1 | `options.Connection(...)` in the delegate |
| 2 | `ConnectionString` or `Connection` in `Kurrent:Surge:Processors:{ProcessorId}` |
| 3 | `surge.Connection(...)` in `builder.AddKurrentSurge(...)` |
| 4 | `ConnectionStrings:Kurrent:Surge` |
| 5 | `ConnectionStrings:KurrentDB` |

The scope resolves sources 2 to 5 before it runs the delegate, so the delegate receives the
connection the processor will use. A connection that the delegate sets replaces it.

When the application compares keys, it ignores case, `-`, and `_`. `order-processor` and
`OrderProcessor` find the same entry.

If no source has a value, the processor fails validation and the host does not start.

To configure the client, use `options.Connection(connectionString, client => ...)`. To attach a
catalog that no assembly attribute activates, call `client.Messages.UseCatalog(...)` in that
delegate. See [Catalogs](/kurrent-surge/schema/catalogs/).

To change the configured connection at the application level, call `surge.Connection(client => ...)`
in the same `AddKurrentSurge` call. This example attaches a catalog at that level:

```csharp title="Program.cs"
var builder = Host.CreateApplicationBuilder(args);

builder.AddKurrentSurge(surge => {
    surge.Connection(client => client.Messages.UseCatalog(catalog));
    surge.AddProcessor<OrderProcessor>();
});

await builder.Build().RunAsync();
```

The processor and each service that gets `KurrentClient` from the container, for example a seeder,
use the same connection options. The table shows what each level changes:

| Level | Call | Reaches |
|---|---|---|
| Processor | `options.Connection(...)` | The processor only |
| Application | `builder.AddKurrentSurge(surge => surge.Connection(...))` | Each processor and each service that gets `KurrentClient` from the container |

If the processor's own section sets `ConnectionString` or `Connection`, the processor uses that
value and not the application connection.

## Processor identity

The processor id is the string that you pass to `AddProcessor`. If you do not pass one, the scope
uses the type name in kebab-case: `LiveTransferProcessor` gives `live-transfer-processor`.

The runtime uses the processor id for these items:

- The configuration section
- The checkpoints
- The name of the health check
- The key of the processor class and of `IDrone` in the container
- The `processor_id` tag on the metrics
- The start of the drone id, which is the id of this instance

The checkpoints use the processor id as their key. If the processor id changes, the processor starts
again from its configured initial position. If you pass no id and rename the class, the processor id
changes. To keep the same processor id, pass it:

```csharp
builder.AddKurrentSurge(surge => surge.AddProcessor<OrderProcessor>("orders"));
```

Set the id at registration. If the delegate sets `ProcessorId`, the scope reads no section for the
new id. To choose the drone id, set `DroneId`.

## Register the processor and its dependencies

`AddProcessor<T>` registers `T` in the container as a singleton, with the processor id as its key.
The container creates `T` and resolves its constructor dependencies. You do not register `T`.

If you register `T` yourself, with the processor id as the key, your registration wins. Use this to
create the processor in your own code:

```csharp
builder.Services.AddKeyedSingleton<OrderProcessor>("orders", (sp, _) =>
    new OrderProcessor(sp.GetRequiredService<IOrderStore>()));

builder.AddKurrentSurge(surge => surge.AddProcessor<OrderProcessor>("orders"));
```

The scope registers `T` and nothing else. Register the dependencies of `T` on `builder.Services`:

```csharp title="Program.cs"
using Kurrent.Surge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDataSource("orders-db");
builder.Services.AddSingleton<IOrderStore, SqlOrderStore>();

builder.AddKurrentSurge(surge => surge.AddProcessor<OrderProcessor>());

await builder.Build().RunAsync();
```

Extension methods for `IHostApplicationBuilder` work on the builder as on any host builder. Examples
are the Aspire service defaults and Aspire client integrations such as `AddNpgsqlDataSource`.
Extension methods for `IServiceCollection`, such as Serilog's `AddSerilog`, work on
`builder.Services`.

To seed data, register a hosted service. The QuickStart registers `TransferSeeder` with
`AddHostedService<TransferSeeder>()`, and the host runs it at the same time as the processor.

### Resolve the processor

To use the processor in other code, resolve it with the processor id as the key:

```csharp
var processor = host.Services.GetRequiredKeyedService<OrderProcessor>("orders");
```

In a minimal API, use `[FromKeyedServices("orders")]` on the parameter.

While a type runs under one id only, `GetRequiredService<OrderProcessor>()` also resolves it. When you
register a second id for the same type, the scope removes the registration with no key. Then use the
key.

If you call `builder.Services.AddSingleton<OrderProcessor>()`, the container holds a different
instance. The processor runtime does not use that instance.

After `Build()`, resolve these services from `host.Services`:

| Service | Key | Contains |
|---|---|---|
| The processor class `T` | The processor id | The processor instance, a singleton |
| `IDrone` | The processor id | `ProcessorId`, `Status`, and `Options`. `Status` is the `DroneStatus` of the processor. `Options` is the `ProcessorOptions` that the processor runs with |
| `HealthCheckService` | None | The health report. Each processor entry has the processor id as its key |

## Run several processors

Call `AddProcessor` or `AddBatchProcessor` one time for each processor in the `AddKurrentSurge`
delegate. The scope registers one hosted service for each processor id. The host runs all the
processors at the same time.

```csharp
builder.AddKurrentSurge(surge => {
    surge.AddProcessor<OrderProcessor>();
    surge.AddBatchProcessor<BulkIndexer>();
});
```

The same type can run under different ids, for example one processor for each tenant. The delegate
that takes an `IServiceProvider` gets services for the options:

```csharp
builder.AddKurrentSurge(surge => {
    foreach (var tenant in tenants)
        surge.AddProcessor<OrderProcessor>($"orders-{tenant}", (options, sp) => {
            options.Subscription.Filter = RecordFilter.ByStream($"orders-{tenant}");
            options.Checkpoint.Interval = sp.GetRequiredService<TenantPolicy>().CheckpointInterval(tenant);
        });
});
```

Each id has its own section, its own checkpoints, and its own health check.

If two registrations use the same id, the scope throws `InvalidOperationException`. The message
names the id.

### Run processors in a web application

In a web application, call `AddKurrentSurge` on `WebApplication.CreateBuilder`. The processor runs
beside the HTTP endpoints. Surge adds no HTTP endpoint.

```csharp title="Program.cs"
var builder = WebApplication.CreateBuilder(args);

builder.AddKurrentSurge(surge => surge.AddProcessor<OrderStatsProcessor>("orders"));

var app = builder.Build();

app.MapGet("/stats", ([FromKeyedServices("orders")] OrderStatsProcessor p) => p.Snapshot());

await app.RunAsync();
```

`Snapshot()` is a method of the application's processor class.
[`Processor.AspNetMinimal`](https://github.com/kurrent-io/kurrent-surge/tree/main/examples/processors/Processor.AspNetMinimal)
hosts a processor in an ASP.NET Core application.

## Read the health check

`AddProcessor` registers one health check for each processor. The name of the health check is the
processor id. Its tags are `kurrent`, `surge`, and `processor`. `HealthCheckService` combines the
checks, and the worst status wins.

The table shows the health for each processor status:

| Processor status | Health |
|---|---|
| Not started | Degraded |
| `Starting` | Degraded |
| `Processing` | Healthy |
| `Processing`, and the last checkpoint failed | Degraded, with the exception |
| `Stopping` or `Stopped` | Unhealthy |
| Failed, at start-up or while processing | Unhealthy, with the exception |

A failed checkpoint keeps the check Degraded until the next checkpoint commits.

To read the health check, get `IDrone` with the processor id as the key, and `HealthCheckService`,
from the host services:

```csharp
using Kurrent.Surge.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var host = builder.Build();

var drone  = host.Services.GetRequiredKeyedService<IDrone>("orders");
var health = host.Services.GetRequiredService<HealthCheckService>();
var report = await health.CheckHealthAsync();

var processor = report.Entries[drone.ProcessorId];
```

Surge adds no HTTP endpoint. To send the report to a different system, register an
`IHealthCheckPublisher` as a singleton. By default, the host runs each publisher at 30-second
intervals.

In a web application, `app.MapHealthChecks("/health")` exposes the checks over HTTP. A liveness
probe on that endpoint lets the orchestrator restart the container when a processor is Unhealthy.
The probe needs nothing from Surge.

## Failure and exit code

`RunAsync` runs the host until Ctrl+C, SIGTERM, or its `cancellationToken` stops it. Then it
disposes the host.

If a processor fails, the host logs the exception at the `Error` level and stops. On .NET 10,
`RunAsync` then returns without an exception, and the process exits with code 0. The SDK never sets
the exit code of the process.

To make the process fail, call `FailProcessOnFatalError()` in `AddKurrentSurge`:

```csharp title="Program.cs"
builder.AddKurrentSurge(surge => {
    surge.FailProcessOnFatalError();
    surge.AddProcessor<OrderProcessor>();
});
```

With this option, `RunAsync` throws the exception of the processor after the host stops. The process
then exits with a failure code, and a container orchestrator can start it again. The processor
continues at or before the record that failed.

On .NET 11, `RunAsync` throws the exception with no option. There, `FailProcessOnFatalError()` adds
nothing.

A stop by Ctrl+C or SIGTERM is not a failure. `RunAsync` returns, and the process exits with code 0,
with or without the option.

These items show a failure:

- The host log, which has an `Error` entry with the exception
- The health check, which reports Unhealthy with the exception
- With `FailProcessOnFatalError()`, the exception that `RunAsync` throws and the failure exit code of
  the process

A liveness probe on the health endpoint is the other way to get a restart. See
[Read the health check](#read-the-health-check).

[Error Handling](/kurrent-surge/processors/error-handling/) covers what counts as a failure and
where the position stops.

[Wiring Up Surge](/kurrent-surge/client/dependency-injection/) covers the full `AddKurrentSurge`
scope.
