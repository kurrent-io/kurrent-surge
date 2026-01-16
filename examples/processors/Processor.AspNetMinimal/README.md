# Processor.AspNetMinimal

Demonstrates hosting a Kurrent processor as an ASP.NET Core background service with Serilog logging.

## What You'll Learn

- Hosting a processor within ASP.NET Core using `BackgroundService`
- Integrating Serilog for structured logging
- Using `ILogger<T>` with `[LoggerMessage]` source generation for high-performance logging
- Resolving processor dependencies from the DI container
- Using the host's cancellation token for graceful shutdown

## Prerequisites

- KurrentDB running locally on port 2113
- .NET 9.0 SDK

## Running the Example

```bash
dotnet run
```

The processor starts automatically. Press `Ctrl+C` to gracefully shutdown.

## Key Code

### TransferProcessor.cs

Uses `[LoggerMessage]` source generation for zero-allocation logging:

```csharp
public sealed partial class TransferProcessor(ILogger<TransferProcessor> logger) : IRecordProcessor {
    public ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records) {
            if (record.Value is PlayerTransferred transfer) {
                LogTransfer(transfer.PlayerName, transfer.FromClubId, transfer.ToClubId, transfer.Fee);
            }
        }
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Transfer: {PlayerName} from {FromClub} to {ToClub} for {Fee:C}")]
    partial void LogTransfer(string playerName, string fromClub, string toClub, decimal fee);
}
```

### TransferProcessorService.cs

Hosts the processor as a `BackgroundService`:

```csharp
public sealed class TransferProcessorService(IServiceProvider services) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var processor = services.GetRequiredService<TransferProcessor>();

        await KurrentProcessors.Create(processor)
            .ConnectTo("kurrentdb://localhost:2113")
            .RunAsync(stoppingToken);
    }
}
```

### Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.Services.AddSingleton<TransferProcessor>();
builder.Services.AddHostedService<TransferProcessorService>();

var app = builder.Build();
app.MapGet("/health", () => Results.Ok("Healthy"));
await app.RunAsync();
```

## Endpoints

- `GET /health` - Returns "Healthy" (useful for container orchestration)

## Configuration

Configure the connection string via environment variables or appsettings:

```bash
export KURRENTDB_CONNECTION="kurrentdb://localhost:2113"
```

## Next Steps

- Add state persistence (see `Processor.WithPostgres`)
- Add caching (see `Processor.WithRedisCache`)
- See `Processor.FullExample` for a complete production setup
