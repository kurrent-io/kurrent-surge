# Kurrent.Client

The official .NET client for [KurrentDB](https://kurrent.io) — a cloud-native event streaming database.

## Installation

```shell
dotnet add package Kurrent.Client
```

### Supported Frameworks

- .NET 10.0

## Quick Start

```csharp
using Kurrent.Client;

var client = new KurrentClient("kurrentdb://localhost:2113");

// Append events to a stream
await client.Streams.AppendAsync("orders-123", [
    new MessageData("OrderPlaced", orderData)
]);

// Read events from a stream
await foreach (var message in client.Streams.ReadAsync("orders-123")) {
    Console.WriteLine($"{message.EventType}: {message.Data}");
}
```

## Connection Strings

```
kurrentdb://localhost:2113
kurrentdb://admin:changeit@localhost:2113
kurrentdb+discover://node1:2113,node2:2113,node3:2113
```

## Features

- **Streams** — append, read, subscribe, and manage streams
- **Schema Registry** — type-safe serialization with automatic schema management
- **Filter Expressions** — server-side SQL filtering for subscriptions and reads
- **Serialization** — built-in JSON and Protobuf serializers with auto-registration
- **OpenTelemetry** — distributed tracing and metrics out of the box
- **Dependency Injection** — first-class `IServiceCollection` integration
- **AOT Compatible** — source generators for zero-reflection operation

## Message Formats

| Format | Package | Encode and decode | Schema for the registry | Transcode to JSON |
|---|---|---|---|---|
| JSON | built in | Yes | From the CLR type | Not necessary. The payload is JSON |
| Protobuf | built in | Yes | From the CLR type, or from `.proto` text | Yes, from `.proto` text |
| Avro | `Kurrent.Surge.Codecs.Avro` | Yes | From the CLR type, or from `.avsc` text | Yes, from `.avsc` text |
| MessagePack | `Kurrent.Surge.Codecs.MessagePack` | Yes | From the CLR type | No |
| MessagePack | `Kurrent.Surge.Codecs.NerdbankMessagePack` | Yes | From the CLR type | No |
| Bytes | built in | Passthrough | None | No |

Each format has a schema, and the registry validates a payload against it. The SDK makes that schema
from the CLR type, so each format in the table has one.

Transcoding is different. It reads a payload and writes JSON, and it uses no .NET type. It must read
the schema as text.

Protobuf has a `.proto` file and Avro has an `.avsc` file. MessagePack has no schema language, so it
has no text to read.

Transcoding is more useful in a server than in a client. A server reads a stored payload that its
process has no type for. A client has the type, and the codec gives it the value.

## Links

- [Documentation](https://kurrent.io/docs)
- [Examples](https://github.com/kurrent-io/Kurrent.Client/tree/main/examples)
- [GitHub Repository](https://github.com/kurrent-io/Kurrent.Client)
- [License (Apache 2.0)](https://github.com/kurrent-io/Kurrent.Client/blob/main/LICENSE.md)
