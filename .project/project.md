# Kurrent .NET SDK

**Version:** v2.0.0
**Last Updated:** February 23, 2026

## What This Repo Is

This is `kurrent-sdk-dotnet`, the .NET monorepo for the Kurrent platform SDK.
Kurrent is a **platform**, not a database. There is no meta-package. Developers
install only what they need.

**Package layers:**

| Layer      | Package                | Status              | Depends On           |
|------------|------------------------|---------------------|----------------------|
| Client     | `Kurrent.Client`       | Active (near-final) | None                 |
| Processors | `Kurrent.Processors`   | Active              | `Kurrent.Client`     |
| Connectors | `Kurrent.Connectors.*` | Planned             | `Kurrent.Processors` |
| Framework  | `Kurrent.Framework.*`  | Future              | `Kurrent.Client`     |

`KurrentDB.Client` (v1) is legacy. `Kurrent.Client` (v2) is a clean break with
a new API surface, Result pattern throughout, and zero backward compatibility
with v1.

**Detailed guides:**
- [Kurrent.Client details](project-client.md)
- [Kurrent.Processors details](project-processors.md)
- [Variant library (Result + DU)](project-variant.md)

---

## Repository Tree

```
.
├── Kurrent.Client.slnx                 Solution (.slnx format, NOT .sln)
├── Directory.Build.props               Root build properties
├── Directory.Packages.props            Central Package Management versions
├── global.json                         SDK + test runner configuration
├── .editorconfig                       Code style rules
├── CLAUDE.md                           AI agent instructions
├── AGENTS.md                           AI agent instructions (Gemini/Copilot)
│
├── src/
│   ├── Kurrent.Client/                   Main client library (IsPackable)
│   ├── Kurrent.Client.SourceGenerators/  Roslyn generators (netstandard2.0)
│   ├── Kurrent.Processors/              Stream processing framework (IsPackable)
│   ├── Variant/                          Result types + discriminated unions (internal)
│   └── Variant.SourceGenerators/         Variant code generation (netstandard2.0)
│
├── test/
│   ├── Kurrent.Client.Tests/             Unit tests
│   ├── Kurrent.Client.Integration.Tests/ Integration tests (FluentDocker)
│   ├── Kurrent.Processors.Tests/         Processor unit tests
│   ├── Kurrent.Processors.Integration.Tests/
│   ├── Kurrent.Testing/                  Shared fixtures, containers, assertions
│   └── Variant.Tests/
│
├── perf/
│   └── Kurrent.Client.Benchmarks/      BenchmarkDotNet
│
├── examples/
│   ├── Transfermarkt.Domain/           Shared domain for examples
│   ├── client/                         Client usage examples (empty)
│   └── processors/                     10 processor examples
│       ├── Processor.AspNetMinimal/
│       ├── Processor.ClickstreamSessions/
│       ├── Processor.FraudDetection/
│       ├── Processor.FullExample/
│       ├── Processor.IoTMonitoring/
│       ├── Processor.Leaderboard/
│       ├── Processor.OrderEnrichment/
│       ├── Processor.WithPostgres/
│       ├── Processor.WithRedisCache/
│       └── Processor.WordCount/
│
├── proto/
│   ├── google/                         Standard Google protos (api, protobuf, rpc)
│   ├── kurrent/rpc/                    Custom Kurrent RPC error types
│   └── kurrentdb/protocol/
│       ├── v0/                         Reserved
│       ├── v1/                         EventStoreDB compatibility
│       │   ├── streams.proto           gossip, operations, persistentsubscriptions,
│       │   └── ...                     projections, serverfeatures, shared, users
│       └── v2/                         KurrentDB native protocol
│           ├── streams/                Stream operations
│           ├── registry/               Schema registry
│           ├── processors/             Processor management
│           └── indexes/                Index management
│
├── .github/
│   ├── workflows/
│   │   ├── build.yml                   Build + unit + integration tests
│   │   ├── claude.yml                  Claude Code GitHub integration
│   │   ├── claude-code-review.yml      Automated PR code review
│   │   └── create-release.yml          Manual release tag creation
│   ├── git-commit-instructions.md      Conventional Commits guide
│   └── PULL_REQUEST_TEMPLATE.md
│
└── scripts/
    ├── gencert.sh                      TLS cert generation for tests
    └── inspect_nuget.sh               NuGet package inspection
```

---

## Technology Stack

- C# 14 (`LangVersion: preview`), .NET 8.0/9.0/10.0 multi-target
- gRPC + Protocol Buffers (dual protocol: v1 legacy + v2 native)
- System.Text.Json (AOT-ready, no Newtonsoft)
- Polly + Microsoft.Extensions.Http.Resilience
- OpenTelemetry instrumentation
- Central Package Management (`Directory.Packages.props`)
- MinVer for git-tag versioning (prefix `v`, phase `alpha`)

---

## Code Style (NON-NEGOTIABLE)

