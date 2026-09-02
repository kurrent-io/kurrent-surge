---
title: Client
description: Five modules. Streams, registry, indexes, connectors and administration.
---

`KurrentClient` is one object with five modules hanging off it and **one connection behind all of
them**. You do not build a client per feature, you do not authenticate five times, and a call into
any module travels the channel the others already opened.

Almost all of an application's traffic goes through **streams**, where records are written and read.
The other four are operational and most projects touch them rarely: the **registry** holds schemas
and their versions, **indexes** builds secondary indexes over the log, **connectors** manages the
server-side pipelines that move data out of KurrentDB, and **admin** runs scavenges and node
operations.

```csharp
await using var client = KurrentClient.Create(connectionString);

await client.Streams.AppendStream("order-001", records, ExpectedStreamState.NoStream);
var schema = await client.Registry.GetSchema("order-placed");
```

Building a client, pointing it at a cluster, and tuning the transport are all
[Connecting](/kurrent-surge/client/connection-strings/). This page is about what you reach once you have one.

## Errors

Operations throw typed exceptions deriving from `KurrentException`, each carrying an error code.
There is no result wrapper. Catch the exception type you care about:

```csharp
try {
    await client.Streams.AppendStream(stream, records, ExpectedStreamState.NoStream);
}
catch (StreamAlreadyExistsException) {
    // the stream was there
}
```

## Two protocols, one surface

Append runs on V2 gRPC. Read and manage run on the V1 legacy service. Indexes and connectors use V2
messages. You call one client; the protocol split is an implementation detail.
