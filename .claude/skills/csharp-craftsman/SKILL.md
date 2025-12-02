---
name: csharp-craftsman
description: Master of modern C# 14 and .NET 10 development. Writes production-ready C# code with aggressive C# 14 feature adoption, performance best practices, comprehensive XML documentation, and TUnit tests. Follows precise formatting standards including vertical alignment and class organization. Domain-agnostic, style-focused approach.
---

# C# Craftsman Skill

Expert-level C# and .NET development skill focused on code quality, modern language features, and performance. Generates production-ready code following established standards with complete documentation and appropriate test coverage.

## Purpose

Provide mastery of modern C# 14 and .NET 10 development including:
- Writing C# code in precise style with vertical alignment and class organization
- Aggressive adoption of C# 14 features (extension members, `field` keyword, span support)
- Performance optimization using .NET 10 capabilities (JIT improvements, stack allocation)
- Complete XML documentation (human-readable, domain-agnostic)
- Comprehensive testing with TUnit, Shouldly, and FakeItEasy
- Balanced performance approach (optimize hot paths, keep other code readable)

## When to Use This Skill

Activate this skill when:
- User requests C# code generation ("write a C# class", "create an interface")
- User asks to add tests or test coverage
- User mentions C# 14 features or .NET 10
- User asks about C# performance optimization
- User requests XML documentation for C# code
- User asks "how should I write this in C#?"
- User wants to refactor C# code to modern standards
- User asks about coding standards or best practices

## Core Philosophy

### Style-Focused, Domain-Agnostic

This skill prioritizes **code style, formatting, and modern capabilities** over domain-specific patterns:

- **Style**: Precise vertical alignment, class organization, naming conventions
- **Modern**: Aggressive C# 14 adoption, .NET 10 performance patterns
- **Universal**: Domain-agnostic code and documentation
- **Quality**: Complete XML docs, comprehensive tests, performance awareness

### C# 14 Feature Adoption: Aggressive

**Use C# 14 features by default** whenever applicable:

✅ **Prefer:**
- Extension members over static utility classes
- `field` keyword over explicit backing fields
- Null-conditional assignment over null checks
- Lambda parameter modifiers without types
- First-class Span<T> support
- `nameof` with unbound generics

❌ **Avoid older patterns when C# 14 equivalent exists**

**Example transformation:**

```csharp
// OLD (C# 13 and earlier)
private string _name;
public string Name {
    get => _name;
    set => _name = value ?? throw new ArgumentNullException(nameof(value));
}

// NEW (C# 14 with field keyword)
public string Name {
    get;
    set => field = value ?? throw new ArgumentNullException(nameof(value));
}
```

### Performance: Balanced Approach

**Optimize hot paths only**, keep other code simple and readable:

**Hot Path Indicators:**
- Method names: `Process*`, `Handle*`, `Execute*`, `Calculate*`
- Collection operations: loops, LINQ, batch processing
- User explicitly mentions performance
- Method called in loops or high-frequency scenarios

**Hot Path Optimizations:**
- Use `Span<T>` and `Memory<T>`
- Stack-allocate small arrays
- Use static lambdas to avoid closures
- Apply SIMD when applicable
- Leverage .NET 10 JIT improvements

**Non-Hot Paths:**
- Prioritize readability and maintainability
- Use standard LINQ and collections
- Keep code simple

**Always document** why performance optimization was applied.

### Documentation: Human-Readable Excellence

**Every public member must have complete XML documentation:**

- **Domain-agnostic** explanations (no domain assumptions)
- **Human-readable** (write for public documentation websites)
- **Focused on purpose** ("why") not just mechanics ("what")
- **Structured defaults** for configuration properties
- **Performance context** for optimization-sensitive code

**Example:**

```csharp
/// <summary>
/// Maximum number of items to process in a single batch operation.
/// </summary>
/// <remarks>
/// <para><b>Default:</b> 1000 items</para>
/// <para><b>Importance:</b> Medium</para>
/// <para><b>Valid Range:</b> 1 to 100,000 items</para>
/// <para>
/// Larger batch sizes improve throughput but increase memory usage and latency.
/// Consider workload characteristics when adjusting this value.
/// </para>
/// </remarks>
public int BatchSize { get; init; } = 1000;
```

### Testing: Core Coverage with Flexibility

**Default test coverage: Core**
- Unit tests for all public methods
- Edge cases and error conditions
- Critical business logic paths
- Integration test structure

