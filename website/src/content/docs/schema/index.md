---
title: Schema
description: How Kurrent Surge turns a CLR type into bytes on a stream, and back again.
---

Every record on a stream carries a **schema name**, a string identity for its type. Readers use it
to decide which CLR type to decode into. The registry uses it as the key for storage and
compatibility rules.

Four things decide what happens to a message:

| Step | Decides | Page |
|---|---|---|
| **Catalog** | Which types this client can append and decode | [Catalogs](/schema/catalogs/) |
| **Name** | The string identity a record carries on the wire | [Schema names](/schema/names/) |
| **Format** | How the payload becomes bytes | [Formats](/schema/formats/) |
| **Registry** | Whether the shape is registered and checked | [Registration](/schema/registration/) |

## Nothing is inferred

Kurrent Surge never invents a mapping. The set of types a client speaks is exactly what you
declared, either with attributes on your contracts or with explicit `Map` calls.

```csharp
[MessageType<OrderPlaced>("order.placed.v1")]
public record OrderPlaced(string OrderId);
```

Appending a type no catalog maps throws. There is nothing safe to guess a permanent wire name from,
and a name guessed today is a contract you cannot change tomorrow.

Reading is deliberately softer. A record whose schema name has no mapping comes back **raw**, as
bytes plus the name with no decoded value. An unknown type from a newer producer therefore does not
kill your subscription.

## The registry is opt-in

The default is `KurrentClientSchemaOptions.Disabled`: schema names resolve locally through your
catalog, and the registry is never consulted. Turn it on when you want registration, validation, or
both. See [Registration](/schema/registration/).

## One definition format, four wire formats

This is the part that surprises people. Your payload can be JSON, Protobuf or Avro on the wire. But
whatever the encoding, **the definition stored in the registry is a JSON Schema**, exported from your
CLR type.

That is deliberate. It gives one comparable shape to run compatibility rules against, no matter how
the bytes were encoded. [Formats](/schema/formats/) covers what each encoding does and where they
genuinely differ.
