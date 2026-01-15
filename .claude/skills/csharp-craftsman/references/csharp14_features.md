# C# 14 Features Comprehensive Reference

Complete guide to C# 14 language features with practical examples and usage guidance. C# 14 is supported on .NET 10 (LTS release, November 2025).

## Table of Contents

- [Feature Overview](#feature-overview)
- [1. Extension Members](#1-extension-members)
- [2. Field Keyword](#2-field-keyword)
- [3. First-Class Span Support](#3-first-class-span-support)
- [4. Lambda Parameter Modifiers](#4-lambda-parameter-modifiers)
- [5. Nameof with Unbound Generics](#5-nameof-with-unbound-generics)
- [6. Null-Conditional Assignment](#6-null-conditional-assignment)
- [7. Partial Constructors and Events](#7-partial-constructors-and-events)
- [8. User-Defined Compound Assignment](#8-user-defined-compound-assignment)
- [Summary](#summary)

---

## Feature Overview

C# 14 introduces:
1. **Extension members** - Extension properties and static extension methods
2. **Field keyword** - Access backing fields in properties without explicit declaration
3. **First-class Span support** - Implicit conversions for `Span<T>` and `ReadOnlySpan<T>`
4. **Lambda parameter modifiers** - Use `ref`, `out`, `in`, `scoped` without explicit types
5. **Nameof with unbound generics** - `nameof(List<>)` returns `"List"`
6. **Null-conditional assignment** - Use `?.` on left side of assignment
7. **Partial constructors and events** - Define and implement across files
8. **User-defined compound assignment** - Custom `+=`, `-=`, etc. operators

---

## 1. Extension Members

### Overview

Extension members extend the C# extension method pattern to support:
- **Extension properties** (instance)
- **Static extension methods**
- **Static extension properties**
- **User-defined operators** (as static extension methods)

### Syntax

```csharp
public static class Extensions {
    // Extension block for instance members
    extension<T>(TargetType<T> receiver) {
        // Extension property
        public ReturnType PropertyName => expression;

        // Extension method
        public ReturnType MethodName(parameters) { ... }
    }

    // Extension block for static members
    extension<T>(TargetType<T>) {
        // Static extension property
        public static ReturnType PropertyName => expression;

        // Static extension method
        public static ReturnType MethodName(parameters) { ... }

        // User-defined operator
        public static TargetType operator + (TargetType left, TargetType right) { ... }
    }
}
```

### Example: Collection Extensions

```csharp
public static class CollectionExtensions {
    // Instance extension members
    extension<T>(IEnumerable<T> source) {
        /// <summary>Indicates whether the collection contains no elements.</summary>
        public bool IsEmpty => !source.Any();

        /// <summary>Indicates whether the collection contains any elements.</summary>
        public bool IsNotEmpty => source.Any();

        /// <summary>Returns the collection with null elements filtered out.</summary>
        public IEnumerable<T> WhereNotNull()
            => source.Where(item => item is not null);

        /// <summary>Returns the first element, or null if collection is empty.</summary>
        public T? FirstOrNull()
            => source.FirstOrDefault();
    }

    // Static extension members
    extension<T>(IEnumerable<T>) {
        /// <summary>Returns an empty collection of type T.</summary>
        public static IEnumerable<T> Empty
            => Enumerable.Empty<T>();

        /// <summary>Combines two collections.</summary>
        public static IEnumerable<T> operator + (IEnumerable<T> left, IEnumerable<T> right)
            => left.Concat(right);
    }
}

// Usage:
var items = GetItems();
if (items.IsEmpty) {
    return IEnumerable<int>.Empty;
}

var filtered = items.WhereNotNull();
var combined = items + moreItems;  // Uses operator +
```

### Benefits

- **Cleaner APIs**: No more static utility classes
- **Discoverability**: Properties appear in IntelliSense on instances
- **Type safety**: Generic constraints apply naturally
- **Operators**: Can define operators for types you don't own

### When to Use

✅ **Use extension members for:**
- Replacing static utility classes (e.g., `StringHelper`, `CollectionHelper`)
- Adding properties to existing types
- Defining operators for types you don't control
- Creating fluent APIs

❌ **Avoid extension members for:**
- Complex logic better suited for dedicated classes
- Members that need access to private state

---

## 2. Field Keyword

### Overview

The `field` keyword provides access to the compiler-generated backing field without explicit declaration. Simplifies property declarations that need custom logic in one accessor.

### Syntax

```csharp
public PropertyType PropertyName {
    get;  // Or custom get
    set => field = value;  // Access backing field with 'field'
}
```

### Basic Examples

```csharp
// Validation in setter
public string Name {
    get;
    set => field = value?.Trim() ?? throw new ArgumentNullException(nameof(value));
}

// Transformation in setter
public string Email {
    get;
    set => field = value.ToLowerInvariant();
}

// Range validation
public int Age {
    get;
    set {
        if (value < 0 || value > 150)
            throw new ArgumentOutOfRangeException(nameof(value));
        field = value;
    }
}

// Custom get accessor
public decimal Price {
    get => field;
    init => field = value >= 0 ? value : throw new ArgumentException("Price must be non-negative");
}
```

### Advanced Patterns

```csharp
// Lazy initialization
public expensive ExpensiveProperty {
    get => field ??= ComputeExpensiveValue();
    set => field = value;
}

// Change tracking
public string TrackedProperty {
    get => field;
    set {
        if (field != value) {
            field = value;
            OnPropertyChanged(nameof(TrackedProperty));
        }
    }
}

// Coalescing with default
public TimeSpan Timeout {
    get => field;
    set => field = value > TimeSpan.Zero ? value : TimeSpan.FromSeconds(30);
}
```

### Migration Path

```csharp
// Before: Explicit backing field
private string _name;
public string Name {
    get => _name;
    set => _name = value?.Trim() ?? string.Empty;
}

// After: Field keyword
public string Name {
    get;
    set => field = value?.Trim() ?? string.Empty;
}
```

### Disambiguation

If your type has a field or property named `field`, use `@field` or `this.field`:

```csharp
class MyClass {
    private int field;  // Existing member named 'field'

    public int Value {
        get => @field;  // Explicit: compiler-generated backing field
        set => @field = value;
    }

    public void Method() {
        this.field = 10;  // Explicit: the existing member
    }
}
```

### When to Use

✅ **Use field keyword for:**
- Properties with validation logic
- Properties with transformation logic
- Properties that need change notification
- Migrating from auto-properties to custom accessors

❌ **Keep auto-properties when:**
- No custom logic needed
- Simple get/set suffices

---

## 3. First-Class Span Support

### Overview

C# 14 introduces implicit conversions between `Span<T>`, `ReadOnlySpan<T>`, and `T[]`, making span types first-class citizens with natural usage patterns.

### Implicit Conversions

```csharp
// T[] → Span<T>
byte[] array = GetArray();
Span<byte> span = array;  // Implicit conversion

// T[] → ReadOnlySpan<T>
ReadOnlySpan<byte> readOnlySpan = array;  // Implicit conversion

// Span<T> → ReadOnlySpan<T>
ReadOnlySpan<byte> readOnly = span;  // Implicit conversion

// Memory<T>.Span → ReadOnlySpan<T>
Memory<byte> memory = GetMemory();
ReadOnlySpan<byte> fromMemory = memory.Span;  // Implicit conversion
```

### API Design with Spans

```csharp
// Design APIs to accept ReadOnlySpan<T> for flexibility
public class DataProcessor {
    /// <summary>Processes data from any contiguous memory source.</summary>
    /// <param name="data">Data to process. Accepts arrays, spans, or memory slices.</param>
    public ProcessResult Process(ReadOnlySpan<byte> data) {
        for (int i = 0; i < data.Length; i++) {
            ProcessByte(data[i]);
        }
        return ProcessResult.Success;
    }
}

// Callers can pass arrays, spans, or stack-allocated data
processor.Process(byteArray);                    // T[] → ReadOnlySpan<T>
processor.Process(span);                         // Span<T> → ReadOnlySpan<T>
processor.Process(stackalloc byte[] {1, 2, 3});  // Direct span
processor.Process(memory.Span);                  // Memory<T>.Span
```

### Extension Methods on Spans

```csharp
public static class SpanExtensions {
    extension<T>(ReadOnlySpan<T> span) where T : IEquatable<T> {
        /// <summary>Finds the index of the first occurrence of a value.</summary>
        public int IndexOf(T value) {
            for (int i = 0; i < span.Length; i++) {
                if (span[i].Equals(value)) return i;
            }
            return -1;
        }

        /// <summary>Indicates whether the span contains the specified value.</summary>
        public bool Contains(T value) => IndexOf(value) >= 0;
    }
}

// Usage with implicit conversions:
int[] numbers = {1, 2, 3, 4, 5};
bool found = numbers.Contains(3);  // int[] → ReadOnlySpan<int>, then extension
```

### Generic Type Inference

```csharp
// Span types participate in generic type inference
public T[] ToArray<T>(ReadOnlySpan<T> span) {
    var array = new T[span.Length];
    span.CopyTo(array);
    return array;
}

// C# 14: Type inference works with span conversions
int[] source = {1, 2, 3};
int[] copy = ToArray(source);  // Type inferred as int
```

### Performance Benefits

```csharp
// Zero-copy slicing
public void ProcessChunks(ReadOnlySpan<byte> data) {
    const int chunkSize = 1024;

    for (int offset = 0; offset < data.Length; offset += chunkSize) {
        var chunk = data.Slice(offset, Math.Min(chunkSize, data.Length - offset));
        ProcessChunk(chunk);  // No allocation, just pointer math
    }
}

// Stack allocation for small buffers
public void ProcessSmallData() {
    Span<int> buffer = stackalloc int[16];  // Stack-allocated
    FillBuffer(buffer);
    ProcessData(buffer);  // Zero heap allocations
}
```

### When to Use

✅ **Design APIs with ReadOnlySpan<T> for:**
- Data processing methods
- Parsing and validation
- Cryptographic operations
- String manipulation
- Buffer operations

✅ **Use Span<T> for:**
- Methods that modify buffer contents
- Output parameters for buffer writes

---

## 4. Lambda Parameter Modifiers

### Overview

C# 14 allows parameter modifiers (`ref`, `out`, `in`, `scoped`, `ref readonly`) on lambda parameters without specifying types.

### Syntax

```csharp
// C# 13 and earlier: Explicit types required with modifiers
delegate bool TryParse<T>(string text, out T result);
TryParse<int> parser = (string text, out int result) => int.TryParse(text, out result);

// C# 14: Types inferred, modifiers allowed
TryParse<int> parser = (text, out result) => int.TryParse(text, out result);
```

### Examples by Modifier

```csharp
// 'out' parameter
delegate bool TryGet<T>(string key, out T value);
TryGet<int> getter = (key, out value) => cache.TryGetValue(key, out value);

// 'ref' parameter
delegate void Transform(ref int value);
Transform doubler = (ref value) => value *= 2;

// 'in' parameter (readonly reference)
delegate bool Compare<T>(in T left, in T right) where T : struct;
Compare<Vector3> comparer = (in left, in right) => left.Equals(right);

// 'scoped' parameter
delegate void ProcessBuffer(scoped Span<byte> buffer);
ProcessBuffer processor = (scoped buffer) => {
    // Buffer cannot escape this scope
    for (int i = 0; i < buffer.Length; i++) {
        buffer[i] = 0;
    }
};

// 'ref readonly' parameter
delegate int GetValue(ref readonly LargeStruct data);
GetValue getter = (ref readonly data) => data.Value;
```

### Combining with Other Features

```csharp
// Modifiers with discard parameters
Func<int, int, int> ignoreSecond = (first, out _) => first * 2;

// Modifiers with default parameters (when types explicit)
delegate int Calculate(int x, int y = 10, out int remainder);
Calculate calc = (x, y, out remainder) => {
    remainder = x % y;
    return x / y;
};
```

### When to Use

✅ **Use lambda parameter modifiers for:**
- TryParse-style patterns
- Delegates that modify caller's data
- Performance-critical lambdas (avoid copying large structs)
- Scope-limited buffer processing

---

## 5. Nameof with Unbound Generics

### Overview

`nameof` now supports unbound generic types, returning the type name without type arguments.

### Syntax

```csharp
// C# 14: Unbound generics
string name1 = nameof(List<>);           // "List"
string name2 = nameof(Dictionary<,>);    // "Dictionary"
string name3 = nameof(Func<,,>);         // "Func"

// Still works with closed generics
string name4 = nameof(List<int>);        // "List"
```

### Use Cases

**1. Generic Documentation**

```csharp
/// <summary>
/// Processes items from a <see cref="nameof(IEnumerable<>)"/> source.
/// </summary>
/// <typeparam name="T">
/// Item type. Must implement <see cref="nameof(IComparable<>)"/>.
/// </typeparam>
public void Process<T>(IEnumerable<T> items) where T : IComparable<T> {
    // Implementation
}
```

**2. Logging and Diagnostics**

```csharp
public void LogGenericType<T>() {
    var openType = nameof(List<>);     // "List"
    var closedType = nameof(List<T>);  // "List"
    logger.LogInformation("Processing {OpenType} of {ItemType}", openType, typeof(T).Name);
}
```

**3. Reflection and Type Resolution**

```csharp
public Type ResolveGenericType(string typeName, Type argumentType) {
    // Use nameof for consistent type name matching
    return typeName switch {
        nameof(List<>) => typeof(List<>).MakeGenericType(argumentType),
        nameof(Dictionary<,>) => typeof(Dictionary<,>).MakeGenericType(typeof(string), argumentType),
        _ => throw new ArgumentException($"Unknown type: {typeName}")
    };
}
```

### When to Use

✅ **Use nameof with unbound generics for:**
- Generic type documentation
- Logging generic type names
- Type name matching in reflection scenarios
- Error messages referencing generic types

---

## 6. Null-Conditional Assignment

### Overview

The null-conditional operators (`?.` and `?[]`) can now be used on the left side of assignments and compound assignments.

### Basic Assignment

```csharp
// C# 13 and earlier
if (customer is not null) {
    customer.Order = GetCurrentOrder();
}

// C# 14
customer?.Order = GetCurrentOrder();  // Only assigns if customer not null
```

### Array/Indexer Assignment

```csharp
// Null-conditional with indexer
users?[index].Status = UserStatus.Active;
items?[key] = newValue;

// Nested null-conditional
order?.LineItems?[0].Quantity = 10;
```

### Compound Assignment

```csharp
// += operator
config?.Timeout += TimeSpan.FromSeconds(10);

// -= operator
account?.Balance -= withdrawalAmount;

// *= operator
settings?.Scale *= 1.5;

// ??= operator (null-coalescing assignment still works)
item?.Name ??= "Default Name";
```

### Short-Circuit Evaluation

```csharp
// Right side only evaluated if left side is not null
customer?.Order = GetOrderFromDatabase();  // Query only runs if customer exists

// Useful for expensive operations
settings?.Cache = BuildExpensiveCache();  // Cache only built if settings exists
```

### Limitations

```csharp
// ❌ Increment/decrement not allowed
counter?.Value++;  // Compiler error
counter?.Value--;  // Compiler error

// ✅ Use compound assignment instead
counter?.Value += 1;
counter?.Value -= 1;
```

### Practical Examples

```csharp
// Configuration updates
public void ApplyDefaults(Configuration? config) {
    config?.Timeout = TimeSpan.FromSeconds(30);
    config?.MaxRetries = 3;
    config?.EnableCaching = true;
}

// Event handler updates
public void UpdateEventHandlers(EventManager? manager) {
    manager?.OnSuccess = HandleSuccess;
    manager?.OnError = HandleError;
}

// Collection modifications
public void UpdateFirstItem(List<Item>? items, Item newItem) {
    items?[0] = newItem;  // Only if items not null
}
```

### When to Use

✅ **Use null-conditional assignment for:**
- Optional configuration updates
- Conditional property sets
- Simplifying null-check patterns
- Fluent API implementations

❌ **Avoid when:**
- Need to handle null case differently
- Assignment side effects are important
- Increment/decrement operations needed

---

## 7. Partial Constructors and Events

### Overview

C# 14 extends partial member support to instance constructors and events, complementing partial methods and properties from C# 13.

### Partial Constructors

```csharp
// File 1: Definition
public partial class Service {
    // Defining declaration (no body)
    partial Service(ILogger logger, IConfiguration config);
}

// File 2: Implementation
public partial class Service {
    ILogger _logger;
    IConfiguration _config;

    // Implementing declaration (with body)
    partial Service(ILogger logger, IConfiguration config) {
        _logger = logger;
        _config = config;
    }
}
```

### Rules for Partial Constructors

- Must have exactly **one defining declaration** and **one implementing declaration**
- Only **instance constructors** can be partial (not static constructors)
- **Constructor initializers** (`this()` or `base()`) only allowed on implementing declaration
- Only **one partial declaration** can use primary constructor syntax

### Partial Events

```csharp
// File 1: Definition (field-like event)
public partial class Publisher {
    // Defining declaration
    partial event EventHandler StatusChanged;
}

// File 2: Implementation (with accessors)
public partial class Publisher {
    EventHandler _statusChanged;

    // Implementing declaration (must have add/remove)
    partial event EventHandler StatusChanged {
        add {
            _statusChanged += value;
            LogSubscription(value);
        }
        remove {
            _statusChanged -= value;
            LogUnsubscription(value);
        }
    }
}
```

### Use Cases

**Source Generators:**

```csharp
// User-written code
public partial class DataModel {
    partial DataModel();  // Defined by user

    public string Name { get; set; }
    public int Age { get; set; }
}

// Source generator creates implementation
public partial class DataModel {
    partial DataModel() {  // Implemented by generator
        // Generated initialization logic
        InitializeValidation();
        SetupPropertyChanged();
    }
}
```

**Event Logging:**

```csharp
// Definition with logging requirement
public partial class Service {
    partial event EventHandler<DataEventArgs> DataReceived;
}

// Implementation with automatic logging
public partial class Service {
    EventHandler<DataEventArgs> _dataReceived;

    partial event EventHandler<DataEventArgs> DataReceived {
        add {
            _dataReceived += value;
            _logger.LogInformation("Subscriber added to DataReceived");
        }
        remove {
            _dataReceived -= value;
            _logger.LogInformation("Subscriber removed from DataReceived");
        }
    }
}
```

### When to Use

✅ **Use partial constructors for:**
- Source-generated initialization logic
- Splitting complex construction across files
- Generated dependency injection setup

✅ **Use partial events for:**
- Source-generated event implementations
- Events requiring logging or validation
- Events with complex subscription logic

---

## 8. User-Defined Compound Assignment

### Overview

C# 14 enables user-defined compound assignment operators (`+=`, `-=`, `*=`, `/=`, etc.) by defining the corresponding binary operator.

### Syntax

```csharp
public class Vector {
    public double X { get; }
    public double Y { get; }

    public Vector(double x, double y) {
        X = x;
        Y = y;
    }

    // Define binary operator
    public static Vector operator + (Vector left, Vector right) =>
        new Vector(left.X + right.X, left.Y + right.Y);

    // Compound assignment (+=) automatically available
}

// Usage:
Vector v1 = new Vector(1, 2);
Vector v2 = new Vector(3, 4);
v1 += v2;  // Equivalent to: v1 = v1 + v2
```

### Supported Operators

All compound assignment operators work:

```csharp
public class Matrix {
    // Define binary operators
    public static Matrix operator + (Matrix left, Matrix right) { ... }
    public static Matrix operator - (Matrix left, Matrix right) { ... }
    public static Matrix operator * (Matrix left, Matrix right) { ... }
    public static Matrix operator / (Matrix left, double scalar) { ... }
}

// Compound assignments automatically available:
matrix += other;   // Uses operator +
matrix -= other;   // Uses operator -
matrix *= other;   // Uses operator *
matrix /= 2.0;     // Uses operator /
```

### Example: Custom Numeric Type

```csharp
public readonly struct Money {
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency) {
        Amount = amount;
        Currency = currency;
    }

    public static Money operator + (Money left, Money right) {
        if (left.Currency != right.Currency)
            throw new InvalidOperationException("Currency mismatch");
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator * (Money money, decimal multiplier) =>
        new Money(money.Amount * multiplier, money.Currency);
}

// Usage:
Money price = new Money(100, "USD");
price += new Money(50, "USD");  // $150
price *= 1.1m;                  // $165 (10% increase)
```

### When to Use

✅ **Use user-defined compound assignment for:**
- Mathematical types (vectors, matrices, complex numbers)
- Custom numeric types (currency, measurements)
- Collection types with natural addition semantics
- Types with accumulation operations

---

## Summary

C# 14 brings significant language improvements:

1. **Extension members** - Cleaner, more discoverable APIs
2. **Field keyword** - Simplified property patterns
3. **Span support** - Natural, high-performance buffer handling
4. **Lambda modifiers** - Concise delegate expressions
5. **Nameof generics** - Better generic type references
6. **Null-conditional assignment** - Simplified conditional updates
7. **Partial constructors/events** - Enhanced source generator support
8. **Compound assignment** - Natural arithmetic for custom types

**Adoption strategy: Aggressive**
Use these features by default. They improve code clarity, reduce boilerplate, and leverage modern .NET performance characteristics.
