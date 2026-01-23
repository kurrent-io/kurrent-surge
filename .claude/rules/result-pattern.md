# Result Pattern (NO Exceptions for Flow Control)

## Public APIs MUST return Result types

```csharp
public async ValueTask<Result<AppendSuccess, AppendError>>
    AppendAsync(Request request, CancellationToken ct)
```

## Error Handling via Pattern Matching

```csharp
return result.Match(
    success => ProcessSuccess(success),
    error => error switch {
        StreamNotFoundException => HandleMissing(),
        _ => throw error.AsException()  // Last resort only
    }
);
```

## NEVER throw exceptions for expected failures
