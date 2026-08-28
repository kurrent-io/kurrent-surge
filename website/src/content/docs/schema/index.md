---
title: Schema
description: Declare your message types once, and let the SDK register and validate them.
---

The SDK maps CLR types to schema names, encodes records, and talks to the KurrentDB schema registry
on your behalf.

Three parts:

| Part | Does |
|---|---|
| [Catalogs](/schema/catalogs/) | declare which CLR types are messages and what they are called |
| [Registration](/schema/registration/) | register and validate those schemas against the registry |
| [Formats](/schema/formats/) | encode and decode — JSON, Protobuf, Avro |

## The shortest version

Declare a catalog:

```csharp
[assembly: MessageCatalog("Orders.Events", AlwaysActive = true)]
```

Then append your own types. The SDK resolves the schema name, encodes the payload, and stamps the
record:

```csharp
var records = new[] { AppendRecord.Create(new OrderPlaced("order-123")) };
await client.Streams.AppendStream("orders-123", records, ExpectedStreamState.NoStream);
```

On read, the schema name resolves back to the CLR type and `record.Value` is your object.

## Why it exists

A stream holds bytes and a name. Without a mapping, every consumer re-implements the same guesswork
about what those bytes are. The catalog makes the mapping a declaration, the registry makes it
shared, and validation makes a breaking change fail at registration instead of at read time.

Everything is source-generated. No reflection, so it works under Native AOT.
