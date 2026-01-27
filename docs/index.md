# Kurrent.Client Documentation Index

**Generated:** 2026-01-16
**Scan Level:** Deep
**Project Type:** Library (Monolith)

---

## Project Overview

- **Type:** Monolith Library
- **Primary Language:** C# 14
- **Frameworks:** .NET 8.0, 9.0, 10.0
- **Architecture:** Modular Client with Feature Modules

---

## Quick Reference

| Aspect | Value |
|--------|-------|
| **Entry Point** | `KurrentClient` |
| **Main Package** | `KurrentDB.Client` |
| **Test Framework** | TUnit (use `dotnet run`, NOT `dotnet test`) |
| **Communication** | gRPC + Protocol Buffers |
| **Error Handling** | Result pattern + idiomatic exceptions |

---

## V2 Key Features

1. **Schema Registry + Automatic Serde** - JSON/Protobuf with auto-registration
2. **Filter Expressions** - SQL-like queries (DuckDB compatible)
3. **Enhanced Resilience** - Pre-built profiles (Default, FailFast, RetryForever, etc.)
4. **Record Model** - Unified message type for all data
5. **Dual API Surface** - Result pattern AND exception-throwing extensions

---

## Generated Documentation

### Core Documentation

- [Project Overview](./project-overview.md) - Introduction and quick start
- [Architecture](./architecture.md) - System design, patterns, V2 differentiators
- [Source Tree Analysis](./source-tree-analysis.md) - Code organization and structure
- [Development Guide](./development-guide.md) - Build, test, contribute

### State File

- [project-scan-report.json](./project-scan-report.json) - Scan metadata and progress

---

## Existing Documentation (In Repository)

### Project Instructions

- [.project/project.md](../.project/project.md) - Comprehensive AI agent development guide
- [CLAUDE.md](../CLAUDE.md) - Core directives for AI agents
- [README.md](../README.md) - Repository introduction

### V2 Feature Specifications

| Feature | Location | Status |
|---------|----------|--------|
| Filter Expressions | [.project/reference/features/filter-expressions-spec.md](../.project/reference/features/filter-expressions-spec.md) | Implemented |
| Stream Reads | [.project/reference/features/stream-reads.md](../.project/reference/features/stream-reads.md) | Draft |
| Record Processing | [.project/reference/features/record-processing.md](../.project/reference/features/record-processing.md) | Draft |

### KurrentDB Server Reference

- [Server Features](../.project/reference/kurrent-db/server/features/) - Streams, projections, connectors
- [HTTP API](../.project/reference/kurrent-db/server/http-api/) - REST API documentation
- [Configuration](../.project/reference/kurrent-db/server/configuration/) - Server setup
- [Security](../.project/reference/kurrent-db/server/security/) - Authentication, authorization

### Templates

- [.project/docs/templates/](../.project/docs/templates/) - PRD, RFC, ADR, Architecture templates

---

## Getting Started

### For Development

1. Read the [Development Guide](./development-guide.md)
2. Understand the [Architecture](./architecture.md)
3. Review the [Source Tree](./source-tree-analysis.md)

### For V2 Features

1. Review [Filter Expressions Spec](../.project/reference/features/filter-expressions-spec.md)
2. Review [Stream Reads Spec](../.project/reference/features/stream-reads.md)
3. Check [KurrentClientResilienceOptions](../src/KurrentDB.Client/Kurrent.Client/Options/KurrentClientResilienceOptions.cs) for resilience profiles

### For Brownfield PRD

Point the PRD workflow to this index: `docs/index.md`

---

## Key Files Reference

| Purpose | File |
|---------|------|
| Main Client | `src/KurrentDB.Client/Kurrent.Client/KurrentClient.cs` |
| Streams Module | `src/KurrentDB.Client/Kurrent.Client/Modules/Streams/StreamsClient.cs` |
| Registry Module | `src/KurrentDB.Client/Kurrent.Client/Modules/Registry/RegistryClient.cs` |
| Resilience Options | `src/KurrentDB.Client/Kurrent.Client/Options/KurrentClientResilienceOptions.cs` |
| Result Types | `src/Variant/Result/Result.cs` |
| Discriminated Unions | `src/Variant/IVariant.cs` |
| Build Config | `Directory.Build.props` |
| Package Versions | `Directory.Packages.props` |

---

## Next Steps

After reviewing this documentation:

1. **Compare with V1** - Use Context7 to fetch EventStoreDB client documentation
2. **Plan Features** - Use the PRD workflow for new feature planning
