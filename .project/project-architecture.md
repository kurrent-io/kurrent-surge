# Architecture

## Overview

The SDK is a layered monorepo where each package is independently installable.
`Kurrent.Client` is the foundation; higher-level packages depend on it but never reach into its internals.
All public methods throw typed exceptions on failure.
Sum types use source-generated `IVariant` discriminated unions for exhaustive matching.

## Components

| Component  | Package                | Notes   | Depends on           |
|------------|------------------------|---------|----------------------|
| Client     | `Kurrent.Client`       |         | None                 |
| Processors | `Kurrent.Processors`   |         | `Kurrent.Client`     |
| Connectors | `Kurrent.Connectors.*` | Planned | `Kurrent.Processors` |
| Framework  | `Kurrent.Framework.*`  | Future  | `Kurrent.Client`     |

`KurrentDB.Client` (v1) is legacy. `Kurrent.Client` (v2) is a clean break with zero backward compatibility.

## Core Patterns

**Exception-based error handling** — All public API methods return `ValueTask<TSuccess>` and throw typed exceptions
on failure. RPC exceptions are parsed via `ex.ToException(parsers...)` where parsers are method groups on domain
exception types. The `Result` type remains in `src/Variant/` but is no longer used by client or processor code.

**Discriminated unions** — Sum types implement `IVariant<T0, T1, ...>` with source-generated `Switch`/`Match`.
Used for: `ReadRecordsResult`, `ExpectedStreamState`, `ActualStreamState`, `ConsistencyCheck`, `StartPosition`,
and all error types. See `src/Variant/IVariant.cs:21`.

**Source-generated exceptions** — Exception types decorated with `[KurrentOperationError(ErrorCode)]` get
constructors that parse Google RPC Status details. Each exception type exposes a static `From` method used as an
`OperationExceptionParser` delegate. See `src/Kurrent.Client/Errors/KurrentOperationErrorAttribute.cs:9`.

**Extension method layering (C# 14)** — Operations use `extension(StreamsClient client)` in two tiers: core methods
on the client class and convenience overloads in extension files. All return `ValueTask<T>` directly.

**Builder pattern** — Builders exposed via static `New` property/method. Records
are immutable after construction. Builders validate on `.Build()`.

## Protocols

V2 gRPC handles all appends. V1 legacy gRPC handles all reads and management. The V2 read path is entirely
commented out; V2 read RPCs do not exist in the proto yet. Proto definitions live in
`proto/kurrentdb/protocol/v1/` and `proto/kurrentdb/protocol/v2/`.
