---
paths:
  - "test/**/*.cs"
---

# TUnit Testing Rules

**CRITICAL: Use `dotnet run`, NOT `dotnet test`**

## Running Tests

```bash
# Integration tests (always start with net8.0)
dotnet run --project test/Kurrent.Client.Integration.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast --adaptive-metrics \
  --output Detailed --log-level Trace \
  --framework net8.0

# Filter by test class
dotnet run --project test/Kurrent.Client.Integration.Tests/ \
  --framework net8.0 --treenode-filter "/*/*/ReadTests/*"

# Unit tests
dotnet run --project test/Kurrent.Client.Tests/ \
  --framework net8.0
```

## Test Method Naming

- **MUST use snake_case** - `registers_initial_version_of_new_schema`
- NOT PascalCase

## Assertions (TUnit)

```csharp
await Assert.That(result).IsNotNull();
await Assert.That(result.Value).IsEqualTo(expected);
```

## Mocking (FakeItEasy)

```csharp
var fake = A.Fake<IService>();
A.CallTo(() => fake.Method(A<string>._)).Returns(result);
```
