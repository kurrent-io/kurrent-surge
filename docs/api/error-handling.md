---
order: 3
---

# Error handling

Kurrent.Client uses the **Result pattern** for error handling. Public APIs return `Result<TSuccess, TError>` instead of throwing exceptions for expected failures. This makes error paths explicit, composable, and free of exception overhead.

## Two API styles

Every operation is available in two styles:

### Idiomatic (throwing)

Methods ending in `Async` throw on failure. Use when you want concise code and are fine catching exceptions at a higher level:

```csharp
// Throws if the stream doesn't exist or another error occurs
var result = await client.Streams.AppendAsync("order-123", record);
Console.WriteLine($"Revision: {result.StreamRevision}");
```

### Explicit (Result-based)

Methods without the `Async` suffix return `Result<TSuccess, TError>`. Use when you need to handle specific error cases without exceptions:

```csharp
var result = await client.Streams.Append(
    new AppendStreamRequest("order-123", ExpectedStreamCondition.NoStream, [record])
);

result.Switch(
    success => Console.WriteLine($"Revision: {success.StreamRevision}"),
    error => HandleError(error)
);
```

Choose the style that fits the call site. Both are equally supported — the idiomatic methods are convenience wrappers that call `.UnwrapAsync()` on the Result internally.

## Working with Result

`Result<TValue, TError>` is a discriminated union — it holds either a success value or an error, never both.

### Switch — side effects

Execute an action based on the outcome. Returns `void`:

```csharp
result.Switch(
    success => Console.WriteLine($"Done: {success}"),
    error => Console.WriteLine($"Failed: {error}")
);
```

Async variant:

```csharp
await result.SwitchAsync(
    async success => await NotifyAsync(success),
    async error => await LogErrorAsync(error)
);
```

### Match — transform

Transform the result into a new value. Both branches must return the same type:

```csharp
var message = result.Match(
    success => $"Appended at revision {success.StreamRevision}",
    error => $"Failed: {error}"
);
```

Async variant:

```csharp
var response = await result.MatchAsync(
    async success => await BuildResponseAsync(success),
    async error => await BuildErrorResponseAsync(error)
);
```

### Map — transform the success value

Transform the success value while preserving errors unchanged:

```csharp
Result<string, AppendSessionError> mapped = result.Map(
    success => $"Written to {success.Stream} at revision {success.StreamRevision}"
);
```

Map with a constant:

```csharp
Result<bool, AppendSessionError> mapped = result.Map(true);
```

### MapError — transform the error

Transform the error while preserving the success value:

```csharp
Result<AppendStreamSuccess, string> mapped = result.MapError(
    error => error.ToString()
);
```

### Bind — chain operations

Chain Result-returning operations. Short-circuits on the first failure:

```csharp
var final = await client.Streams.Append(firstRequest)
    .Bind(success => client.Streams.Append(secondRequest));
```

If the first append fails, the second is never executed and the error propagates.

### Inspect — side effects without transformation

Run a side effect on success or failure without changing the result. Useful for logging:

```csharp
var result = await client.Streams.Append(request)
    .Inspect(success => logger.LogInformation("Appended to {Stream}", success.Stream))
    .InspectError(error => logger.LogWarning("Append failed: {Error}", error));
```

### Unwrap — convert to exception

Extract the success value or throw the error as an exception:

```csharp
// Throws if result is failure
var success = await result.UnwrapAsync();
```

This is what the idiomatic `Async` methods use internally.

### Properties

Check the result state directly:

```csharp
if (result.IsSuccess) {
    var value = result.Value;
}

if (result.IsFailure) {
    var error = result.Error;
}
```

### Deconstruction

```csharp
var (isSuccess, value, error) = result;

if (isSuccess)
    Console.WriteLine($"Value: {value}");
else
    Console.WriteLine($"Error: {error}");
```

## Handling variant errors

Error types in Kurrent.Client are **variant types** — discriminated unions over specific exception types. Use `Switch` to handle each case:

```csharp
var result = await client.Streams.Append(request);

result.Switch(
    success => ProcessSuccess(success),
    error => error.Switch(
        streamNotFound      => HandleMissing(),
        streamAlreadyExists => HandleDuplicate(),
        streamTombstoned    => HandleTombstoned(),
        revisionConflict    => HandleConflict(revisionConflict),
        alreadyInSession    => HandleSessionConflict(),
        maxRecordSize       => HandleSizeLimit(),
        maxTransactionSize  => HandleTransactionLimit(),
        noRequests          => HandleEmpty(),
        exception           => HandleUnexpected(exception)
    )
);
```

Each error variant is a specific exception type. The compiler ensures you handle all cases — no missing branches.

### Partial handling

Handle only the cases you care about and let others throw:

```csharp
result.Switch(
    success => ProcessSuccess(success),
    error => error.Switch(
        revisionConflict => RetryWithNewRevision(revisionConflict),
        _                => throw error.AsException()  // re-throw everything else
    )
);
```

## Result.Try — exception-to-Result conversion

Wrap code that may throw into a Result:

```csharp
var result = Result.Try(
    () => SomeOperationThatMayThrow(),
    exception => new MyError(exception.Message)
);
```

Async variant:

```csharp
var result = await Result.TryAsync(
    async () => await SomeAsyncOperation(),
    exception => new MyError(exception.Message)
);
```

For void operations:

```csharp
Result<Success, MyError> result = Result.Try(
    () => DoSomething(),
    exception => new MyError(exception.Message)
);
```

## Success marker types

Operations that succeed without a meaningful return value use zero-sized marker types:

```csharp
Result<Success, TruncateStreamError> result = await client.Streams.Truncate(...);

result.Switch(
    success => Console.WriteLine("Truncated"),
    error => HandleError(error)
);
```

Available markers: `Success`, `Created`, `Deleted`, `Updated`.

## When to use which style

| Scenario | Recommended Style |
|----------|-------------------|
| Simple scripts, prototyping | Idiomatic (`Async` methods) |
| Web API controllers | Either — catch exceptions or match results |
| Domain logic with specific error recovery | Result-based (explicit handling) |
| Retry loops on revision conflicts | Result-based (inspect error type) |
| Fire-and-forget operations | Idiomatic (let it throw) |
| Pipelines chaining multiple operations | Result-based (`Bind`, `Map`) |
