---
title: Getting Started
description: Install Kurrent Surge, then take the producer path or the consumer path.
---

Two roles, two first programs. Install and declare your messages either way, then take the path that
matches what you are building.

| You are | You want | Go to |
|---|---|---|
| A **producer** | Get events into KurrentDB and read them back | [Append and read](#produce-append-and-read) |
| A **consumer** | React to events as they arrive, continuously | [Run a processor](#consume-run-a-processor) |

For what Surge is and how it is designed, read [What is Kurrent Surge](/kurrent-surge/what-is-surge/) first.

## Install

```shell
dotnet add package Kurrent.Surge
```

Requires .NET 10.0 or a subsequent version. That one package carries the client, the processors and
the codecs.

## Declare your messages

Both paths need this. The SDK maps a CLR type to a schema name through a **catalog**. Declare one for
the namespace that holds your events and the client picks it up:

```csharp title="Orders.Events.cs"
using Kurrent.Surge.Schema.Catalogs;

[assembly: MessageCatalog("Orders.Events", AlwaysActive = true)]

namespace Orders.Events {
    public record OrderPlaced(string OrderId, string Customer, int Quantity);
}
```

Without a catalog the client cannot resolve a schema name for your type, and the append is rejected.
See [Catalogs](/kurrent-surge/schema/catalogs/) for the other ways to declare one.

## Produce: append and read

A producer talks to KurrentDB through `KurrentClient`. `AddKurrentSurge` registers one for
injection. One client reaches all five modules, and streams is the one you want first.

```csharp title="Program.cs"
using Kurrent.Surge;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddKurrentSurge(surge =>
    // tls=false is the local development mode. Production drops it: /kurrent-surge/client/security/
    surge.Connection("kurrentdb://localhost:2113?tls=false"));

using var host = builder.Build();

var client = host.Services.GetRequiredService<KurrentClient>();

var records = new[] { AppendRecord.Create(new OrderPlaced("ORD-001", "Alice", 3)) };
var result  = await client.Streams.AppendStream("order-001", records, ExpectedStreamState.NoStream);

Console.WriteLine($"Wrote revision {result.Revision}.");
```

Reading it back is a lazy enumeration, so nothing buffers the whole stream:

```csharp title="Program.cs"
await foreach (var message in client.Streams.ReadStream("order-001")) {
    if (message.Value is Record record)
        Console.WriteLine(record.Value);
}
```

**The whole program runs:**
[`examples/streams/Streams.QuickStart`](https://github.com/kurrent-io/kurrent-surge/tree/main/examples/streams/Streams.QuickStart)
is 38 lines that append three order events and read them back, with the output it produces.

Building a client directly with `KurrentClient.Create` is also supported, for scenarios the
container does not own. See [Configuring the Client](/kurrent-surge/client/configuring-the-client/).

Next: [Append](/kurrent-surge/streams/append/) for concurrency control, [Read](/kurrent-surge/streams/read/) for the live tail,
[Configuring the Client](/kurrent-surge/client/configuring-the-client/) for the other four modules.

## Consume: run a processor

Reading a stream by hand suits a query. A **processor** suits everything else. It owns the
subscription, the checkpoints, the retries and the batching, and leaves you one method.

```csharp
using Kurrent.Surge.Processors;

public sealed class OrderNotifier : IRecordProcessor {
    public ValueTask ProcessRecord(ProcessRecordContext context) {
        if (context.Record.Value is OrderPlaced placed)
            Console.WriteLine($"Order {placed.OrderId} placed by {placed.Customer}");

        return ValueTask.CompletedTask;
    }
}
```

To run the processor, give its type to `AddProcessor<T>` on the host builder:

```csharp
using Kurrent.Surge;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.AddKurrentSurge(surge => surge.AddProcessor<OrderNotifier>(options => {
    // tls=false is the local development mode. Production drops it: /kurrent-surge/client/security/
    options.Connection("kurrentdb://localhost:2113?tls=false");
    options.Subscription.Filter = RecordFilter.ByStream("order-001");
}));

await builder.Build().RunAsync();
```

`AddProcessor` registers `OrderNotifier` in the container for you. Without a filter the processor
reads every stream. SIGINT and SIGTERM stop the host, and the processor commits what it has handled
before it exits, so a container stop drains rather than kills.

**The whole program runs:**
[`examples/processors/Processor.QuickStart`](https://github.com/kurrent-io/kurrent-surge/tree/main/examples/processors/Processor.QuickStart)
is one file with inline handlers.
[`Processor.WordCount`](https://github.com/kurrent-io/kurrent-surge/tree/main/examples/processors/Processor.WordCount)
seeds a stream and counts words across it.
[`Processor.AspNetMinimal`](https://github.com/kurrent-io/kurrent-surge/tree/main/examples/processors/Processor.AspNetMinimal)
hosts one in an ASP.NET Core application.

Next: [Running a Processor](/kurrent-surge/processors/running-a-processor/),
[Writing a Processor](/kurrent-surge/processors/writing-a-processor/),
[Checkpointing](/kurrent-surge/processors/checkpointing/), [Error Handling](/kurrent-surge/processors/error-handling/).

## More examples

Eighteen runnable projects live in
[`examples/`](https://github.com/kurrent-io/kurrent-surge/tree/main/examples), covering fraud
detection, IoT thresholds, leaderboards, stream-table joins, Postgres and Redis persistence, Kafka
connectors and the schema registry. Sixteen carry a README, and each project runs with `dotnet run`.

To connect to a cluster, or to configure TLS, discovery or keepalive, see
the [Connection String Reference](/kurrent-surge/client/connection-strings/).