**User can request:**
- **"Comprehensive tests"**: Add property-based tests, all edge cases, performance tests
- **"Minimal tests"**: Basic structure only, user adds tests later

**Testing stack:**
- **TUnit**: Modern, fast testing framework
- **Shouldly**: Readable assertions
- **FakeItEasy**: Simple mocking

**Naming convention:** `snake_case` with pattern `[action]_[when_condition]`

```csharp
[Test]
public async Task returns_valid_result_when_processing_succeeds() {
    // Arrange
    var processor = new EventProcessor();
    var eventData = CreateTestEvent();

    // Act
    var result = await processor.ProcessAsync(eventData);

    // Assert
    result.IsValid.ShouldBeTrue();
}
```

## C# 14 Feature Guide

### 1. Extension Members

Define extension properties and static extension members:

```csharp
public static class CollectionExtensions {
    // Extension block for instance members
    extension<T>(IEnumerable<T> source) {
        // Extension property
        public bool IsEmpty => !source.Any();

        // Extension method
        public IEnumerable<T> WhereNotNull() => source.Where(item => item is not null);
    }

    // Extension block for static members
    extension<T>(IEnumerable<T>) {
        // Static extension property
        public static IEnumerable<T> Empty => Enumerable.Empty<T>();

        // Static user-defined operator
        public static IEnumerable<T> operator + (IEnumerable<T> left, IEnumerable<T> right) =>
          left.Concat(right);
    }
}

// Usage:
var list = GetItems();
if (list.IsEmpty) { ... }

var combined = IEnumerable<int>.Empty + list;
```

**When to use:** Replace static utility classes with extension members for cleaner, more discoverable APIs.

### 2. Field Keyword

Access compiler-generated backing field without explicit declaration:

```csharp
public string Name {
    get;
    set => field = value?.Trim() ?? throw new ArgumentNullException(nameof(value));
}

public int Count {
    get => field;
    private set {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        field = value;
    }
}
```

**When to use:** Properties with validation, transformation, or custom logic in accessors.

### 3. First-Class Span Support

Implicit conversions between `Span<T>`, `ReadOnlySpan<T>`, and `T[]`:

```csharp
public void ProcessData(ReadOnlySpan<byte> data) {
    // Implementation
}

// All these calls work with implicit conversions:
byte[] array = GetArray();
ProcessData(array);                    // T[] → ReadOnlySpan<T>

Span<byte> span = GetSpan();
ProcessData(span);                     // Span<T> → ReadOnlySpan<T>

Memory<byte> memory = GetMemory();
ProcessData(memory.Span);              // Memory<T>.Span → ReadOnlySpan<T>
```

**When to use:** Design APIs that accept `ReadOnlySpan<T>` or `Span<T>` for flexibility and performance.

### 4. Lambda Parameter Modifiers

Use `ref`, `out`, `in`, `scoped` without explicit types:

```csharp
delegate bool TryParse<T>(string text, out T result);

// C# 14: Modifiers without types
TryParse<int> parser = (text, out result) => int.TryParse(text, out result);

// Scoped without type
Action<Span<byte>> process = (scoped buffer) => ProcessBuffer(buffer);
```

**When to use:** Lambda expressions with delegates that use parameter modifiers.

### 5. Nameof with Unbound Generics

```csharp
// C# 14: Unbound generic
string name     = nameof(List<>);         // "List"
string dictName = nameof(Dictionary<,>);  // "Dictionary"

// Use in generic constraints documentation:
/// <typeparam name="T">
/// Collection type. Must implement <see cref="nameof(IEnumerable<>)"/>.
/// </typeparam>
```

**When to use:** Generic type references in documentation, logging, reflection scenarios.

### 6. Null-Conditional Assignment

Use `?.` and `?[]` on left side of assignment:

```csharp
// C# 14: Null-conditional assignment
customer?.Order = GetCurrentOrder();           // Only assigns if customer not null

users?[index].Status = UserStatus.Active;      // Only assigns if users not null

// Compound assignment also works
config?.Timeout += TimeSpan.FromSeconds(10);
```

**When to use:** Conditional property updates, avoiding explicit null checks.

### 7. Partial Constructors and Events

```csharp
// File 1: Definition
public partial class Service {
    partial Service(ILogger logger);              // Defining declaration
    partial event EventHandler StatusChanged;      // Defining declaration
}

// File 2: Implementation
public partial class Service {
    partial Service(ILogger logger) {              // Implementing declaration
        Logger = logger;
    }

    partial event EventHandler StatusChanged {     // Implementing declaration
        add    => _statusChanged += value;
        remove => _statusChanged -= value;
    }
}
```

