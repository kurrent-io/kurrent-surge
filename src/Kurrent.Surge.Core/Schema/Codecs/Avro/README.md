# Avro Codec

An [Apache Avro](https://avro.apache.org/) codec for the [Kurrent](https://kurrent.io) .NET SDK, with its own AOT-safe
binary encoding and no Apache.Avro dependency. It ships inside `Kurrent.Surge.Core`, next to the built-in JSON and
Protobuf codecs. Interop with the official
[Apache.Avro](https://github.com/apache/avro/tree/main/lang/csharp) library is an opt-in knob.

## What this codec does

Kurrent Surge serializes messages through a pluggable `ISchemaCodec`. The Avro codec sits next to the SDK's built-in
JSON and Protobuf codecs, and reuses the same registration model and the same read and write path.

You write `.avsc` schema files. A build-time generator turns each one into a C# message type and the code that encodes
and decodes it. Nothing reflects over your types at run time.

## Why Avro

Avro gives you a compact binary format with a real schema behind every message. Choose it for strict schema evolution
rules, for payloads smaller than JSON, or to interoperate with the wider Avro and Kafka ecosystem.

Avro is schema-first. Your message type takes its shape from a schema definition and not from a class, and that is what
separates Avro from the JSON and Protobuf codecs, which derive their schema from the CLR type.

## Installation

Nothing to install. The codec and its compile-time generator ship inside `Kurrent.Surge.Core`, which arrives with
`Kurrent.Surge`.

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
public sealed partial record OrderPlaced {
    public required string OrderId { get; set; }
    public required double Amount  { get; set; }
    // Equals and GetHashCode follow, and compare by value
}
```

Register it like any other message type:

```csharp
var client = KurrentClient.Create(connectionString, options => {
    options.Messages.MapMessage<Acme.Orders.OrderPlaced>();
});
```

The codec registers automatically, exactly like JSON and Protobuf.

No extra reference is needed. The generator ships inside `Kurrent.Surge.Core`.

A type whose `.avsc` never reached `AdditionalFiles` has no generated codec. Encoding or decoding it throws
`AvroCodecNotGeneratedException`, which names the type and the item group to add.

## Generator options

Set these MSBuild properties in your `.csproj` or in `Directory.Build.props`. `Kurrent.Surge.Core` ships the
`CompilerVisibleProperty` entries that carry them to the generator.

| Property                          | Values                | Default  |
|-----------------------------------|-----------------------|----------|
| `KurrentAvroAccessModifier`       | `Public`, `Internal`  | `Public` |
| `KurrentAvroXmlDocs`              | `Off`, `On`           | `Off`    |
| `KurrentAvroTimeModel`            | `Bcl`, `NodaTime`     | `Bcl`    |
| `KurrentAvroNanosecondTimestamps` | `Long`, `NanoInstant` | `Long`   |
| `KurrentAvroApacheInterop`        | `Off`, `On`           | `Off`    |

Values are not case sensitive. A value the generator does not recognise raises `KAVRO004`, and the default holds.

`KurrentAvroApacheInterop` decides whether a generated type carries the surface that Apache's own readers and writers
need. Off emits no `ISpecificRecord`, no `Schema` property and no `SpecificFixed` base. The wire bytes are the same
either way, because Avro's contract is the encoding and not the CLR shape.

`KurrentAvroTimeModel` decides which family the temporal logical types surface as. See [Logical types](#logical-types).

## Native AOT and trimming

`Kurrent.Surge.Core` sets `IsAotCompatible` and `IsTrimmable`, and the codec publishes clean under `PublishAot`.

The generator writes encode and decode into your assembly at compile time, so nothing resolves a type at run time. The
codec deliberately avoids Apache's `SpecificDatumReader<T>` and `SpecificDatumWriter<T>`: they walk the schema through
`Avro.Specific.ObjectCreator`, which calls `Type.GetType(string)` and `MakeGenericType` and needs a JIT.

Under the default, a generated type holds no schema text at all, so nothing roots `Avro.Schema.Parse`. That call pulls
in Newtonsoft.Json, about 9.5 MB of a native binary. With Apache interop on, the type gains a `Schema` property. It
parses its text on first read and never at type initialization, so code that never asks for the schema never pays.

## Supported schema constructs

The generator fully qualifies every type it emits, so the real output reads
`global::System.Collections.Generic.IReadOnlyList<string>` where the examples below read `IReadOnlyList<string>`. The
examples drop the qualification to stay readable. They shorten nothing else.

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

A record becomes a `sealed partial record` with one property for each field, plus the `Equals` and `GetHashCode` pair
that gives it value equality. Declare a record inline and it becomes a sibling type in the same namespace, never a
nested one.

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
public sealed partial record Order    { public required Customer Customer { get; set; } }
public sealed partial record Customer { public required string   Name     { get; set; } }
```

With Apache interop on, the record also implements `ISpecificRecord` explicitly, so `Schema`, `Get` and `Put` stay off
the public surface.

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
public sealed partial class Md5 : AvroFixedValue {
    public Md5() : base(16) { }
}
```

`AvroFixedValue` holds the bytes in `Value`, reports `Size`, compares by content, and prints as hex. With Apache interop
on the base is `Avro.Specific.SpecificFixed` instead, and the class also carries `Size` and `Schema`.

### Arrays and maps

Arrays map to `IReadOnlyList<T>` and maps to `IReadOnlyDictionary<string, T>`. The decoder builds a concrete `List<T>`
or `Dictionary<string, T>` and hands it over as the interface. Avro sends an element count for each block, so the
decoder sizes the collection once instead of growing it from empty.

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

**A union of records** gets an interface, and every branch implements it. The generator names the interface after the
declaring record and the field, so two fields with the same branches each get their own:

```json
{ "name": "Subject", "type": [
    { "type": "record", "name": "Cat", "fields": [ { "name": "Lives", "type": "int" } ] },
    { "type": "record", "name": "Dog", "fields": [ { "name": "Bark",  "type": "string" } ] } ] }
```

```csharp
public partial interface ISightingSubjectVariant;

public sealed partial record Cat : ISightingSubjectVariant { ... }
public sealed partial record Dog : ISightingSubjectVariant { ... }

public required ISightingSubjectVariant Subject { get; set; }
```

A record that appears in two different unions implements both interfaces.

**Any other union** maps to `object`, or to `object?` when one branch is null. The encoder recovers the
branch with a type test:

```json
{ "name": "Value", "type": ["string", "int", "boolean"] }
```

```csharp
public required object Value { get; set; }
```

### Value equality

A generated record compares by value, all the way down. C# otherwise synthesizes equality that compares a `byte[]`, a
list or a dictionary member by reference, and two decoded messages that carry identical data then come out unequal.

```csharp
var codec = new AvroSchemaCodec();

var a = (Order)codec.Decode(wire, typeof(Order));
var b = (Order)codec.Decode(wire, typeof(Order));

a == b;                              // true — element by element, including nested records
a.GetHashCode() == b.GetHashCode();  // true
```

Lists compare in order, maps compare independently of order, and `byte[]` compares by content. A nested record uses its
own generated equality, so the comparison recurses to any depth.

Collections take part in the hash. Mutate one after you use the record as a dictionary key and the entry is lost. Every
property already carries the same hazard, because all of them have setters.

### Field defaults

The generator emits a field with no default as `required`. A field with a default takes its initializer from that
default and drops `required`. A field that can hold null is never `required`, because null is already a value for it:

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

The last line matters. A union default has to match *one* element of the union and not the first, so a `["null", "int"]`
field with a default of `0` initializes to `0` and not to null.

Defaults for `bytes` and `fixed` follow the specification: the JSON string's code points 0-255 are the bytes, so `"ÿ"`
is the single byte `0xFF`.

The generator drops a default it cannot express in C#. This covers a `fixed` default whose length disagrees with the
declared size, and every default on a logical type.

### Documentation

Set `KurrentAvroXmlDocs` to `On`, and a `doc` attribute on a record, an enum, a fixed or a field becomes an XML summary
on the generated member. Off, which is the default, emits no XML documentation.

### Names that collide with C# keywords

The generator escapes an Avro name with `@`, one segment at a time. The name on the wire never changes:

```json
{ "type": "record", "name": "Booking", "namespace": "Acme.event.internal",
  "fields": [ { "name": "class", "type": "string" } ] }
```

```csharp
namespace Acme.@event.@internal;

public sealed partial record Booking {
    public required string @class { get; set; }
}
```

A dotted `name` is a fullname and overrides the `namespace` attribute, as the specification requires.

## Logical types

A logical type annotates a base type. It changes no bytes — it tells the codec what the bytes mean.

`KurrentAvroTimeModel` selects the column. Both models draw the same distinction the Avro specification does, between a
point on the global timeline and a reading with no zone attached.

| Logical type             | Base        | `Bcl` (default)         | `NodaTime`               |
|--------------------------|-------------|-------------------------|--------------------------|
| `decimal`                | `bytes`     | `AvroDecimal`           | `AvroDecimal`            |
| `decimal`                | `fixed`     | `AvroDecimal`           | `AvroDecimal`            |
| `uuid`                   | `string`    | `System.Guid`           | `System.Guid`            |
| `uuid`                   | `fixed(16)` | `System.Guid`           | `System.Guid`            |
| `date`                   | `int`       | `System.DateOnly`       | `NodaTime.LocalDate`     |
| `time-millis`            | `int`       | `System.TimeOnly`       | `NodaTime.LocalTime`     |
| `time-micros`            | `long`      | `System.TimeOnly`       | `NodaTime.LocalTime`     |
| `timestamp-millis`       | `long`      | `System.DateTimeOffset` | `NodaTime.Instant`       |
| `timestamp-micros`       | `long`      | `System.DateTimeOffset` | `NodaTime.Instant`       |
| `local-timestamp-millis` | `long`      | `System.DateTime`       | `NodaTime.LocalDateTime` |
| `local-timestamp-micros` | `long`      | `System.DateTime`       | `NodaTime.LocalDateTime` |
| `timestamp-nanos`        | `long`      | `long`                  | `NodaTime.Instant`       |
| `duration`               | `fixed(12)` | the fixed class         | `NodaTime.Period`        |

`AvroDecimal` is the codec's own struct, which holds an unscaled value and a scale and does not overflow past 96
bits. With Apache interop on it is `Avro.AvroDecimal` instead, because that is the type Apache's readers and writers
expect to see.

An absolute `timestamp` is `DateTimeOffset` because a `DateTime` with `Kind=Unspecified` encodes differently on two
machines. The offset itself is not on the wire, so a value round-trips to the same instant at offset zero and not to its
original offset. The `local-timestamp` variants stay `DateTime`, because they are wall-clock readings and not instants.

A test asserts wire parity with Apache for every construct in the table.

### Nanosecond timestamps

`timestamp-nanos` surfaces as a plain `long`. This is what Apache does: it sees an unknown logical type over a `long`
and erases it.

Set `KurrentAvroNanosecondTimestamps` to `NanoInstant` for a type that carries the units. `DateTime` resolves to 100
nanoseconds and truncates every value, so the generator emits `NanoInstant`, a readonly struct over the wire value:

```csharp
var moment = new NanoInstant(1_700_000_000_123_456_789L);

moment.EpochSeconds;        // 1700000000
moment.NanoOfSecond;        // 123456789, always 0..999,999,999, before the epoch as well
moment.ToDateTimeOffset();  // truncates to 100ns
```

The range belongs to the format and not to this type. A signed 64-bit nanosecond count spans
**1677-09-21 to 2262-04-11**, and no representation reaches further.

Under the NodaTime time model the setting does nothing. `Instant` is nanosecond-native and carries the value already.

Apache.Avro does not model `timestamp-nanos`. It resolves the field to an unknown logical type over a `long` and rejects
any other value. With Apache interop on, `ISpecificRecord.Get` therefore hands it the `long` and `Put` rebuilds the
`NanoInstant`. Apache's own readers and writers keep working, and nothing changes on the wire.

### UUIDs backed by a fixed

The specification has allowed `uuid` on `fixed(16)` since Avro 1.12, and Java supports it. Apache.Avro's C#
implementation rejects it, and rejects it early enough that `Schema.Parse` throws before the unknown-logical-type
fallback can catch it.

With Apache interop on, the generator emits `UuidLogicalType`, which widens Apache's built-in through
`LogicalTypeFactory.Instance.Register` — Apache's own extension point. A `string` base behaves exactly as before.

Registration replaces the built-in for the whole process, so **the generator emits the type and its registration only
into interop-On assemblies that declare such a schema**. An assembly without one pays nothing, and with interop off
nothing parses schema text, so neither the type nor the registration is needed.

The codec writes the bytes in RFC 4122 order. `Guid.ToByteArray()` without an argument is little-endian across the first
three fields and swaps every value, so the generated code passes `bigEndian: true` in both directions.

## Schema conversion

Three types read Avro schema text at run time. None of them needs a CLR type.

| Type                   | Takes            | Gives                                                                                                   |
|------------------------|------------------|---------------------------------------------------------------------------------------------------------|
| `AvroJsonSchema`       | `SchemaSource`   | the schema as a [JSON Schema 2020-12](https://json-schema.org/draft/2020-12/schema) document            |
| `AvroSchemaTranscoder` | `SchemaSource`   | an Avro binary payload as JSON                                                                          |
| `AvroJsonWriter`       | Avro schema text | an Avro binary payload as JSON, from a parse you hold                                                   |

The exported document describes the JSON that the transcoder writes. A transcoded payload therefore validates against
the exported schema.

Export a schema to publish it to a registry, to validate a payload with any JSON Schema validator, or to embed it in an
OpenAPI 3.1 document.

`AvroJsonSchema` and `AvroSchemaTranscoder` both take a `SchemaSource`. Give it exactly one schema. An Avro document
declares one root type and resolves its named references inside itself, so a source that carries none, or more than
one, raises `ArgumentException`. So does text that is not valid Avro.

`SchemaSource.MessageTypeName` names a message in a format whose document declares several. Avro never reads it.

### Export

```csharp
using Kurrent.Surge.Codecs.Avro;
using Kurrent.Surge.Schema;

var source = SchemaSource.Text(File.ReadAllText("Schemas/Shipment.avsc"));

JsonNode document = AvroJsonSchema.Instance.Export(source);
string   text     = AvroJsonSchema.Instance.ExportAsString(source);
```

`AvroJsonSchema` implements `IJsonSchemaExporter`, and `ExportAsString` is an extension on that interface.

Every document declares draft 2020-12, and `AvroJsonSchema.Dialect` holds that URI. There is no dialect to select.

`JsonSchemaExportOptions` carries four settings. This exporter reads one of them:

| Setting              | What the Avro exporter does                                       |
|----------------------|-------------------------------------------------------------------|
| `Canonical`          | Read. Sorts every object key                                      |
| `SerializerOptions`  | Ignored. Property names come from the schema, not from a CLR type |
| `NullableIsOptional` | Ignored. `required` holds the fields that declare no default      |
| `EnumAsOneOf`        | Ignored. An Avro enum is always a string with an `enum` list      |

`Canonical` makes two schemas of the same shape export byte for byte alike:

```csharp
AvroJsonSchema.Instance.ExportAsString(source, new JsonSchemaExportOptions { Canonical = true });
```

`Canonical` never sorts array items. `oneOf` order is the union tag, and `enum` order is the symbol number. To sort
either one changes what the schema says.

### Transcode

```csharp
using Kurrent.Surge.Codecs.Avro;
using Kurrent.Surge.Schema;

var source = SchemaSource.Text(File.ReadAllText("Schemas/Shipment.avsc"));

ReadOnlyMemory<byte> payload = ...;   // the Avro bytes

string   json = AvroSchemaTranscoder.Instance.ToJson(source, payload);
JsonNode node = AvroSchemaTranscoder.Instance.ToJsonNode(source, payload);

AvroSchemaTranscoder.Instance.ToJson(source, payload, writer);   // into a Utf8JsonWriter you own
```

`AvroSchemaTranscoder` implements `ISchemaTranscoder`. The `Utf8JsonWriter` overload is the primitive, and the string
and node forms are extensions built on it. That overload writes into your buffer and does not flush. You own the
writer.

A payload that disagrees with the schema raises `InvalidDataException`.

### Transcode from a parse you hold

`AvroJsonWriter` is the type the transcoder caches. Construct one directly to own the parse yourself:

```csharp
var jsonWriter = new AvroJsonWriter(File.ReadAllText("Schemas/Shipment.avsc"));

string json = jsonWriter.Write(payload);
jsonWriter.Write(payload, writer);   // into a Utf8JsonWriter you own
```

The constructor parses the schema, and raises `ArgumentException` if the text is not valid Avro. Each `Write` call then
costs only the transcode. An instance holds the parsed schema and nothing else, so several threads can share one.

### What a call costs

A parse costs more than an export or a transcode does. The three entry points pay for it differently.

| Call                          | Parses the schema                                         |
|-------------------------------|-----------------------------------------------------------|
| `AvroJsonSchema.Export`       | On every call. Export once and keep the document yourself |
| `AvroSchemaTranscoder.ToJson` | Once for each `SchemaSource` instance                     |
| `AvroJsonWriter.Write`        | Never. The constructor parses                             |

The transcoder keys its cache on the `SchemaSource` object and not on the schema text. Two sources built from the same
text are two keys.

Hold one source and reuse it, and you pay for the parse once. Build a fresh source for each record, and you pay every
time. The cache holds the source weakly, so a transcoded source stays collectable.

`Export` caches nothing. It reads the text twice on each call: once into the schema model, and once as a raw tree for
the facts the model does not keep.

### What it emits

This schema:

```json
{
  "type": "record",
  "name": "Shipment",
  "namespace": "Acme.Orders",
  "doc": "An order left the warehouse.",
  "fields": [
    { "name": "orderId", "type": "string" },
    { "name": "total",   "type": { "type": "bytes", "logicalType": "decimal", "precision": 12, "scale": 2 } },
    { "name": "status",  "type": { "type": "enum", "name": "Status", "symbols": ["Pending", "Shipped"] } },
    { "name": "note",    "type": ["null", "string"], "default": null }
  ]
}
```

exports as this document:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$ref": "#/$defs/Acme.Orders.Shipment",
  "$defs": {
    "Acme.Orders.Shipment": {
      "type": "object",
      "properties": {
        "orderId": { "type": "string" },
        "total": { "type": "string", "pattern": "^[\\x00-\\xFF]*$" },
        "status": { "$ref": "#/$defs/Acme.Orders.Status" },
        "note": {
          "oneOf": [
            { "type": "null" },
            {
              "type": "object",
              "properties": { "string": { "type": "string" } },
              "required": ["string"],
              "additionalProperties": false
            }
          ],
          "default": null
        }
      },
      "required": ["orderId", "total", "status"],
      "additionalProperties": false,
      "description": "An order left the warehouse."
    },
    "Acme.Orders.Status": { "type": "string", "enum": ["Pending", "Shipped"] }
  }
}
```

and a payload of that schema transcodes to this JSON, which the document above accepts:

```json
{"orderId":"ORD-1","total":"\u0000\u0002\u00FF","status":"Shipped","note":{"string":"gift wrap"}}
```

`total` is three characters, one for each byte of the unscaled decimal. `Utf8JsonWriter` escapes them, and a validator
reads the escaped and the unescaped form as the same string.

### The mapping

| Avro      | JSON Schema                                                                                      |
|-----------|--------------------------------------------------------------------------------------------------|
| `null`    | `"type": "null"`                                                                                 |
| `boolean` | `"type": "boolean"`                                                                              |
| `int`     | `"type": "integer"`, with `minimum` and `maximum` at the int32 limits                            |
| `long`    | `"type": "integer"`, with `minimum` and `maximum` at the int64 limits                            |
| `float`   | `"type": "number"`                                                                               |
| `double`  | `"type": "number"`                                                                               |
| `bytes`   | `"type": "string"`, with the byte string `pattern`                                               |
| `string`  | `"type": "string"`                                                                               |
| `record`  | a `$defs` entry: `"type": "object"` with `properties`, `required`, `additionalProperties: false` |
| `enum`    | a `$defs` entry: `"type": "string"` with `enum` for the symbols, in declaration order            |
| `fixed`   | a `$defs` entry: `"type": "string"` with `minLength`, `maxLength` and the byte string `pattern`  |
| `array`   | `"type": "array"` with `items`                                                                   |
| `map`     | `"type": "object"` with `additionalProperties`                                                   |
| `union`   | `oneOf`, one entry for each branch                                                               |

A map writes no `propertyNames`. The keys of a JSON object are strings by definition, so an assertion there asserts
nothing.

### Named types

Every record, enum and fixed becomes a `$defs` entry keyed by its Avro fullname, and the point of use holds a `$ref` to
it. A record root makes the root of the document such a `$ref`. A recursive record needs this, because you cannot
inline a type that contains itself.

### Unions

Every union becomes `oneOf`, and the nullable `["null", X]` form as well. Each branch describes what the Avro JSON
encoding writes, and that is not the branch value.

The encoding writes the null branch as a bare JSON `null`. It wraps each other branch in an object whose single
property is the name of the branch type. The exported schema therefore describes a `oneOf` of tagged wrapper objects:

```json
"note": {
  "oneOf": [
    { "type": "null" },
    {
      "type": "object",
      "properties": { "string": { "type": "string" } },
      "required": ["string"],
      "additionalProperties": false
    }
  ]
}
```

The wrapper is load-bearing. The string branch of that union transcodes to `{"note":{"string":"gift wrap"}}`, and a
schema that described the branch values directly rejects that document. A schema its own transcoder fails is a wrong
schema and not a strict one. The exporter and the transcoder read the tag from the same code, so the two cannot drift.

A named branch carries its fullname as the tag. Each other branch carries its Avro type name.

This is standard JSON Schema. Any validator reads it, and none of it is annotation.

### Bytes and fixed

The Avro JSON encoding writes one Unicode code point for each byte, so `0xFF` is U+00FF. It is not base64. The exported
schema asserts this with a `pattern`:

```json
{ "type": "string", "pattern": "^[\\x00-\\xFF]*$" }
```

A `fixed` adds `minLength` and `maxLength`, both equal to the declared size. One character for each byte means that the
two keywords recover the byte count exactly.

The schema uses `pattern` because `pattern` is an assertion in every draft. `contentEncoding` is not an assertion, and
2020-12 disables `format` by default.

### Fields, defaults and documentation

`required` holds the fields with no declared default. Every field is present on the Avro wire, but a field with a
default is legitimately absent from a JSON document.

The export carries a field `default` verbatim. Under the Avro JSON encoding the two representations are the same one,
so a `bytes` default stays the byte string that Avro wrote.

A `doc` on a record, an enum, a fixed or a field becomes `description`. An enum's own `default` becomes the standard
`default` keyword, because an enum default is one of its symbols.

### What the document does not carry

The document describes what the transcoder writes. An Avro fact that the Avro JSON encoding does not write has no
keyword to hold it, so the export drops it:

| Avro fact                       | Why the document cannot hold it                                                       |
|---------------------------------|---------------------------------------------------------------------------------------|
| `logicalType`                   | The encoding writes the base type. A `date` is a plain integer day count              |
| `float` apart from `double`     | JSON Schema has one numeric type. Both become `number`                                |
| field position                  | The properties of a JSON object have no order. The Avro wire is ordered by it         |
| `aliases`                       | No keyword holds them                                                                 |
| `order`                         | No keyword holds it                                                                   |
| custom properties               | No keyword holds them                                                                 |
| decimal `precision` and `scale` | The encoding writes the unscaled value as bytes, and neither number is in those bytes |

The document is therefore a view of the schema, in one direction. You cannot rebuild the Avro schema from it.

## What this codec does not support

### Other schema file formats

`.avsc` only. Wire nothing else into `AdditionalFiles`.

`.avpr` (JSON protocol) and `.avdl` (IDL) describe RPC protocols. Support for them is a separate piece of work.

### Schema resolution and evolution

The reader schema always equals the writer schema. The codec decodes with the same schema it encoded with, and
implements none of Avro's resolution rules: no field reordering, no defaults for fields the writer omitted, no numeric
promotion.

`aliases`, a field's `order`, and an enum's `default` therefore influence nothing here. With Apache interop on, all
three survive verbatim in the schema text a generated type exposes, so an Apache-based reader still sees them.

### Generic mode

There is no `GenericRecord` path. Every message the codec encodes or decodes needs a static CLR type, and that is what
keeps the codec free of runtime type resolution, and therefore AOT-safe.

The three types under [Schema conversion](#schema-conversion) do read a schema at run time, and none of them needs a CLR
type. They read a payload, or describe one, without the message type in the process.

### Logical types nobody implements in .NET

These parse and round-trip correctly, and degrade to their base type exactly as Apache.Avro does. You get the underlying
value and not a richer one.

| Logical type            | Base        | What you get    | Why                                                                                                                           |
|-------------------------|-------------|-----------------|-------------------------------------------------------------------------------------------------------------------------------|
| `big-decimal`           | `bytes`     | `byte[]`        | No C# implementation exists to check an encoding against                                                                      |
| `duration`              | `fixed(12)` | the fixed class | `TimeSpan` cannot hold a month, so months plus days plus milliseconds has no BCL type. The NodaTime model maps it to `Period` |
| `local-timestamp-nanos` | `long`      | `long`          | Apache implements neither nanos variant, so there is no reference behaviour to match                                          |
| any unrecognised name   | any         | the base type   | The specification requires implementations to ignore unknown logical types                                                    |

`uuid` on a `fixed` of any size other than 16 also degrades, which matches Java.

### Invalid schemas

The generator does not validate Avro names, enum symbol uniqueness, or union constraints. A schema that breaks those
rules reaches the C# compiler as an error and not as a diagnostic. With Apache interop on, Apache.Avro 1.12.2 validates
all three the first time anything reads a generated type's `Schema` property.

## Interoperability with Apache.Avro

This section applies with `KurrentAvroApacheInterop` set to `On`. Off, a generated type carries no Apache surface, so
neither limitation below can reach you.

Interop-On generated code references Apache types, and `Kurrent.Surge.Core` does not depend on Apache.Avro — **a
project that turns interop on must reference the [Apache.Avro](https://www.nuget.org/packages/Apache.Avro) package
itself**.

Two upstream limitations surface as exceptions and not as wrong data.

**Apache's Specific writer cannot encode a logical type backed by a fixed.** Its conversion produces a
`GenericFixed`, and `SpecificDatumWriter.WriteFixed` accepts only a `SpecificFixed`. This affects `decimal` on `fixed`
and `uuid` on `fixed`. Use `GenericDatumWriter` for those, or this codec, which writes the bytes directly
and is unaffected.

**A known logical type on an invalid base throws in Apache's parser.** `{"type":"long","logicalType":"date"}`
declares `date`, which the specification defines on `int`. The codec erases the annotation and uses the base type,
as the specification instructs and as Java does. Apache.Avro throws instead, so reading `Schema` on such a type raises
`AvroTypeException` even though the codec itself works.

## Diagnostics

| Id         | Severity | Meaning                                                                                      |
|------------|----------|----------------------------------------------------------------------------------------------|
| `KAVRO001` | Error    | The file is not valid JSON, or not a valid Avro schema. Reported at the offending offset     |
| `KAVRO002` | Warning  | The root schema is not a record, so no codec is generated                                    |
| `KAVRO003` | Error    | Two schemas declare the same type with different shapes. A type may have only one definition |
| `KAVRO004` | Warning  | A `KurrentAvro*` property holds a value the generator does not recognise. The default holds  |

The generator deduplicates two schemas that declare the same type identically, and reports nothing: sharing a nested
record between files is ordinary, and generates the same C# either way.

## Credits

The generator's Avro name handling and the shape of its emitted types derive from
[AvroSourceGenerator](https://github.com/sadicangel/AvroSourceGenerator) (MIT, Copyright (c) 2024 sadicangel).

## Links

- [Documentation](https://kurrent.io/docs)
- [Apache Avro specification](https://avro.apache.org/docs/1.12.0/specification/)
- [Apache.Avro (C#) source](https://github.com/apache/avro/tree/main/lang/csharp)
- [GitHub Repository](https://github.com/kurrent-io/kurrent-surge)
- [License (Apache 2.0)](https://github.com/kurrent-io/kurrent-surge/blob/main/LICENSE.md)
