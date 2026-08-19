# Kurrent.Surge.Codecs.MessagePack

A [MessagePack](https://msgpack.org/) schema serializer for the [Kurrent](https://kurrent.io) .NET SDK, backed
by the official [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp) library.

## What this package does

Kurrent Surge's message pipeline serializes and deserializes your message types through a pluggable
`ISchemaCodec`. `Kurrent.Surge.Codecs.MessagePack` adds MessagePack as one of those formats, alongside the SDK's
built-in JSON and Protobuf support — same registration model, same message catalog, same read/write path.
Install it, register your message types, and MessagePack becomes just another format choice for your streams.

## Why MessagePack

A compact binary format with a much smaller wire footprint than JSON, without Avro's schema-file ceremony —
your existing C# classes are the contract, just decorated with `[MessagePackObject]`.

## Installation

```shell
dotnet add package Kurrent.Surge.Codecs.MessagePack
```

### Supported Frameworks

- .NET 10.0

## The one thing to get right: resolvers

This serializer never uses MessagePack-CSharp's `StandardResolver` or `ContractlessStandardResolver` — both
silently fall back to `System.Reflection.Emit` dynamic codegen for any `[MessagePackObject]` type without a
formatter, which breaks Native AOT and trimming. A type with no formatter anywhere in the configured resolver
chain throws `MessagePack.FormatterNotRegisteredException` instead of reflecting.

Because Kurrent's message catalog lets you register arbitrary POCOs at runtime (`MessageCatalog.Map<T>()` —
types this package never sees at its own compile time), it cannot ship a fixed resolver that covers them. Run
the MessagePack source generator (the `MessagePackAnalyzer`/`mpc` tool) in your own project and supply the
resolver it generates.

## Quick start

```csharp
using Kurrent.Surge.Codecs.MessagePack;

var client = KurrentClient.Create(connectionString, opts => {
    opts.Messages.UseMessagePack(new MessagePackSchemaCodecOptions {
        UserResolvers = [GeneratedResolver.Instance] // the [GeneratedMessagePackResolver] partial class your project's build emits
    });
    opts.Messages.MapMessage<YourApp.Messages.OrderPlaced>();
});
```

`UserResolvers` is checked ahead of the built-in, reflection-free resolvers for primitives and common BCL types
(`DateTime`, `Guid`, `decimal`). Without a `UserResolvers` entry, only those built-ins serialize — every
consumer message type throws until you supply the generated resolver. `UseMessagePack()` also has a
parameterless overload defaulting to `MessagePackSchemaCodecOptions.Default`, but a real setup almost
always needs `UserResolvers` set.

## Links

- [Documentation](https://kurrent.io/docs)
- [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp)
- [GitHub Repository](https://github.com/kurrent-io/Kurrent.Client)
- [License (Apache 2.0)](https://github.com/kurrent-io/Kurrent.Client/blob/main/LICENSE.md)
