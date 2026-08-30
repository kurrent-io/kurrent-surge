---
title: Client
description: Five modules. Streams, registry, indexes, connectors and administration.
---

`KurrentClient` is the entry point. It exposes five feature modules as properties.

```csharp
// tls=false is the local development mode. Production drops it: /client/security/
await using var client = KurrentClient.Create("kurrentdb://localhost:2113?tls=false");

await client.Streams.AppendStream(...);
await client.Registry.CreateSchema(...);
await client.Indexes.Create(...);
await client.Connectors.Start(...);
await client.Admin.StartScavenge(...);
```

| Module | Covers |
|---|---|
| [Streams](/streams/) | [Append](/streams/append/), [read](/streams/read/), [manage](/streams/manage/), [filter](/streams/filters/), [record metadata](/streams/metadata/) |
| [Connectors](/client/connectors/) | Create, configure, start, stop |
| [Registry](/client/registry/) | Schemas and versions |
| [Admin](/client/admin/) | Scavenge, node priority, shutdown |
| [Indexes](/client/indexes/) | Secondary indexes |

Each page lists every operation the module carries, with its overloads.

Building one, pointing it at a cluster, and tuning the transport are all
[Connecting](/client/connection-strings/). This page is about what you reach once you have one.

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
