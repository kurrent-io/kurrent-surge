# Architecture

> Layered monorepo where each package is independently installable; `Kurrent.Client` is the foundation.

## Overview

The SDK uses a strict dependency hierarchy — higher-level packages depend on `Kurrent.Client` but never reach into its internals. All public methods throw typed exceptions on failure.

## Key Concepts

**Exception-based error handling** — All public API methods return `ValueTask<TSuccess>` and throw typed exceptions on failure. RPC exceptions are parsed via `ex.ToException(parsers...)` where parsers are static `From` method groups on domain exception types. The `Result` type remains in `src/Variant/` but is no longer used by client or processor code.

**Discriminated unions** — Sum types implement `IVariant<T0, T1, ...>` with source-generated `Switch`/`Match`. Used for: `ReadRecordsResult`, `ExpectedStreamState`, `ActualStreamState`, `ConsistencyCheck`, `StartPosition`, and all error types.

**Source-generated exceptions** — Exception types decorated with `[KurrentOperationError(ErrorCode)]` get constructors that parse Google RPC Status details. Each exception type exposes a static `From` method used as an `OperationExceptionParser` delegate.

**Extension method layering (C# 14)** — Operations use `extension(StreamsClient client)` in two tiers: core methods on the client class and convenience overloads in extension files. All return `ValueTask<T>` directly.

**Builder pattern** — Builders exposed via static `New` property/method. Records are immutable after construction. Builders validate on `.Build()`.

## Components

| Component  | Package                | Status  | Depends on           |
|------------|------------------------|---------|----------------------|
| Client     | `Kurrent.Client`       | Active  | None                 |
| Processors | `Kurrent.Processors`   | Active  | `Kurrent.Client`     |
| Connectors | `Kurrent.Connectors.*` | Planned | `Kurrent.Processors` |
| Framework  | `Kurrent.Framework.*`  | Future  | `Kurrent.Client`     |

`KurrentDB.Client` (v1) is legacy. `Kurrent.Client` (v2) is a clean break with zero backward compatibility.

`Kurrent.Client` exposes four active modules: `Streams`, `Registry`, `Indexes`, `Connectors`. These are modules within the package, not separate packages. See `client.md` for the module breakdown.

## Protocols

Proto layout has three active layers plus a shared namespace:

- `proto/kurrentdb/protocol/v0/` — draft layer, **excluded from build entirely** via `.csproj` glob exclusion
- `proto/kurrentdb/protocol/v1/` — legacy gRPC: reads, management
- `proto/kurrentdb/protocol/v2/` — V2 gRPC: appends, registry, indexes, connectors
- `proto/kurrent/rpc/` — shared error types used across both versions

V2 read path is entirely commented out; those RPCs do not exist yet. Despite using V2 protos, Connectors and Indexes still route through `LegacyCallInvoker` — the V1/V2 label describes protocol version, not call path.

## Gotchas

- The `Result` type in `src/Variant/` is intentionally unused — do not wire it back in.
- V2 read code is commented out by design; do not uncomment it until the proto RPCs are defined.
- Never reach from a higher-level package into `Kurrent.Client` internals.
