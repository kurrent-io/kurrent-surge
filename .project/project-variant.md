# Variant Library

Internal library providing Result types and discriminated unions via source
generation. Read [project.md](project.md) first for SDK-level context.

---

## Purpose

The Variant library eliminates boilerplate for two patterns used throughout
the SDK:

1. **Result types** (`Result<TValue, TError>`) for railway-oriented error handling
2. **Discriminated unions** (`IVariant<T0, T1, ...>`) for exhaustive type matching

Both use source generators targeting `netstandard2.0` so they work across all
target frameworks and ship embedded in the NuGet package as analyzers.

---

## Result Type

`Result<TValue, TError>` is a record in `namespace Kurrent`.

### Construction

```csharp
Result<int, string> success = 42;          // implicit from TValue
Result<int, string> failure = "not found"; // implicit from TError
```

### Consumption

```csharp
result.Match(
    value => HandleSuccess(value),
    error => HandleError(error)
);

result.Switch(
    value => DoSomething(value),
    error => DoSomethingElse(error)
);
```

### Functional Extensions

Located in `src/Variant/Result/Result.Functional.*.cs`:

| Method                   | Purpose                                 |
|--------------------------|-----------------------------------------|
| `Map`                    | Transform success value                 |
| `Bind`                   | Chain operations returning Result       |
| `Match`                  | Extract a value from either case        |
| `Switch`                 | Execute side effects for either case    |
| `Inspect`                | Peek at success value without consuming |
| `Unwrap` / `UnwrapAsync` | Extract value or throw error            |
| `Try`                    | Wrap a throwing operation into Result   |

### IResultBase

`IResultBase<TValue, TError>` is a marker interface for source generator
targeting. Types implementing it get generated implicit operators and
conversion methods.

---

## Discriminated Unions (IVariant)

### Definition

```csharp
// Value type DU (readonly record struct)
public readonly partial record struct ReadRecordsResult
    : IVariant<Record, Heartbeat> { }

// Error type DU (class, for exception hierarchy)
public partial class AppendRecordsError
    : IVariantException<ConsistencyViolationException, Exception> { }
```

### Generated Members

For `IVariant<T0, T1>`, the `VariantGenerator` produces:

| Member                             | Purpose                                   |
|------------------------------------|-------------------------------------------|
| `{TypeName}Case` enum              | Discriminant values                       |
| `Is{T}` property                   | Type check                                |
| `As{T}` property                   | Typed access (throws if wrong case)       |
| `implicit operator`                | Both directions (T → DU, DU → T)          |
| `Switch(Action<T0>, Action<T1>)`   | Exhaustive side effects                   |
| `Match<R>(Func<T0,R>, Func<T1,R>)` | Exhaustive extraction                     |
| Async variants                     | `Switch`/`Match` with `ValueTask` lambdas |
| State variants                     | `Switch`/`Match` with `TState` parameter  |

### Storage

Generated code stores values as `object _value` with a `byte _index`
discriminant. This is internal to the generated code.

### IVariant vs IVariantException

| Interface                   | Generated as                     | Use case                          |
|-----------------------------|----------------------------------|-----------------------------------|
| `IVariant<T0, T1>`          | `readonly partial record struct` | Value DUs (state, messages)       |
| `IVariantException<T0, T1>` | `partial class`                  | Error DUs (exception hierarchies) |

`IVariantException` types inherit from `Exception` and support the error
pipeline via `ToExceptionalErrorResult<TError>()`.

### Arity

`IVariant` supports up to 23 type parameters (`IVariant<T0, ..., T22>`).

---

## Source Generators

### VariantGenerator

**Location:** `src/Variant.SourceGenerators/VariantGenerator.cs`

Targets: any `partial record struct` or `partial class` implementing
`IVariant<...>` or `IVariantException<...>`.

**Build output:** embedded in NuGet as `analyzers/dotnet/cs/` (ships with the
package, runs at consumer's compile time).

### KurrentOperationErrorGenerator

**Location:** `src/Kurrent.Client.SourceGenerators/KurrentOperationErrorGenerator.cs`

Targets: any `partial class` annotated with `[KurrentOperationError(errorCode)]`.

Generates:
- Constructor from `string message`
- Constructor from `Google.Rpc.Status` (parses structured error details)
- `TryParseRpcStatus` static method
- `From` static factory
- `AfterParseRpcStatus` hook (default interface method on
  `IKurrentOperationException<T>`) for custom property population

---

## Key Files

| Purpose             | Path                                                                    |
|---------------------|-------------------------------------------------------------------------|
| IVariant interfaces | `src/Variant/IVariant.cs`                                               |
| IVariantException   | `src/Variant/IVariantException.cs`                                      |
| Result record       | `src/Variant/Result/Result.cs`                                          |
| Result extensions   | `src/Variant/Result/Result.Functional.*.cs`                             |
| IResultBase         | `src/Variant/Result/IResultBase.cs`                                     |
| Variant generator   | `src/Variant.SourceGenerators/VariantGenerator.cs`                      |
| Error generator     | `src/Kurrent.Client.SourceGenerators/KurrentOperationErrorGenerator.cs` |
| Error attribute     | `src/Kurrent.Client/Errors/KurrentOperationErrorAttribute.cs`           |
| Error base class    | `src/Kurrent.Client/Errors/KurrentException.cs`                         |
| Unit tests          | `test/Variant.Tests/`                                                   |
