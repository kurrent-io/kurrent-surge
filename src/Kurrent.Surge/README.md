# Kurrent.Surge

The .NET client for [KurrentDB](https://kurrent.io), a cloud-native event streaming database.

## Installation

```shell
dotnet add package Kurrent.Surge
```

### Supported frameworks

- .NET 10.0 or a subsequent version

## Quick start

Declare a catalog for your message types. The client reads it to map each CLR type to a schema name.

```csharp
using Kurrent.Surge.Client;
using Kurrent.Surge.Schema.Catalogs;

[assembly: MessageCatalog("Orders.Events", AlwaysActive = true)]

await using var client = KurrentClient.Create("kurrentdb://localhost:2113?tls=false");

// Append a record to a stream.
var records = new[] { AppendRecord.Create(new OrderPlaced("order-123")) };
var result  = await client.Streams.AppendStream("orders-123", records, ExpectedStreamState.NoStream);

Console.WriteLine($"The append wrote revision {result.Revision}.");

// Read the stream back.
await foreach (var message in client.Streams.ReadStream("orders-123")) {
    if (message.Value is Record record)
        Console.WriteLine(record.Value);
}

namespace Orders.Events {
    public record OrderPlaced(string OrderId);
}
```

## Connection strings

```
kurrentdb://localhost:2113
kurrentdb://admin:changeit@localhost:2113
kurrentdb+discover://node1:2113,node2:2113,node3:2113
```

## Features

- **Streams** — append, read, subscribe to, and manage streams
- **Schema registry** — register a schema, get it, and check its compatibility
- **Indexes and connectors** — create, start, stop, and delete them
- **Filter expressions** — server-side SQL filtering for subscriptions and reads
- **Codecs** — JSON and protobuf are built in. Avro is an opt-in package
- **Stream processing** — processors and projectors with checkpointing and partitioning
- **OpenTelemetry** — distributed tracing and metrics
- **Dependency injection** — first-class `IServiceCollection` integration
- **AOT compatible** — source generators give operation with no reflection

## Message Formats

| Format | Package | Encode and decode | Schema for the registry | Transcode to JSON |
|---|---|---|---|---|
| JSON | built in | Yes | From the CLR type | Not necessary. The payload is JSON |
| Protobuf | built in | Yes | From the CLR type, or from `.proto` text | Yes, from `.proto` text |
| Avro | `Kurrent.Surge.Codecs.Avro` | Yes | From the CLR type, or from `.avsc` text | Yes, from `.avsc` text |
| Bytes | built in | Passthrough | None | No |

Each format has a schema, and the registry validates a payload against it. The SDK makes that schema
from the CLR type, so each format in the table has one.

Transcoding is different. It reads a payload and writes JSON, and it uses no .NET type. It must read
the schema as text.

Protobuf has a `.proto` file and Avro has an `.avsc` file. A format with no schema language has no
text to read, so it cannot transcode.

Transcoding is more useful in a server than in a client. A server reads a stored payload that its
process has no type for. A client has the type, and the codec gives it the value.

## Links

- [Documentation](https://kurrent.io/docs)
- [Examples](https://github.com/kurrent-io/kurrent-sdk-dotnet/tree/main/examples)
- [GitHub Repository](https://github.com/kurrent-io/kurrent-sdk-dotnet)
- [License (Apache 2.0)](https://github.com/kurrent-io/kurrent-sdk-dotnet/blob/main/LICENSE.md)
