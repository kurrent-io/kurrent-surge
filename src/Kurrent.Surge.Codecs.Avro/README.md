# Kurrent.Surge.Codecs.Avro

An [Apache Avro](https://avro.apache.org/) codec for the [Kurrent](https://kurrent.io) .NET SDK, built on the official
[Apache.Avro](https://github.com/apache/avro/tree/main/lang/csharp) library.

## What this package does

Kurrent Surge serializes messages through a pluggable `ISchemaCodec`. This package adds Avro alongside the SDK's
built-in JSON and Protobuf support. It reuses the same registration model, the same message catalog, and the same read
and write path.

You write `.avsc` schema files. A build-time generator turns each one into a C# message type and the code that encodes
and decodes it. Nothing reflects over your types at runtime.

## Why Avro

Avro gives you a compact binary format with a real schema behind every message. Choose it when you want strict schema
evolution rules, payloads smaller than JSON, or interoperability with the wider Avro and Kafka ecosystem.

Avro is schema-first. Your message type takes its shape from a schema definition rather than from a class, and that is
what separates this package from the SDK's MessagePack codecs.

## Installation

```shell
dotnet add package Kurrent.Surge.Codecs.Avro
```

Targets .NET 10.0.

## Quick start

Add a schema file anywhere in your project:

```json
// Schemas/OrderPlaced.avsc
{
  "type": "record",
  "name": "OrderPlaced",
  "namespace": "Acme.Orders",
  "fields": [
    { "name": "OrderId", "type": "string" },
    { "name": "Amount", "type": "double" }
  ]
}
```

Point your project at it. This is the one manual step, because nothing can know your folder layout:

```xml
<ItemGroup>
  <AdditionalFiles Include="Schemas/*.avsc" />
</ItemGroup>
```

Build. `Acme.Orders.OrderPlaced` now exists:

```csharp
public sealed partial record OrderPlaced : global::Avro.Specific.ISpecificRecord {
    public required string OrderId { get; set; }
    public required double Amount  { get; set; }
    // ISpecificRecord.Schema, Get and Put follow, implemented explicitly
}
```

Register it like any other message type:

```csharp
using Kurrent.Surge.Codecs.Avro;

var client = KurrentClient.Create(connectionString, options => {
    options.Messages.UseAvro();
    options.Messages.MapMessage<Acme.Orders.OrderPlaced>();
});
```

Installing this package is the only reference you need. The generator ships inside it.

## Native AOT and trimming

This package sets `IsAotCompatible` and `IsTrimmable`, and publishes clean under `PublishAot`.

The generator writes encode and decode into your assembly at compile time, so nothing resolves a type at runtime. This
package deliberately avoids Apache's `SpecificDatumReader<T>` and `SpecificDatumWriter<T>`: they walk the schema through
`Avro.Specific.ObjectCreator`, which calls `Type.GetType(string)` and `MakeGenericType` and needs a JIT.

`ISpecificRecord.Schema` parses its schema text on first read rather than at type initialization. Code that never asks
for the schema never pays for it: `Avro.Schema.Parse` pulls in Newtonsoft.Json, about 9.5 MB of a native binary.

## Supported schema constructs

The generator fully qualifies every type it emits, so the real output reads
The generator fully qualifies every type it emits, so the real output reads
`global::System.Collections.Generic.IReadOnlyList<string>` where the examples below read `IReadOnlyList<string>`. The
examples drop the qualification to stay readable; nothing else about them is shortened.

### Primitive types

| Avro      | C#        |
|-----------|-----------|
| `null`    | `object?` |
| `boolean` | `bool`    |
| `int`     | `int`     |
| `long`    | `long`    |
| `float`   | `float`   |
| `double`  | `double`  |
| `bytes`   | `byte[]`  |
| `string`  | `string`  |

### Records

A record becomes a `sealed partial record` implementing `ISpecificRecord` explicitly, so `Schema`, `Get` and `Put` stay
off the public surface. Declare a record inline and it becomes a sibling type in the same namespace, never a nested one.

```json
{
  "type": "record", "name": "Order", "namespace": "Acme.Orders",
  "fields": [
    { "name": "Customer", "type": {
        "type": "record", "name": "Customer",
        "fields": [ { "name": "Name", "type": "string" } ] } }
  ]
}
```

```csharp
public sealed partial record Order    : ISpecificRecord { public required Customer Customer { get; set; } }
public sealed partial record Customer : ISpecificRecord { public required string   Name     { get; set; } }
```

A record may reference itself, or any named type declared earlier in the same file.

### Enums

```json
{ "type": "enum", "name": "Status", "symbols": ["Pending", "Shipped", "Cancelled"] }
```

```csharp
public enum Status { Pending, Shipped, Cancelled, }
```

### Fixed

```json
{ "type": "fixed", "name": "Md5", "size": 16 }
```

```csharp
public sealed partial class Md5 : SpecificFixed {
    public Md5() : base(16) { }
    public int Size => 16;
}
```

### Arrays and maps

Arrays map to `IReadOnlyList<T>` and maps to `IReadOnlyDictionary<string, T>`. The decoder builds a concrete `List<T>`
or `Dictionary<string, T>` and hands it over as the interface. Avro sends an element count per block, so the decoder
sizes the collection once instead of growing it from empty.

```json
{ "name": "Lines",    "type": { "type": "array", "items": "string" } }
{ "name": "Counters", "type": { "type": "map",   "values": "long" } }
```

```csharp
public required IReadOnlyList<string>             Lines    { get; set; }
public required IReadOnlyDictionary<string, long> Counters { get; set; }
```

### Unions

Three shapes, three treatments.

**A null union** maps to the nullable form of its other branch:

```json
{ "name": "Note", "type": ["null", "string"] }
```

```csharp
public string? Note { get; set; }
```

**A union of records** gets an interface, and every branch implements it. The interface is named from the
declaring record and the field, so two fields with the same branches each get their own:

```json
{ "name": "Subject", "type": [
    { "type": "record", "name": "Cat", "fields": [ { "name": "Lives", "type": "int" } ] },
    { "type": "record", "name": "Dog", "fields": [ { "name": "Bark",  "type": "string" } ] } ] }
```

```csharp
public partial interface ISightingSubjectVariant;

public sealed partial record Cat : ISpecificRecord, ISightingSubjectVariant { ... }
public sealed partial record Dog : ISpecificRecord, ISightingSubjectVariant { ... }

public required ISightingSubjectVariant Subject { get; set; }
```

A record appearing in two different unions implements both interfaces.

**Any other union** maps to `object`, or `object?` when one branch is null. The encoder recovers the
branch with a type test:

```json
{ "name": "Value", "type": ["string", "int", "boolean"] }
```

```csharp
public required object Value { get; set; }
```

### Value equality

A generated record compares by value, all the way down. C# would otherwise synthesize equality that compares a `byte[]`,
a list or a dictionary member by reference, so two decoded messages carrying identical data would come out unequal.

```csharp
var a = (Order)codec.Decode(wire, typeof(Order));
var b = (Order)codec.Decode(wire, typeof(Order));

a == b;                        // true — element by element, including nested records
a.GetHashCode() == b.GetHashCode();  // true
```

Lists compare in order, maps compare regardless of order, and `byte[]` compares by content. Nested records use their own
generated equality, so the comparison recurses to any depth.

Collections take part in the hash. Mutate one after using the record as a dictionary key and the entry is lost — the
same hazard every property already carries, since all of them have setters.

### Field defaults

A field with no default is emitted as `required`. A field with one is initialized from it and drops `required`:

```json
{ "name": "Retries", "type": "int",              "default": 3 },
{ "name": "Label",   "type": "string",           "default": "none" },
{ "name": "Tags",    "type": { "type": "array", "items": "string" }, "default": ["a", "b"] },
{ "name": "Count",   "type": ["null", "int"],    "default": 0 }
```

```csharp
public int          Retries { get; set; } = 3;
public string       Label   { get; set; } = "none";
public IReadOnlyList<string> Tags { get; set; } = new List<string> { "a", "b" };
public int?         Count   { get; set; } = 0;
```

The last line matters. A union default has to match *one* element of the union, not the first, so a `["null", "int"]`
field defaulting to `0` initializes to `0` rather than to null.

Defaults for `bytes` and `fixed` follow the specification: the JSON string's code points 0-255 are the bytes, so `"ÿ"`
is the single byte `0xFF`.

The generator drops a default it cannot express in C#, and the property stays `required`. This covers a `fixed` default
whose length disagrees with the declared size, and every default on a logical type.

### Documentation

A `doc` attribute on a record, enum, fixed or field becomes an XML summary on the generated member.

### Names that collide with C# keywords

The generator escapes an Avro name with `@`, one segment at a time. The name on the wire never changes:

```json
{ "type": "record", "name": "Booking", "namespace": "Acme.event.internal",
  "fields": [ { "name": "class", "type": "string" } ] }
```

```csharp
namespace Acme.@event.@internal;

public sealed partial record Booking : ISpecificRecord {
    public required string @class { get; set; }
}
```

A dotted `name` is a fullname and overrides the `namespace` attribute, as the specification requires.

## Logical types

A logical type annotates a base type. It changes no bytes — it tells the codec what the bytes mean.

| Logical type             | Base        | C# type            |
|--------------------------|-------------|--------------------|
| `decimal`                | `bytes`     | `Avro.AvroDecimal` |
| `decimal`                | `fixed`     | `Avro.AvroDecimal` |
| `uuid`                   | `string`    | `System.Guid`      |
| `uuid`                   | `fixed(16)` | `System.Guid`      |
| `date`                   | `int`       | `System.DateTime`  |
| `time-millis`            | `int`       | `System.TimeSpan`  |
| `time-micros`            | `long`      | `System.TimeSpan`  |
| `timestamp-millis`       | `long`      | `System.DateTime`  |
| `timestamp-micros`       | `long`      | `System.DateTime`  |
| `local-timestamp-millis` | `long`      | `System.DateTime`  |
| `local-timestamp-micros` | `long`      | `System.DateTime`  |
| `timestamp-nanos`        | `long`      | `NanoInstant`      |

Conversions match `Avro.Util` exactly, down to the direction each one truncates. A test asserts wire parity with Apache
for every construct in the table.

### Nanosecond timestamps

`DateTime` resolves to 100 nanoseconds, so it would truncate every `timestamp-nanos` value. The generator emits
`NanoInstant` instead, a readonly struct over the wire value:

```csharp
var moment = new NanoInstant(1_700_000_000_123_456_789L);

moment.EpochSeconds;        // 1700000000
moment.NanoOfSecond;        // 123456789, always 0..999,999,999, before the epoch as well
moment.ToDateTimeOffset();  // truncates to 100ns
```

The range belongs to the format rather than to this type. A signed 64-bit nanosecond count spans
**1677-09-21 to 2262-04-11**, and no representation reaches further.

Apache.Avro does not model `timestamp-nanos`. It resolves the field to an unknown logical type over a `long` and rejects
any other value, so `ISpecificRecord.Get` hands it the `long` and `Put` reconstructs the `NanoInstant`. Apache's own
readers and writers keep working, and nothing changes on the wire.

### UUIDs backed by a fixed

The specification has allowed `uuid` on `fixed(16)` since Avro 1.12, and Java supports it. Apache.Avro's C#
implementation rejects it, and rejects it early enough that `Schema.Parse` throws before the unknown-logical-type
fallback can catch it.

This package ships `UuidLogicalType`, which widens Apache's built-in through `LogicalTypeFactory.Instance.Register` —
Apache's own extension point. A `string` base behaves exactly as before.

Registration replaces the built-in for the whole process, so **the generator emits the registration only into assemblies
that declare such a schema**. An assembly without one pays nothing.

The codec writes the bytes in RFC 4122 order. `Guid.ToByteArray()` without an argument is little-endian across the first
three fields and would swap every value, so the generated code passes `bigEndian: true` in both directions.

## What this package does not support

### Other schema file formats

`.avsc` only. Wire nothing else into `AdditionalFiles`.

`.avpr` (JSON protocol) and `.avdl` (IDL) describe RPC protocols. Support for them is a separate piece of work.

### Schema resolution and evolution

The reader schema always equals the writer schema. This package decodes with the same schema it encoded with, and
implements none of Avro's resolution rules: no field reordering, no defaults for fields the writer omitted, no numeric
promotion.

Consequently `aliases`, a field's `order`, and an enum's `default` influence nothing here. All three survive verbatim in
the schema text a generated type exposes, so an Apache-based reader still sees them.

### Generic mode

There is no `GenericRecord` path. Every message needs a static CLR type, and that is what keeps the codec free of
runtime type resolution, and therefore AOT-safe.

### Logical types nobody implements in .NET

These parse and round-trip correctly, degrading to their base type exactly as Apache.Avro does. You get the underlying
value rather than a richer one.

| Logical type            | Base        | What you get    | Why                                                                                         |
|-------------------------|-------------|-----------------|---------------------------------------------------------------------------------------------|
| `big-decimal`           | `bytes`     | `byte[]`        | No C# implementation exists to check an encoding against                                    |
| `duration`              | `fixed(12)` | the fixed class | Months plus days plus milliseconds has no faithful BCL type. `TimeSpan` cannot hold a month |
| `local-timestamp-nanos` | `long`      | `long`          | Apache implements neither nanos variant, so there is no reference behaviour to match        |
| any unrecognised name   | any         | the base type   | The specification requires implementations to ignore unknown logical types                  |

`uuid` on a `fixed` of any size other than 16 also degrades, matching Java.

### Invalid schemas

The generator does not validate Avro names, enum symbol uniqueness, or union constraints. A schema that breaks those
rules reaches the C# compiler as an error rather than a diagnostic. Apache.Avro 1.12.2 validates all three when a
generated type's `Schema` property is read.

## Interoperability with Apache.Avro

Two upstream limitations surface as exceptions rather than as wrong data.

**Apache's Specific writer cannot encode a logical type backed by a fixed.** Its conversion produces a
`GenericFixed`, and `SpecificDatumWriter.WriteFixed` accepts only a `SpecificFixed`. This affects `decimal` on `fixed`
and `uuid` on `fixed`. Use `GenericDatumWriter` for those, or this package's own codec, which writes the bytes directly
and is unaffected.

**A known logical type on an invalid base throws in Apache's parser.** `{"type":"long","logicalType":"date"}`
declares `date`, which is defined on `int`. This package erases the annotation and uses the base type, as the
specification instructs and as Java does. Apache.Avro throws instead, so reading `Schema` on such a type raises
`AvroTypeException` even though the codec itself works.

## Diagnostics

| Id         | Severity | Meaning                                                                                  |
|------------|----------|------------------------------------------------------------------------------------------|
| `KAVRO001` | Error    | The file is not valid JSON, or not a valid Avro schema. Reported at the offending offset |
| `KAVRO002` | Warning  | The root schema is not a record, so no codec is generated                                |
| `KAVRO003` | Warning  | Two schemas declare the same type with different shapes. Only the first is generated     |

The generator silently deduplicates two schemas that declare the same type identically: sharing a nested record between
files is ordinary, and generates the same C# either way.

## Credits

The generator's Avro name handling and the shape of its emitted types derive from
[AvroSourceGenerator](https://github.com/sadicangel/AvroSourceGenerator) (MIT, Copyright (c) 2024 sadicangel).

## Links

- [Documentation](https://kurrent.io/docs)
- [Apache Avro specification](https://avro.apache.org/docs/1.12.0/specification/)
- [Apache.Avro (C#) source](https://github.com/apache/avro/tree/main/lang/csharp)
- [GitHub Repository](https://github.com/kurrent-io/Kurrent.Client)
- [License (Apache 2.0)](https://github.com/kurrent-io/Kurrent.Client/blob/main/LICENSE.md)