**When to use:** Source generators, splitting concerns across files.

### 8. User-Defined Compound Assignment

```csharp
public static class Vector {
    public static Vector operator + (Vector left, Vector right) =>
        new Vector(left.X + right.X, left.Y + right.Y);

    // C# 14: Compound assignment (+=, -=, etc.)
}

// Usage:
vector += otherVector;  // Uses operator + and assigns
```

**When to use:** Custom types with arithmetic operations.

## .NET 10 Performance Patterns

### JIT Compiler Improvements

**.NET 10 JIT optimizations to leverage:**

1. **Array Interface Devirtualization**
   ```csharp
   // .NET 10 optimizes this pattern:
   int[] numbers = GetNumbers();
   IEnumerable<int> enumerable = numbers;

   foreach (var num in enumerable) {  // Devirtualized in .NET 10
       Process(num);
   }
   ```

2. **Improved Inlining**
   ```csharp
   // Methods that enable devirtualization through inlining are now inlined:
   public int ProcessItems<T>(IEnumerable<T> items) where T : IItem {
       return items.Sum(item => item.GetValue());  // Better inlining in .NET 10
   }
   ```

3. **Stack Allocation**
   ```csharp
   // Small arrays of value types and reference types stack-allocated:
   int[]    numbers = {1, 2, 3, 4, 5};           // Stack-allocated in .NET 10
   string[] words   = {"Hello", "World"};       // Stack-allocated in .NET 10
   ```

### Performance Optimization Strategies

**1. Use Span<T> for High-Performance Operations**

```csharp
// Hot path: Process large buffers
public void ProcessBuffer(ReadOnlySpan<byte> buffer) {
    for (int i = 0; i < buffer.Length; i++) {
        ProcessByte(buffer[i]);
    }
}

// Stack-allocate small buffers
Span<int> localBuffer = stackalloc int[16];
```

**2. Avoid Closures in Hot Paths**

```csharp
// ❌ Bad: Captures variable, allocates closure
int threshold = 100;
var filtered  = items.Where(x => x.Value > threshold);

// ✅ Good: Static lambda with explicit parameter
var filtered = items.Where(static (x, t) => x.Value > t, threshold);
```

**3. Use ValueTask for Frequently Synchronous Operations**

```csharp
public ValueTask<Item> GetCachedItemAsync(int id) {
    if (_cache.TryGetValue(id, out var cached))
        return new ValueTask<Item>(cached);  // Synchronous path

    return LoadItemAsync(id);                // Asynchronous path
}
```

**4. Collection Initialization with Capacity**

```csharp
// ✅ Good: Pre-size collections when size known
var items  = new List<Item>(expectedCount);
var lookup = new Dictionary<string, Item>(expectedSize);
```

**5. High-Performance Logging**

```csharp
// Use source-generated logging (no allocations):
[LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Processing {Count} items for {UserId}")]
static partial void LogProcessing(ILogger logger, int count, string userId);
```

## Class Organization Rules

Follow precise member ordering:

```csharp
public class CompleteExample {
    // 1. CONSTRUCTORS (parameter count order: 0, 1, 2, ...)
    public CompleteExample() { }

    public CompleteExample(string name) {
        Name = name;
    }

    public CompleteExample(string name, ILogger logger) : this(name) {
        Logger = logger;
    }

    // 2. PUBLIC PROPERTIES (logical grouping, then alphabetical)
    public string Name     { get; init; }
    public bool   IsActive { get; set; }

    // 3. PRIVATE/INTERNAL PROPERTIES
    ILogger Logger { get; }

    // 4. PUBLIC METHODS (logical grouping, main operations first)
    public async Task<Result> ProcessAsync(CancellationToken ct = default) {
        // Implementation
    }

    public void Reset() {
        // Implementation
    }

    // 5. PRIVATE/INTERNAL METHODS
    async Task<bool> ValidateInternalAsync() {
        // Implementation
    }

    // 6. INNER TYPES (records, enums, nested classes)
    public record Result(bool Success, string Message);

    public enum Status {
        Idle,
        Processing,
        Complete
    }
}
```

## Vertical Alignment Patterns

Apply vertical alignment for readability:

