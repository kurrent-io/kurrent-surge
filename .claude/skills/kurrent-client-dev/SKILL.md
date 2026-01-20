---
name: kurrent-client-dev
description: Enforce Kurrent.Client code style (K&R braces, _camelCase fields, var everywhere), run TUnit tests with dotnet run, troubleshoot build/test failures. Use when writing C# code, checking naming conventions, running tests, fixing build errors, or asking about project patterns in this .NET codebase.
---

# Kurrent.Client Development Guide

## Testing (TUnit)

**Use `dotnet run`, NOT `dotnet test`.**

### Setup (one-time)
```bash
sudo ./gencert.sh                                      # Generate certs
cd test/Kurrent.Client.Testing && docker compose up -d # Start Seq + Aspire
```

### Run Tests (always via `dotnet run`)

- always run for dotnet 8.0 until the tests pass.
- once tests pass for dotnet 8.0, run for dotnet 9.0 and 10.0 as well.

```bash
# Integration tests
dotnet run --project test/Kurrent.Client.Integration.Tests/ \
--disable-logo --no-ansi --no-progress --fail-fast --adaptive-metrics \
--output Detailed --log-level Trace \
--framework net8.0

# Filter by category
dotnet run --project test/Kurrent.Client.Integration.Tests/ \
--disable-logo --no-ansi --no-progress --fail-fast --adaptive-metrics \
--output Detailed --log-level Trace \
--framework net8.0 \
--treenode-filter "/*/*/*/*[Category=Streams]"

# Filter by test class name
dotnet run --project test/Kurrent.Client.Integration.Tests/ \
--disable-logo --no-ansi --no-progress --fail-fast --adaptive-metrics \
--output Detailed --log-level Trace \
--framework net8.0 \
--treenode-filter "/*/*/ReadTests/*"

# Unit tests
dotnet run --project test/Kurrent.Client.Tests/ \
--disable-logo --no-ansi --no-progress --fail-fast --adaptive-metrics \
--output Detailed --log-level Trace \
--framework net8.0
```

### Assertions (TUnit)
```csharp
await Assert.That(result).IsNotNull();
await Assert.That(result.Value).IsEqualTo(expected);
await Assert.That(collection).Contains(item);
await Assert.That(action).ThrowsException();
```

**Mocking** - FakeItEasy:
```csharp
var fake = A.Fake<IService>();
A.CallTo(() => fake.Method(A<string>._)).Returns(result);
```

### Observability
- **Logs:** http://localhost:5341 (Seq)
- **Traces:** http://localhost:18888 (Aspire)

## Code Style

| Rule              | Convention                                             |
|-------------------|--------------------------------------------------------|
| Braces            | K&R                                                    |
| Indentation       | 4 spaces C#, 2 spaces JSON/YAML/XML                    |
| Private fields    | `_camelCase`                                           |
| Variables         | `var` everywhere                                       |
| Expression bodies | Prefer `=>`                                            |
| Modifiers         | Omit `private`, `internal`                             |
| Test methods      | `snake_case`                                           |
| Async (library)   | `ConfigureAwait(false)` on ALL awaits                  |
| Nullability       | `T` = no null checks; `T?` only for valid domain state |
| DI patterns       | Use `Add*(sp => ...)` before creating factory classes  |

```csharp
public class OrderProcessor {
    readonly ILogger<OrderProcessor> _logger;

    public OrderProcessor(ILogger<OrderProcessor> logger) => _logger = logger;

    public async Task ProcessAsync(Order order, CancellationToken ct) {
        var result = await _service.HandleAsync(order, ct).ConfigureAwait(false);
        if (result.IsSuccess)
            _logger.LogInformation("Processed {OrderId}", order.Id);
    }
}
```

## Commands

```bash
# Build
dotnet build
dotnet build -c Release

# Observability
cd test/Kurrent.Client.Testing && docker compose up -d   # Start
cd test/Kurrent.Client.Testing && docker compose down    # Stop

# Package
dotnet pack -c Release

# Version
dotnet tool restore && dotnet tool run minver -- --tag-prefix=v

# Format
dotnet format --verify-no-changes
```

## Architecture Patterns

### Result Pattern
Return `Result<T>` instead of throwing for expected failures:
```csharp
public async Task<Result<Order>> GetOrderAsync(OrderId id) {
    var order = await _repository.FindAsync(id).ConfigureAwait(false);
    return order is null
        ? Result.Failure<Order>(Errors.OrderNotFound(id))
        : Result.Success(order);
}
```

### Discriminated Unions (IVariant)
```csharp
[Variant]
public partial record StreamState {
    partial record NoStream;
    partial record StreamExists(long Revision);
    partial record Any;
}

var position = state switch {
    StreamState.NoStream => ExpectedRevision.NoStream,
    StreamState.StreamExists(var rev) => ExpectedRevision.Revision(rev),
    StreamState.Any => ExpectedRevision.Any,
};
```

### Source-Generated Errors
```csharp
[Error("Stream '{StreamName}' not found")]
public static partial KurrentException StreamNotFound(string streamName);
```

## Troubleshooting

| Problem                 | Solution                                            |
|-------------------------|-----------------------------------------------------|
| Tests not discovered    | Use `dotnet run`, not `dotnet test`                 |
| Connection refused      | Start Docker: `docker compose up -d`                |
| SDK not found           | Install .NET 8.0, 9.0, or 10.0                      |
| Package restore failed  | `dotnet nuget locals all --clear && dotnet restore` |
| Wrong expected revision | Use `ExpectedRevision.Any` for unknown streams      |

## References

- `.editorconfig` - Full formatting rules
- `.project/project.md` - Architecture
- `CONTRIBUTING.md` - Guidelines
