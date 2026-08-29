---
title: Client
description: Five modules. Streams, registry, indexes, connectors and administration.
---

`KurrentClient` is the entry point. It exposes five feature modules as properties.

```csharp
await using var client = KurrentClient.Create("kurrentdb://localhost:2113?tls=false");

await client.Streams.AppendStream(...);
await client.Registry.CreateSchema(...);
await client.Indexes.Create(...);
await client.Connectors.Start(...);
await client.Admin.StartScavenge(...);
```

| Module | Covers |
|---|---|
| [Streams](/client/streams/) | [Append](/client/streams/append/), [read](/client/streams/read/), [manage](/client/streams/manage/), [filter](/client/streams/filters/), [record metadata](/client/streams/metadata/) |
| [Connectors](/client/connectors/) | Create, configure, start, stop |
| [Registry](/client/registry/) | Schemas and versions |
| [Admin](/client/admin/) | Scavenge, node priority, shutdown |
| [Indexes](/client/indexes/) | Secondary indexes |

Each page lists every operation the module carries, with its overloads.

## Creating a client

```csharp
KurrentClient.Create("kurrentdb://localhost:2113")
KurrentClient.Create(options)
KurrentClient.Create("kurrentdb://localhost:2113", opts => { … })
```

Or through dependency injection:

```csharp
builder.Services.AddKurrent(kurrent => kurrent
    .Connection("kurrentdb://admin:changeit@localhost:2113?tls=false"));
```

## Cluster connections

Under `kurrentdb+discover://`, the client discovers topology through gossip, picks a channel per
call, and reacts to a leader change without you handling it. `ReadPreference` selects the node:
`Leader`, `Follower`, `Random` or `ReadOnlyReplica`.

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
