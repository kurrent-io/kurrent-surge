# Project Overview

**Generated:** 2026-01-16
**Project:** Kurrent.Client
**Type:** .NET Client Library for KurrentDB

---

## What is Kurrent.Client?

Kurrent.Client is the official .NET client library for [KurrentDB](https://kurrent.io), an event-native database engineered for modern software applications and event-driven architectures.

### Key Capabilities

- **Stream Operations** - Read, append, subscribe to event streams
- **Schema Registry** - Type-safe serialization with automatic schema management
- **Filter Expressions** - SQL-like queries for stream filtering
- **Resilience** - Built-in retry policies and circuit breakers
- **Observability** - OpenTelemetry integration for tracing and metrics

---

## V2 Client Highlights

The V2 client introduces significant improvements over the V1 EventStoreDB client:

| Feature | Description |
|---------|-------------|
| **Automatic Serde** | JSON/Protobuf serialization with schema registry integration |
| **Result Pattern** | Functional error handling (no exceptions for expected failures) |
| **Filter Expressions** | DuckDB-compatible SQL-like filtering |
| **Resilience Profiles** | Pre-built retry/timeout configurations |
| **Record Model** | Unified "Record" concept for all message types |
| **AOT Support** | Source generators for ahead-of-time compilation |

---

## Quick Start

### Installation

```bash
dotnet add package KurrentDB.Client
```

### Basic Usage

```csharp
// Create client
await using var client = new KurrentClient("kurrentdb://localhost:2113");

// Append records
var result = await client.Streams.AppendAsync(
    "orders",
    new OrderPlaced { OrderId = "123", Amount = 99.99m }
);

// Read records
await foreach (var message in await client.Streams.ReadRecords(new ReadStreamOptions {
    Stream = "orders",
    Direction = ReadDirection.Forwards
})) {
    message.Switch(
        record => Console.WriteLine($"Event: {record.SchemaName}"),
        heartbeat => { }
    );
}
```

---

## Repository Structure

```
Kurrent.Client/
├── src/                    # Source code
│   ├── KurrentDB.Client/   # Main client library
│   ├── Variant/            # Result types library
│   └── ...                 # Supporting projects
├── test/                   # Test projects
├── examples/               # Example applications
├── proto/                  # Protocol Buffer definitions
└── .project/               # Documentation & specifications
```

---

## Technology Stack

- **Language:** C# 14
- **Frameworks:** .NET 8.0, 9.0, 10.0
- **Communication:** gRPC, Protocol Buffers
- **Testing:** TUnit (not xUnit/NUnit)

---

## Documentation Index

- [Architecture](./architecture.md) - System design and patterns
- [Source Tree Analysis](./source-tree-analysis.md) - Code organization
- [Development Guide](./development-guide.md) - Build, test, contribute

---

## Links

- **GitHub:** https://github.com/kurrent-io/Kurrent.Client
- **KurrentDB:** https://kurrent.io
- **Documentation:** https://docs.kurrent.io
