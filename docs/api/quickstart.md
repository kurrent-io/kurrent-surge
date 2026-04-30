---
order: 1
---

# Getting started

Connect a .NET application to KurrentDB, append events to a stream, and read them back.

## Prerequisites

- .NET 8.0 or later
- A running KurrentDB instance (local or remote). For local development, the [KurrentDB Docker image](https://hub.docker.com/r/eventstore/eventstore) is the fastest path.
- A connection string for the target node or cluster.

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

Pass a connection string to `KurrentClient.Create`:

```csharp
using Kurrent.Client;

await using var client = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113?tls=false");
```

The example above targets a local development node with TLS disabled. For a production node, omit `?tls=false`. TLS is enabled by default.

```csharp
await using var client = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113");
```

## Append events

Events in KurrentDB are organized into streams. Each stream is identified by a name, and events are appended in order. Build records with `AppendRecord.New` and append them via `client.Streams.AppendStream`:

```csharp
using Kurrent.Client.Streams;

var record = AppendRecord.New
    .ForStream("order-123")
    .WithValue(new {
        OrderId  = "order-123",
        Product  = "Widget",
        Quantity = 5
    })
    .Build();

var result = await client.Streams.AppendStream(record);

Console.WriteLine($"Stream:   {result.Stream}");
Console.WriteLine($"Revision: {result.Revision}");
Console.WriteLine($"Position: {result.Position}");
```

`AppendStream` serializes the value as JSON by default and appends it to the stream named on the record. If the stream does not exist, it is created automatically.

### Appending multiple events

Use `AppendStreamRequest.For(...)` to append several records to one stream atomically:

```csharp
var request = AppendStreamRequest.For("order-456")
    .ExpectingState(ExpectedStreamState.Any)
    .WithMessage(new { OrderId = "order-456", Product = "Gadget" })
    .WithMessage(new { OrderId = "order-456", ConfirmedAt = DateTimeOffset.UtcNow })
    .Build();

var result = await client.Streams.AppendStream(request);
```

## Read events

Read all events from a stream:

```csharp
var stream = client.Streams.ReadStreamRecords("order-123");

await foreach (var item in stream) {
    item.Switch(
        record    => Console.WriteLine($"Event: {record.TypeName} at revision {record.Revision}"),
        heartbeat => { }
    );
}
```

Read the first or last event in a stream:

```csharp
var first = await client.Streams.ReadFirstStreamRecord("order-123");
var last  = await client.Streams.ReadLastStreamRecord("order-123");
```

## Next steps

- [Configuration](./configuration.md): connection strings, programmatic options, dependency injection, and security
