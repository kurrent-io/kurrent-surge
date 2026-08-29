---
title: Getting started
description: Install Kurrent Surge, connect to KurrentDB, append and read your first records.
---

This gets a record into KurrentDB and reads it back. For what Surge is and how it is designed, read
[What is Kurrent Surge](/what-is-surge/) first.

## Install

```shell
dotnet add package Kurrent.Surge
```

Requires .NET 10.0 or a subsequent version. `Kurrent.Surge` carries the client and the processors;
the [other three packages](/what-is-surge/) are opt in.

## Declare your messages

The SDK maps a CLR type to a schema name through a **catalog**. Declare one for the namespace that
holds your events, and the client picks it up:

```csharp
using Kurrent.Surge.Schema.Catalogs;

[assembly: MessageCatalog("Orders.Events", AlwaysActive = true)]

namespace Orders.Events {
    public record OrderPlaced(string OrderId);
}
```

Without a catalog the client cannot resolve a schema name for your type, and the append is rejected.

## Connect, append, read

```csharp
using Kurrent.Surge.Client;

// tls=false is the local development mode. Production drops it: /client/security/
await using var client = KurrentClient.Create("kurrentdb://localhost:2113?tls=false");

// Append
var records = new[] { AppendRecord.Create(new OrderPlaced("order-123")) };
var result  = await client.Streams.AppendStream("orders-123", records, ExpectedStreamState.NoStream);

Console.WriteLine($"The append wrote revision {result.Revision}.");

// Read it back
await foreach (var message in client.Streams.ReadStream("orders-123")) {
    if (message.Value is Record record)
        Console.WriteLine(record.Value);
}
```

`ReadStream` and `Subscribe` return `IAsyncEnumerable<T>` over a channel. Enumerate once.

## Next

Reading a stream by hand is fine for a query. To *process* a stream continuously, use a
[processor](/processors/) instead. It owns the checkpoints, the retries and the partitioning.

To connect to a cluster, or to configure TLS, discovery or keepalive, see
[Connection strings](/client/connection-strings/).
