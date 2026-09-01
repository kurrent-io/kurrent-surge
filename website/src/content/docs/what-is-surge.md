---
title: What is Kurrent Surge
description: The design, the goals, and what changed from the v1 KurrentDB client.
---

**Nothing is inferred.** Every schema name, every codec and every mapping is something you declared
and the compiler checked, not something the SDK guessed at runtime.

Kurrent Surge is the .NET SDK for KurrentDB and the Kurrent Platform. It replaces the v1
`KurrentDB.Client` packages, and the EventStoreDB clients before them. The v1 client was a transport:
it moved bytes to a stream and back, and everything above that was yours to build. Surge keeps that
surface and adds the layers most applications ended up writing themselves.

Three of those layers, in the order you meet them.

## Declare your messages

A record on a stream carries a schema name, a string identity that outlives your code. The v1 client
took the CLR type name and wrote that, so renaming a class silently broke every consumer at the next
deploy, with no error anywhere to find.

Surge makes you say the name. A **catalog** maps a type to one, a source generator turns that
declaration into code while you build, and appending an undeclared type throws instead of writing a
name you can never change. The cost is one attribute. What it buys is that a rename is a refactor
rather than an outage.

Reading is deliberately softer than writing. A record whose schema name has no mapping comes back
raw rather than throwing, so an unknown type from a newer producer does not kill a running
subscription. Strict where a mistake is permanent, forgiving where it is not.

See [Schema](/schema/).

## Append and read

One client reaches five modules, and streams is the one you meet first. Appends are transactional
across streams, with optimistic concurrency you opt into per call. Reads are lazy, so the server
streams results as you consume them and nothing buffers a whole stream into memory. Both work in
terms of the types you declared rather than in bytes.

The other four modules are the same client and the same shape: the schema registry, secondary
indexes, connectors, and node administration. There is no second client type to construct and no
second connection to configure.

See [Client](/client/) and [Streams](/streams/).

## Process what arrives

Reading by hand suits a question you ask once. Anything continuous wants a **processor**, because
the hard parts of consuming a log are not the reading. They are the checkpoints, the reconnection
after a leader moves, the batching, the partition assignment, and doing all of it again correctly
after a restart. The runtime owns those. You write one method and say which records you want.

A processor builds and owns its own client, so it is not something you attach to the client above.
It is the other way to talk to KurrentDB, and which one you reach for depends on whether you are
producing or consuming.

See [Processors](/processors/).

## How it is designed

**Built for Native AOT.** Catalogs, discriminated unions and Avro codecs come from source generators.
All four packages are `IsAotCompatible` and `IsTrimmable`, trim and AOT analysis runs unsuppressed
across the repository, and both the map from CLR type to schema name and the table of codecs are
frozen dictionaries built once at startup. JSON goes through a source-generated
`JsonSerializerContext`, with the reflection fallback behind a feature switch the linker removes.

**Serialization happens for you, in three formats.** You append a declared type and the SDK encodes
it. Every record carries its own format on the wire, so a reader picks the codec from the record
instead of from configuration, and a producer moving to Protobuf does not break a consumer. JSON is
the default, Protobuf and Avro are a property on the record you append, and raw bytes skip the layer
entirely. See [Formats](/schema/formats/).

**Resilience is configured on both halves.** The client retries at the gRPC channel, declared in the
service config so the transport enforces it rather than a loop above it. A processor reconnects its
own subscription on a pipeline you can extend or replace outright. The same vocabulary on both sides,
and a hand-rolled reconnect loop on neither. See [Configuring the client](/client/configuring-the-client/) and
[Error handling](/processors/error-handling/).

**Routing is a gRPC policy, not a wrapper.** Discovery, node ranking, connection replacement and
leader-change following all run inside the channel, as a load balancing policy registered on it. See
[Clusters and load balancing](/client/clusters-and-discovery/).

**One surface over two protocols.** Appends run on the V2 gRPC service. Reads and stream management
run on the V1 legacy service. Indexes and connectors use V2 messages. You call one client, and the
split stays an implementation detail.

**Failures have types.** Every operation throws a typed exception carrying an error code. There is no
result wrapper to unpack, and no single catch-all to inspect for a reason string.

**Install only what you need.** There is no meta-package.

| Package | What it is | Links |
|---|---|---|
| `Kurrent.Surge` | The client and the processors. What you install | [Docs](/client/) · [NuGet](https://www.nuget.org/packages/Kurrent.Surge) · [Source](https://github.com/kurrent-io/kurrent-surge/tree/main/src/Kurrent.Surge) |
| `Kurrent.Surge.Core` | Contracts, value types, and the JSON, Protobuf, and Avro codecs. No gRPC, no transport. Arrives with the client | [Docs](/schema/formats/) · [NuGet](https://www.nuget.org/packages/Kurrent.Surge.Core) · [Source](https://github.com/kurrent-io/kurrent-surge/tree/main/src/Kurrent.Surge.Core) |
| `Kurrent.Surge.Schema.Catalogs` | Message declaration and the catalog generator | [Docs](/schema/catalogs/) · [NuGet](https://www.nuget.org/packages/Kurrent.Surge.Schema.Catalogs) · [Source](https://github.com/kurrent-io/kurrent-surge/tree/main/src/Kurrent.Surge.Schema.Catalogs) |

## What changed from the v1 client

| | v1 `KurrentDB.Client` | Kurrent Surge |
|---|---|---|
| Packages | Four, one per feature | One |
| Client | One type per feature | One client, five modules |
| Schema names | Inferred from the type name | Declared in a catalog |
| Errors | `WrongExpectedVersionException` and a catch-all | A type per failure, each with a code |
| Stream processing | Write the subscription loop yourself | A processor runtime |
| Serialization | Hand-rolled, by you | JSON, Protobuf or Avro through a codec |
| Schema registry | Not addressed | First-class, opt in |
| Secondary indexes | Not addressed | First-class |
| AOT | Reflection in the path | Source-generated, `IsAotCompatible` |
| Disposal | `IDisposable` | `IAsyncDisposable` |

Your connection strings keep working. `esdb://` and `eventstore://` still parse, and every parameter
means what it means in the other KurrentDB SDKs. See [Migration](/migration/) for the rest.

## What it is not

**Not a rewrite of your domain.** Surge serializes contracts you write. It has no opinion about
aggregates, commands or handlers.

**Not a projections engine.** Server-side projections stay on the server. Processors are a
client-side runtime for consuming streams.

**Not finished.** It is new, and it is built to grow into the rest of the platform without anyone
needing a second SDK.