```csharp
// Record properties
public record Configuration {
    public int                   MaxEvents       { get; init; } = 100;
    public bool                  IncludeMetadata { get; init; } = true;
    public TimeSpan              Timeout         { get; init; } = TimeSpan.FromSeconds(30);
    public IReadOnlyList<string> EventTypes      { get; init; } = [];
}

// Fluent methods
public class Builder {
    public Builder WithName(string name)                      => this with { Name = name };
    public Builder WithTimeout(TimeSpan timeout)              => this with { Timeout = timeout };
    public Builder WithMaxRetries(int maxRetries)             => this with { MaxRetries = maxRetries };
    public Builder WithEventFilter(Func<string, bool> filter) => this with { EventFilter = filter };
}
```

## Test Generation Patterns

### Basic Test Structure

```csharp
using TUnit.Core;
using Shouldly;
using FakeItEasy;

public class ServiceTests {
    IRepository _repository;
    ILogger     _logger;
    Service     _service;

    [Before(Test)]
    public void Setup() {
        _repository = A.Fake<IRepository>();
        _logger     = A.Fake<ILogger<Service>>();
        _service    = new Service(_repository, _logger);
    }

    [Test]
    public async Task returns_valid_result_when_processing_succeeds() {
        // Arrange
        var data = CreateTestData();
        A.CallTo(() => _repository.GetAsync(A<int>._))
            .Returns(Task.FromResult(data));

        // Act
        var result = await _service.ProcessAsync(1);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Data.ShouldBe(data);
    }
}
```

### Test Data Builders

```csharp
public class ItemBuilder {
    string _name  = "default";
    int    _value = 0;

    public ItemBuilder WithName(string name) {
        _name = name;
        return this;
    }

    public ItemBuilder WithValue(int value) {
        _value = value;
        return this;
    }

    public Item Build() => new(_name, _value);
}

// Usage in tests:
var item = new ItemBuilder()
    .WithName("test")
    .WithValue(100)
    .Build();
```

## Reference Documentation

Comprehensive references in `references/` directory:

1. **csharp14_features.md** - Complete C# 14 feature guide with examples
2. **dotnet10_performance.md** - .NET 10 JIT and runtime optimizations
3. **coding_standards.md** - Class organization, vertical alignment, patterns
4. **documentation_standards.md** - XML documentation rules and examples
5. **testing_standards.md** - TUnit, Shouldly, FakeItEasy patterns
6. **performance_guidelines.md** - Memory management, SIMD, optimization strategies
7. **examples.md** - Complete code examples showing all patterns

## User Preference System

### Test Coverage Levels

**Default: Core Coverage**
- Public methods tested
- Edge cases and error conditions
- Critical paths

**User can request "comprehensive tests":**
```
User: "Write EventProcessor with comprehensive tests"
```
Generates:
- All edge cases
- Property-based tests
- Performance benchmarks
- Integration tests
- Error condition exhaustive testing

**User can request "minimal tests":**
```
User: "Write ConfigLoader with minimal tests"
```
Generates:
- Basic test structure
- One smoke test
- User adds remaining tests

### Performance Optimization Requests

**User can explicitly request optimization:**
```
User: "Optimize the ProcessBatch method for performance"
```
Applies:
- Span<T> usage
- Stack allocation
- SIMD if applicable
- .NET 10 JIT-friendly patterns
- Documents optimization rationale

## Code Generation Workflow

### When User Requests Code

1. **Analyze requirements** - Understand what user wants to build
2. **Identify hot paths** - Determine performance-critical sections
3. **Apply C# 14 features** - Use modern language capabilities
4. **Generate complete XML docs** - Human-readable, domain-agnostic
5. **Create appropriate tests** - Core coverage unless user specifies
6. **Document decisions** - Explain C# 14 choices and optimizations

### Output Structure

```
[ClassName].cs
- Class with C# 14 features
- Complete XML documentation
- Performance optimizations where applicable
- Clean, vertically aligned code

[ClassName]Tests.cs
- TUnit test class
- Shouldly assertions
- FakeItEasy mocks
- Core coverage (or as requested)
- Test data builders if needed
```

## Summary

C# Craftsman skill provides:
- **Modern C#**: Aggressive C# 14 adoption
- **Performance**: Balanced approach with .NET 10 optimizations
- **Quality**: Complete documentation and appropriate tests
- **Style**: Precise formatting and organization
- **Flexibility**: User can control test coverage and optimization level

Use this skill for all C# development to ensure consistent, modern, high-quality code.
