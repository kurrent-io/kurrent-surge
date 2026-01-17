# Source Tree Analysis

**Generated:** 2026-01-16
**Project:** Kurrent.Client
**Type:** Monolith Library

---

## Project Structure Overview

```
Kurrent.Client/
├── src/                          # Source projects
│   ├── KurrentDB.Client/         # Main client library (CORE)
│   ├── Kurrent.Processors/       # Record processors (separate package)
│   ├── Kurrent.Client.SourceGenerators/  # AOT-compatible code generation
│   ├── Kurrent.Client.Schema.Protobuf/   # Protobuf schema serializer
│   ├── Kurrent.Grpc.LoadBalancing/       # Cluster load balancing
│   ├── Variant/                  # Result types & discriminated unions
│   └── Variant.SourceGenerators/ # Variant code generation
├── test/                         # Test projects
│   ├── Kurrent.Client.Tests/     # Unit tests
│   ├── Kurrent.Client.Integration.Tests/ # Integration tests
│   ├── Kurrent.Client.Benchmarks/        # Performance benchmarks
│   ├── Kurrent.Testing/          # Test infrastructure
│   └── Variant.Tests/            # Variant library tests
├── examples/                     # Example applications
├── proto/                        # Protocol Buffer definitions
├── .project/                     # Project documentation & specs
└── scripts/                      # Build/utility scripts
```

---

## Critical Directories

### src/KurrentDB.Client/ (Main Client Library)

```
KurrentDB.Client/
├── Kurrent/                      # Infrastructure layer
│   ├── DuckDb/                   # DuckDB integration (filter validation)
│   ├── GoogleRpc/                # Google RPC status handling
│   ├── Grpc/                     # gRPC utilities
│   ├── Http/                     # HTTP utilities
│   ├── Options/                  # Options base classes
│   ├── Talyn/                    # Internal utilities
│   └── Validation/               # Validation framework
│
├── Kurrent.Client/               # Public API layer
│   ├── KurrentClient.cs          # ENTRY POINT - Main client class
│   ├── KurrentConnectionString.cs # Connection string parsing
│   ├── Errors/                   # Error handling infrastructure
│   ├── Internal/                 # Internal utilities
│   ├── Modules/                  # Feature modules (see below)
│   ├── Options/                  # Client configuration
│   │   ├── KurrentClientOptions.cs
│   │   └── KurrentClientResilienceOptions.cs  # Resilience profiles
│   └── Schema/                   # Schema management
│       ├── Serialization/        # JSON/Protobuf/Bytes serializers
│       └── MessageTypeMapper.cs  # CLR type to event type mapping
│
├── Legacy/                       # EventStoreDB compatibility
│   ├── RipAndTear/               # Legacy code being phased out
│   └── SystemStreams.cs
│
└── Resources/                    # Embedded resources
    └── ErrorMessages.resx
```

### Feature Modules (Kurrent.Client/Modules/)

```
Modules/
├── Streams/                      # V2 ACTIVE - Stream operations
│   ├── StreamsClient.cs          # Main streams client
│   ├── Operations/               # Operation-based organization
│   │   ├── Append/               # Append operations
│   │   ├── Read/                 # Read operations (ReadRecords, ReadBatches)
│   │   ├── Manage/               # Stream management
│   │   └── Subscribe/            # Subscriptions
│   ├── Models/                   # Domain models (Record, etc.)
│   └── Checkpoints/              # Position tracking
│
├── Registry/                     # V2 ACTIVE - Schema registry
│   ├── RegistryClient.cs
│   └── Model/
│
├── Projections/                  # PENDING REFACTOR
├── PersistentSubscriptions/      # PENDING REFACTOR
├── Users/                        # PENDING REFACTOR
├── Connectors/                   # PENDING REFACTOR
└── Admin/                        # PENDING REFACTOR
```

### src/Variant/ (Result Types Library)

```
Variant/
├── IVariant.cs                   # Discriminated union interface
├── IVariantException.cs          # Exception variant interface
└── Result/                       # Result monad implementation
    ├── Result.cs                 # Core Result<TValue, TError>
    ├── Result.Abstractions.cs
    ├── Result.Functional.*.cs    # Functional operations (Map, Bind, Match, etc.)
    └── Result.Try.cs             # Try/catch wrappers
```

### src/Kurrent.Grpc.LoadBalancing/ (Cluster Support)

```
Kurrent.Grpc.LoadBalancing/
├── Builder/                      # Fluent builders
├── Configuration/                # Load balancing options
├── Infrastructure/               # Core implementation
│   ├── ClusterLoadBalancer.cs
│   ├── ClusterResolver.cs
│   └── ClusterPicker.cs
├── Model/                        # Cluster topology models
├── Picking/                      # Node selection strategies
│   ├── LeaderOnlyStrategy.cs
│   ├── RoundRobinStrategy.cs
│   └── DefaultStrategy.cs
└── Sources/                      # Topology discovery
```

### src/Kurrent.Processors/ (Separate Package - EXCLUDED FROM CORE)

```
Kurrent.Processors/
├── KurrentProcessors.cs          # Main processor orchestrator
├── ProcessorWorker.cs            # Worker implementation
├── Checkpointing/                # Checkpoint management
├── Partitioning/                 # Partition strategies
├── State/                        # State management (DuckDB)
├── Gateway/                      # Server communication
├── Options/                      # Configuration
└── Diagnostics/                  # Metrics & tracing
```

---

## Test Infrastructure

```
test/
├── Kurrent.Client.Tests/         # Unit tests
├── Kurrent.Client.Integration.Tests/  # Integration tests with real server
├── Kurrent.Client.Benchmarks/    # BenchmarkDotNet performance tests
├── Kurrent.Testing/              # Shared test infrastructure
│   ├── Containers/               # FluentDocker container management
│   ├── Fixtures/                 # Test fixtures
│   └── docker-compose.yml        # Observability services (Seq, Aspire)
└── Variant.Tests/                # Variant library unit tests
```

---

## Entry Points

| Entry Point | Location | Purpose |
|-------------|----------|---------|
| `KurrentClient` | `Kurrent.Client/KurrentClient.cs` | Main client instantiation |
| `StreamsClient` | `Modules/Streams/StreamsClient.cs` | Stream operations |
| `RegistryClient` | `Modules/Registry/RegistryClient.cs` | Schema registry |
| `KurrentProcessors` | `Kurrent.Processors/KurrentProcessors.cs` | Record processing (separate package) |

---

## Key Files Reference

| Category | File | Purpose |
|----------|------|---------|
| Client Entry | `KurrentClient.cs` | Main client class |
| Options | `KurrentClientOptions.cs` | Client configuration |
| Resilience | `KurrentClientResilienceOptions.cs` | Retry/timeout profiles |
| Module Base | `ClientModuleBase.cs` | Module inheritance |
| Error Handling | `Errors/KurrentErrorDetails.cs` | Structured errors |
| Result Types | `Variant/Result/Result.cs` | Result monad |
| Discriminated Unions | `Variant/IVariant.cs` | Sum types |
| Connection | `KurrentConnectionString.cs` | URI parsing |
| Serialization | `Schema/Serialization/` | JSON/Protobuf serde |

---

## Build Configuration

| File | Purpose |
|------|---------|
| `Directory.Build.props` | Global build settings (frameworks, language version) |
| `Directory.Packages.props` | Central package management |
| `global.json` | .NET SDK version, test runner config |
| `Kurrent.Client.slnx` | Solution file |
