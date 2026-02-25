# Variant Library

Internal library providing Result types and discriminated unions via source
generation. Read [project.md](project.md) first for SDK-level context.

## Contents

- [Result Type](#result-type)
- [Discriminated Unions](#discriminated-unions)
- [Source Generators](#source-generators)
- [Key Files](#key-files)

---

## Result Type

`Result<TValue, TError>` is a record in `namespace Kurrent`.
See `src/Variant/Result/Result.cs:16`.

Implicit conversion from both `TValue` and `TError`. Consumption via
`Match` (extract a value) or `Switch` (side effects). Functional extensions
in `src/Variant/Result/Result.Functional.*.cs`: `Map`, `Bind`, `Match`,
`Switch`, `Inspect`, `Unwrap`/`UnwrapAsync`, `Try`.

`IResultBase<TValue, TError>` is a marker interface for source generator
targeting.

---

## Discriminated Unions

### IVariant

Types implementing `IVariant<T0, T1, ...>` (up to 23 type parameters) get
source-generated: `{TypeName}Case` enum, `Is{T}`/`As{T}` properties,
implicit operators both directions, exhaustive `Switch`/`Match` with sync,
async, and stateful overloads. See `src/Variant/IVariant.cs:21`.

Value DUs are `readonly partial record struct`. Example:
`src/Kurrent.Client/Modules/Streams/Models/RecordStream.cs:89`
(`ReadRecordsResult : IVariant<Record, Heartbeat>`).

### IVariantException

Error DUs are `partial class` inheriting from `Exception`.
See `src/Variant/IVariantException.cs:11`. Supports the error pipeline
via `ToExceptionalErrorResult<TError>()`.

---

## Source Generators

**VariantGenerator** (`src/Variant.SourceGenerators/VariantGenerator.cs`) —
Targets any partial type implementing `IVariant<...>` or `IVariantException<...>`.
Embedded in NuGet as `analyzers/dotnet/cs/`; runs at consumer compile time.

**KurrentOperationErrorGenerator**
(`src/Kurrent.Client.SourceGenerators/KurrentOperationErrorGenerator.cs`) —
Targets types with `[KurrentOperationError(errorCode)]`. Generates constructors
from string and `Google.Rpc.Status`, `TryParseRpcStatus`, `From` factory,
and `AfterParseRpcStatus` hook.

---

## Key Files

| Purpose             | Path                                                                    |
|---------------------|-------------------------------------------------------------------------|
| IVariant interfaces | `src/Variant/IVariant.cs:21`                                            |
| IVariantException   | `src/Variant/IVariantException.cs:11`                                   |
| Result record       | `src/Variant/Result/Result.cs:16`                                       |
| Result extensions   | `src/Variant/Result/Result.Functional.*.cs`                             |
| Variant generator   | `src/Variant.SourceGenerators/VariantGenerator.cs`                      |
| Error generator     | `src/Kurrent.Client.SourceGenerators/KurrentOperationErrorGenerator.cs` |
| Error attribute     | `src/Kurrent.Client/Errors/KurrentOperationErrorAttribute.cs:9`         |
| Unit tests          | `test/Variant.Tests/`                                                   |
