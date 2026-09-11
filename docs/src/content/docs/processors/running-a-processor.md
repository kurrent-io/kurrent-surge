---
title: Running a Processor
description: Run one processor as a .NET host application, and set its connection, options, identity, health check, and exit code.
---

`SurgeProcessor.CreateBuilder<T>` creates a builder for a .NET host application that runs one
processor. The host gives you configuration, logging, dependency injection, and graceful shutdown.
You write the processor class. This page shows how to write the program, configure it, and read its
health and exit code.

## Write the smallest program

This program runs `OrderProcessor`, the class from
[Writing a Processor](/kurrent-surge/processors/writing-a-processor/):

```csharp title="Program.cs"
using Kurrent.Surge;
using Microsoft.Extensions.Hosting;

var builder = SurgeProcessor.CreateBuilder<OrderProcessor>(args);

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

The class can implement `IRecordProcessor` or `IRecordBatchProcessor`. The runtime selects the
record path or the batch path from the type.

To set an option in code, give `CreateBuilder` a delegate:

```csharp title="Program.cs"
using Kurrent.Surge;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.Hosting;

var builder = SurgeProcessor.CreateBuilder<OrderProcessor>(args, options =>
    options.Subscription.Filter = RecordFilter.ByStream("orders"));

await builder.Build().RunAsync();
```

## Build and run the application

A program goes through these steps:

1. Create the builder with `SurgeProcessor.CreateBuilder<T>(args, "processor-id", options => ...)`.
   The processor id and the delegate are optional.
2. Register services on `builder.Services`.
3. Call `builder.Build()`. It reads the options from configuration, runs the delegate, validates the
   result, and returns an `IHost`.
4. Call `RunAsync()` on the host.

`args` is always the first argument. To ignore the command line, pass `null` as `args`:
`CreateBuilder<T>(null, options => ...)`.

The builder is an `IHostApplicationBuilder`. Its `Services`, `Configuration`, `Environment`,
`Logging`, and `Metrics` properties work as they do on `Host.CreateApplicationBuilder`.

The QuickStart passes the processor id `live-transfers`, sets its options in the delegate, and
registers a seeder:

```csharp
var builder = SurgeProcessor.CreateBuilder<LiveTransferProcessor>(
    args, "live-transfers", options => {
        options.Subscription.Filter          = RecordFilter.ByExpression("stream like 'player-%'");
        options.Subscription.InitialPosition = SubscriptionInitialPosition.Latest;

        // Tailing live data only, so there is nothing to resume after a restart — checkpoint
        // rarely rather than never, so the drone never wastes writes catching up to itself.
        options.Checkpoint.Interval = TimeSpan.FromDays(1);
    });

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

`Build()` reads the options once, from the section `Kurrent:Surge:Processors:{id}`. `{id}` is the
processor id. Then `Build()` runs the delegate on the values that it read. A value that the delegate
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

Its delegate sets the checkpoint interval:

```csharp
var builder = SurgeProcessor.CreateBuilder<LiveTransferProcessor>(
    args, "live-transfers", options => {
        options.Subscription.Filter          = RecordFilter.ByExpression("stream like 'player-%'");
        options.Subscription.InitialPosition = SubscriptionInitialPosition.Latest;

        // Tailing live data only, so there is nothing to resume after a restart — checkpoint
        // rarely rather than never, so the drone never wastes writes catching up to itself.
        options.Checkpoint.Interval = TimeSpan.FromDays(1);
    });
```

The processor runs with these values:

| Option | Section | Delegate | Result |
|---|---|---|---|
| `Batching.MaxBatchSize` | `1000` | Not set | `1000` |
| `Checkpoint.Interval` | `00:00:10` | `TimeSpan.FromDays(1)` | One day |

The section follows these rules:

- When `Build()` compares the section name with the id, it ignores case, `-`, and `_`.
  `live-transfers`, `live_transfers`, and `LiveTransfers` reach the same section.
- `Build()` reads no other section.
- `Build()` validates the options and throws `InvalidOperationException` for an incorrect value.

To change an option with no rebuild, add its key to the section.

### Keys that have no effect

Configuration cannot set the processor id. `Build()` logs a `ProcessorId` key in the section as an
unrecognised setting. The processor id does not change.

