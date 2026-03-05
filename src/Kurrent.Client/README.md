# Kurrent.Client

The official .NET client for [KurrentDB](https://kurrent.io) — a cloud-native event streaming database.

## Installation

```shell
dotnet add package Kurrent.Client
```

### Supported Frameworks

- .NET 8.0
- .NET 9.0
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

- **Event Streams** — append, read, subscribe, and manage streams
- **Schema Registry** — type-safe serialization with automatic schema management
- **Filter Expressions** — server-side SQL filtering for subscriptions and reads
- **Serialization** — built-in JSON and Protobuf serializers with auto-registration
- **OpenTelemetry** — distributed tracing and metrics out of the box
- **Dependency Injection** — first-class `IServiceCollection` integration
- **AOT Compatible** — source generators for zero-reflection operation
- **Result Pattern** — type-safe error handling without exceptions

## Links

- [Documentation](https://kurrent.io/docs)
- [Examples](https://github.com/kurrent-io/Kurrent.Client/tree/main/examples)
- [GitHub Repository](https://github.com/kurrent-io/Kurrent.Client)
- [License (Apache 2.0)](https://github.com/kurrent-io/Kurrent.Client/blob/main/LICENSE.md)
