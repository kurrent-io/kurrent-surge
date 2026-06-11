# Variant: Type-Safe Discriminated Unions for .NET

Variant provides source-generated discriminated unions (sum types) for .NET. A variant holds exactly one
value out of a fixed set of types, and the generator emits exhaustive, allocation-friendly `Match` / `Switch`
members so every case is handled at compile time.

## What is a Discriminated Union?

A discriminated union represents a value that is *one of* several types. C# has no native sum type, so Variant
fills the gap: declare a `partial` type that implements `IVariant<…>`, and the source generator writes the
implementation for you.

```csharp
using Kurrent.Variant;

// Holds either a string or an int — never both, never neither.
public readonly partial record struct StringOrInt : IVariant<string, int>;
```

The generator produces a `readonly partial record struct` implementation, so variants are value types with
zero heap allocation.

## Quick Start

### Declaring a Variant

Mark the type `partial` and implement `IVariant<…>` with two or more case types:

```csharp
using Kurrent.Variant;

public readonly partial record struct Shape : IVariant<Circle, Square, Triangle>;
```

`IVariant<T0>` through `IVariant<T0, …, T22>` are available (up to 23 cases).

### Creating Values

Each case gets an implicit conversion, so you can return a case type directly:

```csharp
Shape shape = new Circle(radius: 5);   // implicit conversion
Shape other = new Square(side: 3);
```

### Pattern Matching

`Match` transforms the variant into a value; `Switch` runs side effects. The generated parameters are named
`on{CaseType}` — one per case — and matching is exhaustive:

```csharp
// Transform — returns a value
double area = shape.Match(
    onCircle:   c => Math.PI * c.Radius * c.Radius,
    onSquare:   s => s.Side * s.Side,
    onTriangle: t => 0.5 * t.Base * t.Height
);

// Side effects — returns void
shape.Switch(
    onCircle:   c => Console.WriteLine($"circle r={c.Radius}"),
    onSquare:   s => Console.WriteLine($"square s={s.Side}"),
    onTriangle: t => Console.WriteLine($"triangle {t.Base}x{t.Height}")
);
```

For primitive cases the parameter name uses a friendly alias — `IVariant<string, int>` generates `onString`
and `onInt`.

### Inspecting the Active Case

```csharp
if (shape.IsCircle) {
    var circle = shape.AsCircle;   // throws if the active case is not Circle
}

object raw = shape.Value;          // the boxed underlying value
int    pos = shape.Index;          // zero-based case index
Shape.ShapeCase active = shape.Case;  // generated enum: Circle | Square | Triangle
```

## Generated API

For a variant `T : IVariant<C0, …, Cn>`, the generator emits:

| Member                     | Description                                                          |
|----------------------------|----------------------------------------------------------------------|
| Constructors               | One per case type.                                                   |
| `{Type}Case` enum          | `byte`-backed enum with one member per case.                         |
| `Value` / `Index` / `Case` | The boxed value, zero-based index, and case enum.                    |
| `Is{Case}` / `As{Case}`    | Case test and checked cast (throws on mismatch).                     |
| Implicit operators         | `Case → Variant` and `Variant → Case` both directions.               |
| `Match<TResult>`           | Exhaustive transform; `…<TResult, TState>` overload avoids closures. |
| `MatchAsync<TResult>`      | `ValueTask`-returning transform, with and without state.             |
| `Switch`                   | Exhaustive side effects; `…<TState>` overload avoids closures.       |
| `SwitchAsync`              | `ValueTask`-returning side effects, with and without state.          |
| `ToString()`               | Delegates to the active value's `ToString()`.                        |

### State Overloads (No Closures)

Every `Match` / `Switch` has a `TState` overload that threads a value through, avoiding closure allocations on
hot paths:

```csharp
var label = shape.Match(
    onCircle:   (logger, c) => { logger.LogInfo("circle"); return "round"; },
    onSquare:   (logger, s) => { logger.LogInfo("square"); return "boxy"; },
    onTriangle: (logger, t) => { logger.LogInfo("triangle"); return "pointy"; },
    state: _logger
);
```

## Performance

- **Value type** — the generated implementation is a `readonly record struct`, so success/failure cases are
  stack-allocated with no heap pressure.
- **`ValueTask`** — async `Match` / `Switch` use `ValueTask` for minimal allocation on synchronous fast paths.
- **State overloads** — pass shared state explicitly instead of capturing it, eliminating closure allocations
  in hot loops.

## Source Generation

Variant relies on a Roslyn source generator (`VariantGenerator`) to emit the implementation for every type
that implements `IVariant<…>`. The `partial` keyword is required on the declaration so the generator can add
the generated half.

- **Repository**: part of the kurrent-sdk monorepo.
- **Location**: `src/Variant/` (runtime interfaces) and `src/Variant.SourceGenerators/` (the generator).
- **Target framework**: net10.0. The source generator targets netstandard2.0 (Roslyn requirement).
