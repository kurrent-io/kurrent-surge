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

Under `kurrentdb+discover://`, the client discovers topology through gossip, ranks the members by
your node preference, and follows a leader change without you handling it. `ReadPreference` selects
the node: `Leader`, `Follower`, `Random` or `ReadOnlyReplica`.

That ranking is enforced by a real gRPC load balancing policy registered on the channel, not by a
wrapper that resolves an address once. See [Load balancing](/client/load-balancing/) for the
mechanism and [Clusters and discovery](/client/clusters-and-discovery/) for the settings.

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
