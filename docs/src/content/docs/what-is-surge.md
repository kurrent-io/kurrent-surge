---
title: What is Kurrent Surge
description: The .NET SDK for KurrentDB, and the design decisions behind it.
---

Kurrent Surge is the .NET SDK for KurrentDB and the Kurrent Platform. It replaces the v1
`KurrentDB.Client` packages and the EventStoreDB clients before them.

The v1 client was a transport. It moved bytes to a stream and back, and everything above that —
schema names, serialization, checkpoints, reconnection, batching — was yours to build. Surge keeps
that surface and ships the layers most applications ended up writing themselves.

**Nothing is inferred.** Every schema name, every codec and every mapping is something you declared
and the compiler checked, not something the SDK guessed at runtime. A rename is a refactor rather
than an outage, because the name on the wire is something you wrote down.

## How it is designed

**Built for Native AOT.** Catalogs, discriminated unions and Avro codecs come from source generators.
Both packages are `IsAotCompatible` and `IsTrimmable`, and trim and AOT analysis runs unsuppressed
across the repository. The map from CLR type to schema name and the table of codecs are both frozen
dictionaries built once at startup. JSON goes through a source-generated
`JsonSerializerContext`, with the reflection fallback behind a feature switch the linker removes.

**Serialization happens for you, in three formats.** You append a declared type and the SDK encodes
it. The format comes from the message type unless you state one: Avro when a codec was generated for
the type, Protobuf when it is a Protocol Buffers message, JSON otherwise. Every record carries its
own format on the wire, so a reader picks the codec from the record instead of from configuration.
A producer moving to Protobuf therefore does not break a consumer. Raw bytes skip the layer
entirely. See [Data Formats](/kurrent-surge/schema/formats/).

**Resilience is configured on both halves.** The client retries at the gRPC channel, declared in the
service config so the transport enforces it rather than a loop above it. A processor reconnects its
own subscription on a pipeline you can extend or replace outright. The same vocabulary on both sides,
and a hand-rolled reconnect loop on neither. See
[Configuring the Client](/kurrent-surge/client/configuring-the-client/) and
[Error Handling](/kurrent-surge/processors/error-handling/).

**Routing is a gRPC policy, not a wrapper.** Discovery, node ranking, connection replacement and
leader-change following all run inside the channel, as a load balancing policy registered on it. See
[Clusters and Load Balancing](/kurrent-surge/client/clusters-and-discovery/).

**Failures have types.** Every operation throws a typed exception carrying an error code. There is no
result wrapper to unpack, and no single catch-all to inspect for a reason string.

## Where to start

[Getting Started](/kurrent-surge/getting-started/) is two first programs, one for producing and one
for consuming. If you are coming from the v1 client or from EventStoreDB,
[Migration](/kurrent-surge/migration/) is the shorter path.