| Rule        | Convention                                                                                        |
|-------------|---------------------------------------------------------------------------------------------------|
| Braces      | K&R (opening brace on same line)                                                                  |
| Fields      | `_camelCase`                                                                                      |
| Variables   | `var` everywhere                                                                                  |
| Async       | `ConfigureAwait(false)` on ALL awaits in library code                                             |
| Nullability | `<Nullable>enable</Nullable>`, no defensive null checks for non-nullable params                   |
| Test names  | `snake_case` (e.g., `appends_record_to_new_stream`)                                               |
| Namespaces  | Feature-based, file-scoped (e.g., `Kurrent.Client.Streams`, not `Kurrent.Client.Modules.Streams`) |
| Line length | 120 characters max                                                                                |
| Indentation | 4 spaces (C#), 2 spaces (JSON, YAML, XML, csproj)                                                 |

---

## Core Patterns

### Result Pattern

All public API methods return `Result<TSuccess, TError>`. Never throw exceptions
for expected failures. Consumers use `Match`/`Switch` for exhaustive handling.
Idiomatic extension methods (suffix `Async`) call `.UnwrapAsync()` to provide a
throwing API for users who prefer exceptions.

### Discriminated Unions (IVariant)

Sum types use `IVariant<T0, T1, ...>` with source-generated `Switch`/`Match`
methods. Used for: `ReadRecordsResult`, `ExpectedStreamState`,
`ActualStreamState`, `ConsistencyCheck`, `StartPosition`, and all error types.

### Source-Generated Errors

Exception types decorated with `[KurrentOperationError(ErrorCode)]` get a
source-generated constructor that parses Google RPC Status details. Error union
types implement `IVariantException<T0, T1, ...>` for exhaustive error handling.

### Extension Method Layering (C# 14)

Operations use the new `extension(StreamsClient client)` syntax in three tiers:
1. Core methods on the partial class (return `Result<T, E>`)
2. Extension overloads with fewer parameters (return `Result<T, E>`)
3. Idiomatic extensions with `Async` suffix (throw on error)

### Builder Pattern

Builders exposed via static `New` property/method. Records are immutable after
construction. Builders validate on `.Build()`.

---

## Build and Test

```bash
# Build (all frameworks)
dotnet build

# Build specific framework
dotnet build --framework net10.0

# Format check
dotnet format --verify-no-changes
```

### Testing with TUnit (NOT xUnit/NUnit)

**Use `dotnet run`, NOT `dotnet test`.** TUnit uses Microsoft.Testing.Platform
which requires the `dotnet run` entry point.

```bash
# Run all tests for a project (start with net8.0, then net9.0, net10.0)
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0

# Run specific test class
dotnet run --project test/Kurrent.Client.Integration.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/ReadTests/*"

# Run by category
dotnet run --project test/Kurrent.Client.Integration.Tests/ \
  ... \
  --treenode-filter "/*/*/*/*[Category=Streams]"
```

**Test assertions:** `ShouldNotThrowOrFailAsync`, `ShouldFailAsync` (custom
Result-aware extensions in `test/Kurrent.Testing/Shouldly/`).

**Containers:** FluentDocker (NOT TestContainers). KurrentDB image:
`docker.kurrent.io/kurrent-latest/kurrentdb:latest`. Containers are reused
across runs (`.KeepContainer().KeepRunning().ReuseIfExists()`).

**One-time setup:** `sudo ./scripts/gencert.sh` (generates TLS certs for
secure test mode).

**Observability:** `cd test/Kurrent.Testing && docker compose up -d` starts
Seq (logs, port 5341) and Aspire Dashboard (telemetry, port 18888).

---

## Connection Strings

```
kurrentdb://localhost:2113                           # Single node, insecure
kurrentdb://admin:changeit@localhost:2113             # With credentials
kurrentdb+discover://node1:2113,node2:2113           # Cluster discovery
kurrents://localhost:2113?tls=true&tlsVerifyCert=true # TLS
```

Node preference options: `Leader`, `Follower`, `Random`, `ReadOnlyReplica`.

---

## CI/CD

GitHub Actions only. Key workflows:

| Workflow                 | Trigger                            | Purpose                          |
|--------------------------|------------------------------------|----------------------------------|
| `build.yml`              | Push to `main`, PRs, `v*.*.*` tags | Build + unit + integration tests |
| `claude.yml`             | `@claude` mentions                 | Claude Code GitHub integration   |
| `claude-code-review.yml` | PRs to `main`                      | Automated code review            |
| `create-release.yml`     | Manual dispatch                    | Create and push version tag      |

**Commit style:** Conventional Commits v1.0.0 (`feat:`, `fix:`, `refactor:`,
etc.). See `.github/git-commit-instructions.md`.

---

## Don'ts

1. **Don't use `dotnet test`** for TUnit projects; use `dotnet run`
2. **Don't use Allman brace style**; K&R is mandatory
3. **Don't use PascalCase for test methods**; use `snake_case`
4. **Don't throw exceptions for expected failures**; return `Result` types
5. **Don't forget `ConfigureAwait(false)`** in library async code
6. **Don't use reflection for type mapping**; use `MessageTypeMapper` registration
7. **Don't modify generated code** (protobuf output, `.g.cs` files, `.Designer.cs`).
   When renaming domain types, verify the new name does not collide with
   protobuf contract field names or source-generated member names
8. **Don't use TestContainers**; use FluentDocker
9. **Don't use xUnit/NUnit attributes**; use TUnit (`[Test]`, `[Arguments]`, `[Category]`)
10. **Don't use Shouldly in new tests**; use TUnit `Assert.That(...)` exclusively
11. **Don't use `Task` for test methods**; use `ValueTask`
