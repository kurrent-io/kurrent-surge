# Processor.Minimal

The simplest possible Kurrent processor - demonstrates the bare minimum setup required to process records.

## What You'll Learn

- How to create a basic `IRecordProcessor` implementation
- How records are automatically decoded (no manual deserialization needed)
- Using `RunWithGracefulShutdownAsync()` for console applications
- Using `ILogger<T>` with Serilog in a console app

## Prerequisites

- KurrentDB running locally on port 2113

## Running the Example

```bash
dotnet run
```

Press `Ctrl+C` to gracefully shutdown the processor.

## Key Code

### MinimalProcessor.cs

```csharp
public sealed class MinimalProcessor(ILogger<MinimalProcessor> logger) : IRecordProcessor {
    public ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records) {
            // Records are decoded by default - just access the Value
            if (record.Value is PlayerTransferred transfer) {
                logger.LogInformation("[Transfer] {PlayerName} from {FromClub} to {ToClub}",
                    transfer.PlayerName, transfer.FromClubId, transfer.ToClubId);
            }
        }
        return ValueTask.CompletedTask;
    }
}
```

### Program.cs

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());
var processor = new MinimalProcessor(loggerFactory.CreateLogger<MinimalProcessor>());

await KurrentProcessors.Create(processor)
    .ConnectTo("kurrentdb://localhost:2113")
    .RunWithGracefulShutdownAsync();
```

## What's Happening

1. **Auto-generated ProcessorId**: Derived from the processor class name (`minimal_processor`)
2. **Automatic checkpointing**: Progress is saved periodically
3. **Graceful shutdown**: `Ctrl+C` triggers clean shutdown with 30-second timeout
4. **Record decoding**: Messages are deserialized before reaching your processor

## Next Steps

- Add lifecycle hooks (`OnInitialize`, `OnCheckpointComplete`, `OnShutdown`)
- Add filtering with `WithFilter()`
- Store state in a database (see `Processor.WithPostgres`)