| Key in the section | Warning that `Build()` logs |
|---|---|
| A key with a spelling error | `Unrecognised setting. Check the spelling — it had no effect.` |
| `ProcessorId` | `Unrecognised setting. Check the spelling — it had no effect.` |
| `Observers`, `MeterFactory`, or `Time` | `This setting is not configurable and had no effect. Set it in code.` |

`DroneId`, the id of this instance, can come from the section.

## Configure the application

The application reads the same configuration sources as `Host.CreateApplicationBuilder`. These
sources include:

- `appsettings.json`
- Environment variables
- The command line, if you supply `args`

### Set the connection

The application uses the first source in this table that has a value.

| # | Source |
|---|---|
| 1 | `options.Connection(...)` in the delegate |
| 2 | `ConnectionString` or `Connection` in `Kurrent:Surge:Processors:{ProcessorId}` |
| 3 | `surge.Connection(...)` in `builder.AddKurrentSurge(...)` |
| 4 | `ConnectionStrings:Kurrent:Surge` |
| 5 | `ConnectionStrings:KurrentDB` |

`Build()` resolves sources 2 to 5 before it runs the delegate, so the delegate receives the
connection the processor will use. A connection that the delegate sets replaces it.

When the application compares keys, it ignores case, `-`, and `_`. `order-processor` and
`OrderProcessor` find the same entry.

If no source has a value, `Build()` throws `InvalidOperationException`. The message contains the
processor id.

To configure the client, use `options.Connection(connectionString, client => ...)`. To attach a
catalog that no assembly attribute activates, call `client.Messages.UseCatalog(...)` in that
delegate. See [Catalogs](/kurrent-surge/schema/catalogs/).

The builder is an `IHostApplicationBuilder`, so `builder.AddKurrentSurge(...)` works on it too. To
change the configured connection at the application level, call `surge.Connection(client => ...)`
in `builder.AddKurrentSurge(...)`. This example attaches a catalog at that level:

```csharp
var builder = SurgeProcessor.CreateBuilder<OrderProcessor>(args);

builder.AddKurrentSurge(surge => surge.Connection(client =>
    client.Messages.UseCatalog(catalog)));

await builder.Build().RunAsync();
```

The processor and each service that gets `KurrentClient` from the container, for example a seeder,
use the same connection options. The table shows what each level changes:

| Level | Call | Reaches |
|---|---|---|
| Processor | `options.Connection(...)` | The processor only |
| Application | `builder.AddKurrentSurge(surge => surge.Connection(...))` | The processor and each service that gets `KurrentClient` from the container |

If the processor's own section sets `ConnectionString` or `Connection`, the processor uses that
value and not the application connection.

## Processor identity

The processor id comes from `CreateBuilder` only. Pass it as the second argument. If you do not pass
it, the builder uses the type name in kebab-case: `LiveTransferProcessor` gives
`live-transfer-processor`.

The runtime uses the processor id for these items:

- The configuration section
- The checkpoints
- The name of the health check
- The `processor_id` tag on the metrics
- The start of the drone id, which is the id of this instance

When you create the builder, it sets the application name to the processor id in Title Case. If the
host `applicationName` setting has a value, the application keeps that name. The command-line option
`--applicationName` and the environment variable `DOTNET_APPLICATIONNAME` set it.

The table shows the result for `CreateBuilder<LiveTransferProcessor>`:

| You pass | Processor id | Application name |
|---|---|---|
| Nothing | `live-transfer-processor` | `Live Transfer Processor` |
| The id `live-transfers` | `live-transfers` | `Live Transfers` |
| The id `live-transfers` and `--applicationName=Transfers` | `live-transfers` | `Transfers` |

The checkpoints use the processor id as their key. If the processor id changes, the processor starts
again from its configured initial position. If you pass no id and rename the class, the processor id
changes. To keep the same processor id, pass it:

```csharp
var builder = SurgeProcessor.CreateBuilder<OrderProcessor>(args, "orders");
```

### Change the identity in code

If the delegate sets `ProcessorId`, or your code sets `builder.Environment.ApplicationName`, the
builder does not follow the change:

- `Build()` reads no section for the new id. The options come from the section of the first id.
- The health check keeps the first id as its name.
- The application name stays as it is.
- The drone id starts with the new id. To choose the drone id, set `DroneId`.

## Register dependencies

`builder.Services` is the container for the processor and its dependencies. The builder registers
the processor class as a singleton, and the container resolves its constructor dependencies. To
supply your own instance, register it on `builder.Services`.

