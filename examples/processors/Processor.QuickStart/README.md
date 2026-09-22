# Processor.QuickStart

This example shows the minimum code for a processor host with a processor class. It has one file. The
top-level statements make the host, and the processor class is below them.

## What You'll Learn

- How to make a host with `Host.CreateApplicationBuilder(args)` and add a processor with
  `builder.AddKurrentSurge(surge => surge.AddProcessor<T>(...))`
- How to give the processor an ID: pass `live-transfers` as the first argument of `AddProcessor<T>`
- How to set the processor options in the `AddProcessor<T>` lambda: the filter, the start position, and the checkpoint interval
- How to write a `MessageProcessor` class with one handler for each message type
- How to add Serilog and a `BackgroundService` through `builder.Services`
- How the host gets the connection string from `appsettings.json`, with no connection code

## How It Works

1. `builder.AddKurrentSurge(surge => surge.AddProcessor<LiveTransferProcessor>("live-transfers", options => { ... }))`
   registers one `LiveTransferProcessor` on the host builder. The processor ID is `live-transfers`.
2. The host reads the options from the `Kurrent:Surge:Processors:live-transfers` configuration
   section. Then the lambda sets the subscription filter, the start position, and the checkpoint
   interval.
3. The top-level statements add Serilog and `TransferSeeder` to `builder.Services`.
4. `builder.Build().RunAsync()` starts the host and runs the processor until you press `Ctrl+C`.
5. If the processor stops because of an error, the host stops. To make the process exit with a
   failure code, add `surge.FailProcessOnFatalError()` inside `AddKurrentSurge`.

`LiveTransferProcessor` is below the top-level statements. Its constructor adds one handler for
`PlayerTransferred` and one handler for `PlayerRegistered`.

## Prerequisites

- A KurrentDB server on `localhost:2113`
- The .NET 10 SDK

## Running the Example

```bash
cd examples/processors/Processor.QuickStart
dotnet run
```

When you start the example for the first time, the subscription gets only new records.
`TransferSeeder` waits two seconds. Then it writes a `PlayerRegistered` event and a
`PlayerTransferred` event to a new `player-*` stream each second. The console output shows one
`Registration:` line and one `Transfer:` line for each pair.

`TransferSeeder` is in `Transfermarkt.Domain`. Its only function is to supply input.

Press `Ctrl+C` to stop the host.

## Key Code

### Program.cs

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

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

## Sample Output

```
[INF] Seeding player registrations and transfers into player-* streams
[INF] Registration: <player-name> (<country>) <position>
[INF] Transfer: <player-name> from <club-id> to <club-id> for <fee>
```

## Configuration

`Program.cs` passes the processor ID `live-transfers` to `AddProcessor<T>`. The health check uses
this ID.

The host reads the options from the `Kurrent:Surge:Processors:live-transfers` configuration section
before the lambda runs, so a value that the lambda sets replaces the configured value.

The host reads the connection string from `ConnectionStrings:kurrentdb` in `appsettings.json`.

To use a different server, change `appsettings.json` or set the `ConnectionStrings__kurrentdb`
environment variable.

## Next Steps

- For a processor class in a different file, see `Processor.FraudDetection`.
- To host a processor in ASP.NET Core, use `AddKurrentSurge`. For an example, see
  `Processor.AspNetMinimal`.
