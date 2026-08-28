---
title: Getting started
description: Install the SDK, connect to KurrentDB, append and read your first records.
---

## Install

```shell
dotnet add package Kurrent.Surge
```

Requires .NET 10.0 or a subsequent version.

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

## Connection strings

```
kurrentdb://localhost:2113                               single node, insecure
kurrentdb://admin:changeit@localhost:2113                with credentials
kurrentdb+discover://node1:2113,node2:2113               cluster discovery
kurrents://localhost:2113?tls=true&tlsVerifyCert=true    TLS
```

`ReadPreference` selects the node: `Leader`, `Follower`, `Random` or `ReadOnlyReplica`.

## Next

Reading a stream by hand is fine for a query. To *process* a stream continuously — with
checkpoints, retries and partitioning — use a [processor](/processors/) instead.