Register the dependencies on `builder.Services`:

```csharp title="Program.cs"
using Kurrent.Surge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = SurgeProcessor.CreateBuilder<OrderProcessor>(args);

builder.Services.AddSingleton<IOrderStore, SqlOrderStore>();
builder.AddServiceDefaults();
builder.AddNpgsqlDataSource("orders-db");

await builder.Build().RunAsync();
```

Extension methods for `IHostApplicationBuilder` work on the builder. Examples are the Aspire service
defaults and Aspire client integrations such as `AddNpgsqlDataSource`. Extension methods for
`IServiceCollection`, such as Serilog's `AddSerilog`, work on `builder.Services`.

To seed data, register a hosted service. The QuickStart registers `TransferSeeder` with
`AddHostedService<TransferSeeder>()`, and the host runs it at the same time as the processor.

## Read the health check

The application registers one health check. The name of the health check is the processor id. Its
tags are `kurrent`, `surge`, and `processor`.

The table shows the health for each processor status:

| Processor status | Health |
|---|---|
| Not started | Degraded |
| `Starting` | Degraded |
| `Processing` | Healthy |
| `Stopping` or `Stopped` | Unhealthy |
| Failed | Unhealthy, with the exception |

To read the health check, get `IDrone` and `HealthCheckService` from the host services:

```csharp
using Kurrent.Surge.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var host = builder.Build();

var drone  = host.Services.GetRequiredService<IDrone>();
var health = host.Services.GetRequiredService<HealthCheckService>();
var report = await health.CheckHealthAsync();

var processor = report.Entries[drone.ProcessorId];
```

After the processor starts, each result contains these data entries:

| Key | Contains |
|---|---|
| `processorId` | The processor id |
| `droneId` | The id of this instance |
| `status` | The `DroneStatus` of the processor |
| `totalRecords` | The number of processed records |
| `recordsPerSecond` | The current throughput |
| `lowestCommittedPosition` | The lowest committed position |
| `lastProcessedAt` | The time of the last processed record. Absent before the first record |

`SurgeProcessor` has no HTTP endpoint. To send the report to a different system, register an
`IHealthCheckPublisher` as a singleton. By default, the host runs each publisher at 30-second
intervals.

The health check does not show an exception from `OnInitialize`. The host stops, and the exit code
is 1.

## Failure and exit code

`RunAsync` runs the host until Ctrl+C, SIGTERM, or its `cancellationToken` stops it. Then it
disposes the host.

If the processor fails, the host logs the exception at the `Error` level and stops. `RunAsync`
returns without an exception, and the process exit code is 1. A container orchestrator sees exit
code 1 and starts the process again. The processor continues at or before the record that failed.

A stop by Ctrl+C or SIGTERM is not a failure, so the exit code stays 0.

These four items show a failure:

- The exit code of the process, which is 1
- The host log, which has an `Error` entry with the exception
- The health check, which reports Unhealthy with the exception
- `Environment.ExitCode` in the program, after `RunAsync` returns

This code writes a line after a failure:

```csharp
await builder.Build().RunAsync();

if (Environment.ExitCode == 1)
    Console.Error.WriteLine("The processor failed. See the host log.");
```

If the program returns an `int`, for example with `return 0;`, that value replaces exit code 1.

After `Build()`, resolve these services from `host.Services`:

| Service | Contains |
|---|---|
| The processor class `T` | The processor instance, a singleton |
| `IDrone` | `ProcessorId`, `Status`, and `Options`. `Status` is the `DroneStatus` of the processor. `Options` is the `ProcessorOptions` that the processor runs with |
| `HealthCheckService` | The health report. The processor entry has the processor id as its key |

[Error Handling](/kurrent-surge/processors/error-handling/) covers what counts as a failure and
where the position stops.

## One processor per application

`SurgeProcessor` runs one processor only. If you register a second processor, for example with
`builder.Services.AddKurrentSurge(...)`, `Build()` throws `InvalidOperationException`. The message
names the other processor type.

The table shows which entry point to use:

| You need | Use |
|---|---|
| One processor in its own process | `SurgeProcessor` |
| Several processors in one process | `AddKurrentSurge` on `Host.CreateApplicationBuilder` |
| A processor in a web application | `AddKurrentSurge` on `WebApplication.CreateBuilder` |

[Wiring Up Surge](/kurrent-surge/client/dependency-injection/) covers `AddKurrentSurge`.
