# Kurrent.Surge.Codecs.Avro

An [Apache Avro](https://avro.apache.org/) schema serializer for the [Kurrent](https://kurrent.io) .NET SDK,
backed by the official [Apache.Avro](https://github.com/apache/avro/tree/main/lang/csharp) library.

## What this package does

Kurrent Surge's message pipeline serializes and deserializes your message types through a pluggable
`ISchemaCodec`. `Kurrent.Surge.Codecs.Avro` adds Avro as one of those formats, alongside the SDK's built-in JSON
and Protobuf support — same registration model, same message catalog, same read/write path. Install it,
register your message types, and Avro becomes just another format choice for your streams.

## Why Avro

A compact binary wire format with a real schema behind every message — useful when you want strict schema
evolution rules, a smaller payload than JSON, or interoperability with the wider Avro/Kafka ecosystem. Unlike
this SDK's MessagePack packages, Avro is schema-first by design: your message type's shape comes from a
schema definition, not from reflecting over a plain class.

## Installation

```shell
dotnet add package Kurrent.Surge.Codecs.Avro
```

### Supported Frameworks

- .NET 10.0

## Not Native AOT compatible

This package is deliberately not AOT- or trim-compatible, and does not set `IsAotCompatible`/`IsTrimmable`.
Apache.Avro's Specific-mode reader/writer pipeline (`Avro.Specific.SpecificDatumReader<T>`/`SpecificDatumWriter<T>`)
and its `ISpecificRecord.Get`/`Put` dispatch are reflection-bound in the current release, and there is no
AOT-safe Avro library for .NET today. Choosing this package is an informed tradeoff: if Native AOT is a hard
requirement, use [`Kurrent.Surge.Codecs.NerdbankMessagePack`](https://www.nuget.org/packages/Kurrent.Surge.Codecs.NerdbankMessagePack)
or the SDK's Protobuf support instead.

## How it works

This serializer runs in Avro's **Specific mode**: your message type is a class generated from a schema
definition, implementing `Avro.Specific.ISpecificRecord`. The generated class carries its own schema — this
package never derives, infers, or hand-parses one, and `AvroSchemaCodecOptions` takes no schema input at
all.

## Quick start

1. Add a schema file anywhere in your project — any folder name, your choice:

   ```json
   // Schemas/OrderPlaced.avsc
   {
     "type": "record",
     "name": "OrderPlaced",
     "namespace": "YourApp.Messages",
     "fields": [
       { "name": "OrderId", "type": "string" },
       { "name": "Amount", "type": "double" }
     ]
   }
   ```

2. Point your project at it — this is the one manual step, since nothing can know your folder layout for you:

   ```xml
   <ItemGroup>
     <AdditionalFiles Include="Schemas/*.avsc" />
   </ItemGroup>
   ```

3. Build. `YourApp.Messages.OrderPlaced` now exists as a class implementing `ISpecificRecord` — no CLI to run,
   nothing to keep in sync by hand. Installing `Kurrent.Surge.Codecs.Avro` is the only package reference needed; the
   build-time generator ships with it.

4. Register and use it exactly like any other message type:

   ```csharp
   using Kurrent.Surge.Codecs.Avro;

   var client = KurrentClient.Create(connectionString, opts => {
       opts.Messages.UseAvro();
       opts.Messages.MapMessage<YourApp.Messages.OrderPlaced>();
   });
   ```

## Configuration

**Only `.avsc` (JSON schema) works today — `.avpr`/`.avdl` are not yet released.** The generator's own
documentation also lists `.avpr` (JSON protocol) and `.avdl` (IDL) as supported input. Confirmed real, not
vaporware: built directly against the generator's own repository source (commit `4d838d1`, 2026-08-01) and
both formats generate the correct `ISpecificRecord` shape — but that support has not shipped to NuGet in any
form yet, not in the current stable release, not in the latest preview. Two syntax requirements the upstream
README doesn't mention, confirmed by reading the generator's own parser source directly:

- **`.avpr` needs a `messages` block.** A `types`-only protocol file fails schema validation — at least one
  RPC message must be defined, even if your use case has no real RPC surface.
- **`.avdl` needs to be wrapped in `protocol { }`.** A bare top-level `record` declaration (what the
  upstream README's own example shows) is not sufficient — the parser requires either an explicit "main
  schema directive" or exactly one `protocol` declaration in the file.

Tracked as a follow-up: wire `.avpr`/`.avdl` into this package's test project once this ships to NuGet. Until
then, `AdditionalFiles` globs for them are deliberately not wired in — use `.avsc` only.

A handful of MSBuild properties tune the generator's output — set these in your own project's `.csproj`, they
apply whether or not you reference the generator directly (installing `Kurrent.Surge.Codecs.Avro` is enough,
confirmed by direct testing):

| Property | Values | Default | Purpose |
|---|---|---|---|
| `AvroSourceGeneratorAccessModifier` | `public`, `internal` | `public` | Visibility of generated types |
| `AvroSourceGeneratorRecordDeclaration` | `record`, `class` | `record` | Generated type kind |
| `AvroSourceGeneratorDuplicateResolution` | `Error`, `Ignore` | `Error` | Behavior when two schema inputs would generate the same type |
| `AvroSourceGeneratorReferenceResolution` | `Strict`, `Deferred` | `Strict` | Whether cross-schema references must resolve immediately or after all inputs are read |

```xml
<PropertyGroup>
  <AvroSourceGeneratorAccessModifier>internal</AvroSourceGeneratorAccessModifier>
</PropertyGroup>
```

## Links

- [Documentation](https://kurrent.io/docs)
- [Apache Avro](https://avro.apache.org/)
- [Apache.Avro (C#) source](https://github.com/apache/avro/tree/main/lang/csharp)
- [GitHub Repository](https://github.com/kurrent-io/Kurrent.Client)
- [License (Apache 2.0)](https://github.com/kurrent-io/Kurrent.Client/blob/main/LICENSE.md)
