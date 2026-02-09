# Architecture Documentation

**Generated:** 2026-01-16
**Project:** Kurrent.Client
**Version:** V2

---

## Executive Summary

Kurrent.Client is an enterprise-grade .NET client library for KurrentDB event streaming database. It provides type-safe, high-performance access with sophisticated features including schema registry integration, automatic serialization, filter expressions, and advanced resilience patterns.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                     Client Application                          │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      KurrentClient                              │
│  (Entry Point - Orchestrates all modules)                       │
└─────────────────────────────────────────────────────────────────┘
                              │
        ┌─────────────────────┼─────────────────────┐
        ▼                     ▼                     ▼
┌───────────────┐   ┌─────────────────┐   ┌───────────────────┐
│ StreamsClient │   │ RegistryClient  │   │ [Other Modules]   │
│ (V2 Active)   │   │ (V2 Active)     │   │ (Pending Refactor)│
└───────────────┘   └─────────────────┘   └───────────────────┘
        │                     │
        └──────────┬──────────┘
                   ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Infrastructure Layer                          │
│  ┌──────────────┐  ┌───────────────┐  ┌──────────────────────┐  │
│  │ gRPC Client  │  │ Schema Serde  │  │ Error Handling       │  │
│  │ + Resilience │  │ (JSON/Proto)  │  │ (Result + Exceptions)│  │
│  └──────────────┘  └───────────────┘  └──────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                       KurrentDB Server                           │
└─────────────────────────────────────────────────────────────────┘
```

---

## Core Design Principles

### 1. Type-Safe Error Handling

No exception-based flow control. All operations return `Result<TValue, TError>`:

```csharp
public async ValueTask<Result<AppendSuccess, AppendError>> AppendAsync(...);

// Consumer pattern matching
result.Match(
    success => ProcessSuccess(success),
    error => HandleError(error)
);
```

### 2. Functional Programming Influence

- **Discriminated Unions** - `IVariant<T0, T1, ...>` for sum types
- **Result Monad** - Composable error handling
- **Immutability** - Options and models are immutable records

### 3. Zero Reflection / AOT Compatible

- **Source Generators** - Compile-time code generation
- **Registration-based Type Mapping** - No runtime reflection

### 4. Dual API Surface

- **Result Pattern** - Functional, explicit error handling
- **Idiomatic Extensions** - Familiar .NET exception-throwing API

---

## V2 Differentiators (vs V1 EventStoreDB Client)

| Feature | V1 (EventStoreDB) | V2 (KurrentDB) |
|---------|-------------------|----------------|
| **Error Handling** | Exceptions | Result pattern + Exceptions |
| **Serialization** | Manual | Automatic (JSON/Protobuf) |
| **Schema Registry** | None | First-class integration |
| **Filter Expressions** | Basic | SQL-like (DuckDB compatible) |
| **Resilience** | Manual Polly | Built-in profiles |
| **Ubiquitous Language** | Events | Records (all message types) |
| **AOT Support** | Limited | Full (source generators) |

---

## Module Architecture

### ClientModuleBase Pattern

All feature modules inherit from `ClientModuleBase`:

```csharp
public abstract class ClientModuleBase {
    protected ILogger Logger { get; }
    protected SchemaSerializerProvider SerializerProvider { get; }
    protected ServerCapabilities ServerCapabilities { get; }
    protected MessageTypeMapper TypeMapper { get; }
}
```

### Active Modules (V2)

| Module | Namespace | Purpose |
|--------|-----------|---------|
| StreamsClient | `Kurrent.Client.Streams` | Read, Append, Subscribe, Manage streams |
| RegistryClient | `Kurrent.Client.Registry` | Schema registration & validation |

### Pending Refactor

- ProjectionsClient
- PersistentSubscriptionsClient
- UsersClient
- ConnectorsClient
- AdminClient

---

## Schema & Serialization Architecture

```
CLR Object → MessageTypeMapper → SchemaManager → Serializer → Protocol Buffer
    │               │                   │              │
