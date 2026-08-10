# Kurrent.Surge.MessagePack.Nerdbank

A MessagePack `ISchemaCodec` for the [Kurrent](https://kurrent.io) .NET SDK, backed by
[Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack).

## What this package does

Kurrent Surge's message pipeline serializes and deserializes your message types through a pluggable
`ISchemaCodec`. `Kurrent.Surge.MessagePack.Nerdbank` adds MessagePack as one of those formats, alongside
the SDK's built-in JSON and Protobuf support — same registration model, same message catalog, same read/write
path. Install it, register your message types, and MessagePack becomes just another format choice for your
streams.

## Why Nerdbank over classic MessagePack-CSharp

This SDK targets Native AOT with no reflection fallback. Nerdbank.MessagePack is the AOT- and trim-safe
successor to classic MessagePack-CSharp: it resolves every type through a
[PolyType](https://github.com/eiriktsarpalis/PolyType) type shape generated at compile time, with no
`StandardResolver`-style reflection path to fall back to. If you need classic MessagePack-CSharp instead (e.g.
for compatibility with an existing resolver), use
[`Kurrent.Surge.MessagePack`](https://www.nuget.org/packages/Kurrent.Surge.MessagePack).

## Installation

```shell
dotnet add package Kurrent.Surge.MessagePack.Nerdbank
```

### Supported Frameworks

- .NET 10.0

## Quick start

The SDK's message catalog lets consumers register arbitrary POCOs as message types at runtime — types this
package has never seen at its own compile time. PolyType solves this with a **witness class**: a partial class
carrying one `[GenerateShapeFor<T>]` attribute per message type, which the PolyType source generator turns
into a static `ITypeShapeProvider`.

```csharp
using PolyType;
using Kurrent.Surge.Client.Schema.Serialization.MessagePack;

// One witness class per assembly, listing every message type it needs to serialize.
[GenerateShapeFor<OrderPlaced>]
[GenerateShapeFor<OrderShipped>]
partial class MessageWitness;

var client = KurrentClient.Create(connectionString, opts => {
    opts.Messages.UseMessagePackNerdbank(new MessagePackSchemaSerializerOptions {
        TypeShapeProvider = MessageWitness.GeneratedTypeShapeProvider
    });
    opts.Messages.MapMessage<OrderPlaced>();
});
```

There is no default `TypeShapeProvider`, and `UseMessagePackNerdbank()` has no parameterless overload —
Nerdbank.MessagePack has no reflection fallback, so an unset provider would silently break Native AOT rather
than degrade gracefully. Supplying it is mandatory, enforced at compile time.

## Links

- [Nerdbank.MessagePack documentation](https://aarnott.github.io/Nerdbank.MessagePack/)
- [Kurrent Documentation](https://kurrent.io/docs)
- [GitHub Repository](https://github.com/kurrent-io/Kurrent.Client)
- [License (Apache 2.0)](https://github.com/kurrent-io/Kurrent.Client/blob/main/LICENSE.md)
