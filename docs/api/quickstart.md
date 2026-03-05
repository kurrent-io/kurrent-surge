---
order: 1
---

# Getting started

Get started by connecting your application to KurrentDB.

## Installation

Install the `Kurrent.Client` NuGet package:

::: code-tabs#shell

@tab dotnet CLI

```bash
dotnet add package Kurrent.Client
```

@tab Package Manager

```powershell
Install-Package Kurrent.Client
```

:::

## Connect to KurrentDB

The fastest way to connect is with a connection string:

```csharp
using Kurrent.Client;

await using var client = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113?tls=false");
```

This creates a client connected to a local KurrentDB instance with default credentials and TLS disabled — suitable for development.

For production setups with TLS enabled:

```csharp
await using var client = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113");
```

When TLS is not explicitly disabled, it is enabled by default.

## Append events

Events in KurrentDB are organized into streams. Each stream is identified by a name, and events are appended in order.

```csharp
using Kurrent.Client.Streams;

var record = new AppendRecord("OrderPlaced", new {
    OrderId   = "order-123",
    Product   = "Widget",
    Quantity  = 5
});

var result = await client.Streams.AppendAsync("order-123", record);

Console.WriteLine($"Written at position: {result.Position}");
Console.WriteLine($"Stream revision: {result.StreamRevision}");
```

`AppendAsync` serializes the event data as JSON and appends it to the specified stream. If the stream does not exist, it is created automatically.

### Appending multiple events

```csharp
var request = new AppendStreamRequest(
    stream: "order-456",
    expectedState: ExpectedStreamState.Any,
    messages: [
        new AppendRecord("OrderPlaced", new { OrderId = "order-456", Product = "Gadget" }),
        new AppendRecord("OrderConfirmed", new { OrderId = "order-456", ConfirmedAt = DateTime.UtcNow })
    ]
);

var result = await client.Streams.AppendAsync(request);
```

## Read events

Read all events from a stream:

```csharp
var stream = await client.Streams.ReadStreamRecordsAsync("order-123");

await foreach (var item in stream) {
    item.Switch(
        record => Console.WriteLine($"Event: {record.EventType} at revision {record.Position.StreamRevision}"),
        heartbeat => { }
    );
}
```

Read the first or last event in a stream:

```csharp
var first = await client.Streams.ReadFirstStreamRecordAsync("order-123");
var last  = await client.Streams.ReadLastStreamRecordAsync("order-123");
```

## Next steps

- [Configuration](./configuration.md) — connection strings, builder API, dependency injection, and security options