Event Type      Schema ID          Validation    Bytes + Metadata
```

### Automatic Serde Flow

1. **Type Mapping** - CLR type → event type name
2. **Schema Resolution** - Check registry or auto-register
3. **Serialization** - JSON/Protobuf based on schema
4. **Metadata** - Schema ID embedded in record

### Supported Formats

- **JSON** - `JsonSchemaSerializer`
- **Protobuf** - `ProtobufSchemaSerializer`
- **Bytes** - `BytesPassthroughSerializer` (no schema)

---

## Filter Expressions

SQL-like query language for filtering records (DuckDB compatible):

```sql
stream = 'orders' AND properties.amount > 100 AND timestamp >= '2024-01-01'
```

### Supported Columns

| Column | Type | Description |
|--------|------|-------------|
| `stream` | STRING | Stream name |
| `schema_name` | STRING | Event type |
| `timestamp` | DATETIME | Record timestamp |
| `position` | INT64 | Log position |
| `properties` | MAP | Custom metadata |

### Operators

- Comparison: `=`, `!=`, `<`, `>`, `<=`, `>=`
- Logical: `AND`, `OR`, `NOT`
- Pattern: `LIKE`, `ILIKE`
- Set: `IN`, `NOT IN`, `BETWEEN`
- Null: `IS NULL`, `IS NOT NULL`

---

## Resilience Architecture

### Built-in Profiles

| Profile | Deadline | Retries | Use Case |
|---------|----------|---------|----------|
| `Default` | Infinite | 3 | General purpose |
| `FailFast` | 5s | 0 | UI-facing operations |
| `RetryForever` | Infinite | Unlimited | Critical operations |
| `Subscription` | Infinite | 8 | Long-running streams |
| `HighAvailability` | 30s | 5 | Quick recovery |
| `CautiousWrite` | 60s | 3 | Non-idempotent writes |

### gRPC Resilience

- **Keepalive** - 60s ping interval, 30s timeout
- **Exponential Backoff** - Configurable multiplier
- **Retryable Status Codes** - Unavailable, Unknown, ResourceExhausted, Internal

---

## Communication Layer

### Connection String Formats

```bash
# Single node
kurrentdb://localhost:2113

# Cluster discovery
kurrentdb+discover://node1:2113,node2:2113,node3:2113

# With TLS
kurrents://localhost:2113?tls=true

# With credentials
kurrentdb://admin:changeit@localhost:2113
```

### Node Preference Strategies

- `Leader` - Route writes to leader
- `Follower` - Route reads to followers
- `Random` - Random node selection
- `ReadOnlyReplica` - Read replicas only

---

## Source Generator Integration

### KurrentOperationErrorGenerator

Generates exception classes from attributes:

```csharp
[KurrentOperationError(ServerError.StreamNotFound)]
public partial class StreamNotFoundException;
```

### Variant.SourceGenerators

Generates discriminated union implementations:

```csharp
public readonly partial record struct ReadMessage : IVariant<Record, Heartbeat>;
```

---

## Record Model (New Ubiquitous Language)

V2 uses "Record" as the universal message type, encompassing:

- Events
- Commands
- Snapshots
- System messages
- Heartbeats

```csharp
public record Record {
    public Guid RecordId { get; }
    public string Stream { get; }
    public string SchemaName { get; }
    public long Position { get; }
    public long StreamRevision { get; }
    public DateTimeOffset Timestamp { get; }
    public ReadOnlyMemory<byte> Data { get; }
    public Metadata Properties { get; }
}
```

---

## Technology Stack Summary

| Category | Technology | Version |
|----------|------------|---------|
| Language | C# | 14 (preview) |
| Runtime | .NET | 8.0, 9.0, 10.0 |
| Communication | gRPC | 2.76.0 |
| Serialization | Protobuf | 3.33.2 |
| Resilience | Polly + MS Resilience | 8.6.4 / 9.10.0 |
| Observability | OpenTelemetry | 1.13.1 |
| Validation | FluentValidation | 12.1.0 |
| Filter Engine | DuckDB.NET | 1.4.1 |
