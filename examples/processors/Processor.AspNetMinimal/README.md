# Processor.AspNetMinimal

Demonstrates hosting a Kurrent processor as an ASP.NET Core background service with Serilog logging.

## What You'll Learn

- Registering a processor with `AddKurrentSurge` so the SDK hosts it inside ASP.NET Core
- Integrating Serilog for structured logging
- Handling messages by type with `MessageProcessor`
- Seeding sample events from a `BackgroundService` that shares the registered `KurrentClient`
- Graceful shutdown through the host's cancellation token

## Prerequisites

- KurrentDB running locally on port 2113
- .NET 10 SDK

## Running the Example

```bash
dotnet run
```

The processor starts automatically. A hosted `TransferSeeder` writes a player registration and a
transfer every second, so the log shows `Registration:` and `Transfer:` lines right away.
Press `Ctrl+C` to gracefully shutdown.

## Key Code

### LiveTransferProcessor.cs

Handles each message type with the context logger:

```csharp
public class LiveTransferProcessor : MessageProcessor {
    public LiveTransferProcessor() {
        Process<PlayerTransferred>((transfer, ctx) => {
            ctx.Logger.LogInformation(
                "Transfer: {PlayerName} from {FromClub} to {ToClub} for {Fee:C}",
                transfer.PlayerName, transfer.FromClubId.ToString(), transfer.ToClubId.ToString(), transfer.Fee.Amount);
        });
    }
}
```

### Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.AddKurrentSurge(surge => {
    surge.Connection("kurrentdb://admin:changeit@localhost:2113?tls=false");

    surge.AddProcessor(new LiveTransferProcessor(), prx => {
        prx.Subscription.InitialPosition = SubscriptionInitialPosition.Latest;
        prx.Subscription.Filter          = RecordFilter.ByExpression("stream like 'player-%'");
    });
});

builder.Services.AddHostedService<TransferSeeder>();

var app = builder.Build();
app.MapGet("/health", () => Results.Ok("Healthy"));
await app.RunAsync();
```

## Endpoints

- `GET /health` - Returns "Healthy" (useful for container orchestration)

## Configuration

The connection string is set in `Program.cs`. To configure it externally, drop the `Connection` call
and add `ConnectionStrings:KurrentDB` to `appsettings.json` or the environment; `AddKurrentSurge`
reads it.

## Next Steps

- Add state persistence (see `Processor.WithPostgres`)
- Add caching (see `Processor.WithRedisCache`)
- See `Processor.FullExample` for a complete production setup
