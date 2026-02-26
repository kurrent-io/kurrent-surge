# Architecture

## Overview

The SDK is a layered monorepo where each package is independently installable.
`Kurrent.Client` is the foundation; higher-level packages depend on it but never reach into its internals.
All public methods return `Result<TSuccess, TError>` for railway-oriented error handling.
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

**Result pattern** — All public API methods return `Result<TSuccess, TError>`.
Consumers use `Match`/`Switch` for exhaustive handling. Idiomatic extension methods (suffix `Async`) call
`.UnwrapAsync()` for a throwing API. See `src/Variant/Result/Result.cs:16`.

**Discriminated unions** — Sum types implement `IVariant<T0, T1, ...>` with source-generated `Switch`/`Match`.
Used for: `ReadRecordsResult`, `ExpectedStreamState`, `ActualStreamState`, `ConsistencyCheck`, `StartPosition`,
and all error types. See `src/Variant/IVariant.cs:21`.

**Source-generated errors** — Exception types decorated with `[KurrentOperationError(ErrorCode)]` get
constructors that parse Google RPC Status details. Error unions implement `IVariantException<T0, T1, ...>`.
See `src/Kurrent.Client/Errors/KurrentOperationErrorAttribute.cs:9`.

**Extension method layering (C# 14)** — Operations use `extension(StreamsClient client)` in three tiers: core
methods (return `Result`), convenience overloads (return `Result`), idiomatic extensions with `Async` suffix (throw on error).

**Builder pattern** — Builders exposed via static `New` property/method. Records
are immutable after construction. Builders validate on `.Build()`.

## Protocols

V2 gRPC handles all appends. V1 legacy gRPC handles all reads and management. The V2 read path is entirely
commented out; V2 read RPCs do not exist in the proto yet. Proto definitions live in
`proto/kurrentdb/protocol/v1/` and `proto/kurrentdb/protocol/v2/`.
