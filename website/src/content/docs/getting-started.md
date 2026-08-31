---
title: Getting started
description: Install Kurrent Surge, then take the producer path or the consumer path.
---

Two roles, two first programs. Install and declare your messages either way, then take the path that
matches what you are building.

| You are | You want | Go to |
|---|---|---|
| A **producer** | Get events into KurrentDB and read them back | [Append and read](#produce-append-and-read) |
| A **consumer** | React to events as they arrive, continuously | [Run a processor](#consume-run-a-processor) |

For what Surge is and how it is designed, read [What is Kurrent Surge](/what-is-surge/) first.

## Install

```shell
dotnet add package Kurrent.Surge
```

Requires .NET 10.0 or a subsequent version. `Kurrent.Surge` carries the client and the processors;
the [other three packages](/what-is-surge/) are opt in.

## Declare your messages

Both paths need this. The SDK maps a CLR type to a schema name through a **catalog**. Declare one for
the namespace that holds your events and the client picks it up:

```csharp
using Kurrent.Surge.Schema.Catalogs;

[assembly: MessageCatalog("Orders.Events", AlwaysActive = true)]

namespace Orders.Events {
    public record OrderPlaced(string OrderId, string Customer, int Quantity);
}
```

Without a catalog the client cannot resolve a schema name for your type, and the append is rejected.
See [Catalogs](/schema/catalogs/) for the other ways to declare one.

## Produce: append and read

A producer talks to KurrentDB through `KurrentClient`. One client reaches all five modules, and
streams is the one you want first.

```csharp
using Kurrent.Surge.Client;

// tls=false is the local development mode. Production drops it: /client/security/
await using var client = KurrentClient.Create("kurrentdb://localhost:2113?tls=false");

var records = new[] { AppendRecord.Create(new OrderPlaced("ORD-001", "Alice", 3)) };
var result  = await client.Streams.AppendStream("order-001", records, ExpectedStreamState.NoStream);

Console.WriteLine($"Wrote revision {result.Revision}.");
```

Reading it back is a lazy enumeration, so nothing buffers the whole stream:

```csharp
await foreach (var message in client.Streams.ReadStream("order-001")) {
    if (message.Value is Record record)
        Console.WriteLine(record.Value);
}
```

**The whole program runs:**
[`examples/streams/Streams.QuickStart`](https://github.com/kurrent-io/kurrent-surge/tree/main/examples/streams/Streams.QuickStart)
is 31 lines that append three order events and read them back, with the output it produces.

Next: [Append](/streams/append/) for concurrency control, [Read](/streams/read/) for the live tail,
[Client](/client/) for the other four modules.

## Consume: run a processor

Reading a stream by hand suits a query. A **processor** suits everything else. It owns the
subscription, the checkpoints, the retries and the batching, and leaves you one method.

```csharp
using Kurrent.Surge.Streaming;

public sealed class OrderNotifier : IRecordProcessor {
    public ValueTask ProcessRecord(ProcessRecordContext context) {
        if (context.Record.Value is OrderPlaced placed)
            Console.WriteLine($"Order {placed.OrderId} placed by {placed.Customer}");

        return ValueTask.CompletedTask;
    }
}
```

Give it a connection and run it. The processor builds and owns its own client, so you do not pass in
the one from the producer path:

```csharp
await KurrentProcessor
    .Create(new OrderNotifier(), opts => {
        // tls=false is the local development mode. Production drops it: /client/security/
        opts.Connection("kurrentdb://localhost:2113?tls=false");
        opts.Subscription.Filter = RecordFilter.ByStream("order-001");
    })
    .RunWithGracefulShutdownAsync();
```

Without a filter the processor reads every stream. `RunWithGracefulShutdownAsync` handles SIGINT and
SIGTERM, so a container stop drains rather than kills.

**The whole program runs:**
[`examples/processors/Processor.WordCount`](https://github.com/kurrent-io/kurrent-surge/tree/main/examples/processors/Processor.WordCount)
seeds a stream and counts words across it.
[`Processor.AspNetMinimal`](https://github.com/kurrent-io/kurrent-surge/tree/main/examples/processors/Processor.AspNetMinimal)
hosts one as an ASP.NET Core background service, which is how you would deploy it.

Next: [Writing a processor](/processors/writing-a-processor/),
[Checkpointing](/processors/checkpointing/), [Error handling](/processors/error-handling/).

## More examples

Eighteen runnable projects live in
[`examples/`](https://github.com/kurrent-io/kurrent-surge/tree/main/examples), covering fraud
detection, IoT thresholds, leaderboards, stream-table joins, Postgres and Redis persistence, Kafka
connectors and the schema registry. Each one has its own README and runs with `dotnet run`.

To connect to a cluster, or to configure TLS, discovery or keepalive, see
[Connection strings](/client/connection-strings/).
