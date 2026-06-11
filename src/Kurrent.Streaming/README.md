# Kurrent.Processors

A message processing framework for [KurrentDB](https://kurrent.io) — build streaming processors with automatic checkpointing and partitioning.

## Installation

```shell
dotnet add package Kurrent.Streaming
```

### Supported Frameworks

- .NET 10.0

## Quick Start

```csharp
using Kurrent.Client.Streams;

await KurrentProcessors.Create(p => p
        .ProcessRecordsAsync(async ctx => {
            foreach (var record in ctx.Records)
                Console.WriteLine($"Processing: {record.EventType}");
        }))
    .ConnectTo("kurrentdb://localhost:2113")
    .WithProcessorId("my-processor")
    .RunAsync(cancellationToken);
```

## Features

- **Automatic Checkpointing** — durable position tracking with configurable intervals
- **Filter Expressions** — server-side SQL filtering to process only matching events
- **Batch Processing** — process records individually or in configurable batches
- **OpenTelemetry** — distributed tracing and metrics for processor operations
- **Dependency Injection** — first-class `IServiceCollection` integration
- **Graceful Shutdown** — clean stop with checkpoint flush on cancellation

## Links

- [Documentation](https://kurrent.io/docs)
- [Example Projects](https://github.com/kurrent-io/Kurrent.Client/tree/main/examples/processors)
- [GitHub Repository](https://github.com/kurrent-io/Kurrent.Client)
- [License (Apache 2.0)](https://github.com/kurrent-io/Kurrent.Client/blob/main/LICENSE.md)
