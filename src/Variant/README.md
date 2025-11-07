# Variant: Functional Error Handling for Kurrent Client

Type-safe discriminated unions and functional error handling for .NET, designed specifically for the Kurrent Client library.

## What is Variant?

Variant provides two complementary systems:

1. **`Result<TValue, TError>`** - Functional error handling with explicit success/failure types
2. **`IVariant`** - Discriminated unions for representing one-of-many type scenarios

The primary use case is `Result<TValue, TError>` where `TError : IVariantException`, enabling type-safe error handling with multiple exception types.

**Key Benefits:**
- **Type Safety**: Errors are explicit in method signatures
- **Performance**: Zero-allocation struct-based design
- **Composability**: Chain operations that can fail
- **Pattern Matching**: Exhaustive matching on all error cases

## Quick Start

### Creating and Using Results

```csharp
using Kurrent.Variant;

// Define error type with multiple exception variants
public partial class AppendSessionError : IVariantException<
    StreamNotFoundException,
    StreamRevisionConflictException,
    StreamTombstonedException,
    KurrentException>
{ }

// Return Result from your operation
public async ValueTask<Result<AppendSessionSuccess, AppendSessionError>>
    BeginAppendSessionAsync(string stream) {

    // Success case
    if (success) {
        return new AppendSessionSuccess(stream, position, revision);
    }

    // Failure case
    return new StreamNotFoundException(stream);  // Implicit conversion
}
```

### Pattern Matching Results

```csharp
var result = await client.BeginAppendSessionAsync("my-stream");

// Transform result to a value
var message = result.Match(
    onSuccess: success => $"Appended at position {success.Position}",
    onFailure: error => $"Failed: {error.ErrorMessage}"
);

// Execute side effects
result.Switch(
    onSuccess: success => _logger.LogInfo($"Success: {success.Position}"),
    onFailure: error => _logger.LogError($"Error: {error.ErrorMessage}")
);
```

### Pattern Matching on Error Types

```csharp
result.Match(
    onSuccess: success => ProcessSuccess(success),
    onFailure: error => error.Match(
        case1: (StreamNotFoundException e) =>
            Console.WriteLine($"Stream '{e.StreamName}' not found"),
        case2: (StreamRevisionConflictException e) =>
            Console.WriteLine($"Conflict: expected {e.ExpectedRevision}, got {e.ActualRevision}"),
        case3: (StreamTombstonedException e) =>
            Console.WriteLine($"Stream '{e.StreamName}' is tombstoned"),
        case4: (KurrentException e) =>
            Console.WriteLine($"Server error: {e.Message}")
    )
);
```

### Fluent Chaining with Side Effects

```csharp
return await client.BeginAppendSessionAsync(stream)
    .OnSuccess(success => _metrics.RecordSuccess())
    .OnFailure(error => _logger.LogError(error))
    .Match(
        onSuccess: success => Ok(success),
        onFailure: error => StatusCode(500, error.ErrorMessage)
    );
```

## Result<TValue, TError> API

### Creating Results

```csharp
// Factory methods
var success = Result.Success<User, MyError>(user);
var failure = Result.Failure<User, MyError>(error);

// Implicit conversions
Result<User, MyError> result = user;   // Success
Result<User, MyError> result = error;  // Failure
```

### Checking Result State

```csharp
if (result.IsSuccess) {
    var value = result.Value;  // Safe access
}

if (result.IsFailure) {
    var error = result.Error;  // Safe access
}

// Implicit bool conversion
if (result) {  // true if success
    Console.WriteLine("Success!");
}
```

### Pattern Matching Operations

#### Match - Transform the Result

Transform based on success or failure:

```csharp
// Synchronous transformation
string message = result.Match(
    onSuccess: user  => $"Hello {user.Name}",
    onFailure: error => $"Error: {error.ErrorMessage}"
);

// Async transformation
var response = await result.MatchAsync(
    onSuccess: async user  => await GenerateWelcomeAsync(user),
    onFailure: async error => await GenerateErrorAsync(error)
);

// With state (avoids closure allocations)
var message = result.Match(
    onSuccess: (logger, user) => {
        logger.LogInfo(user);
        return "OK";
    },
    onFailure: (logger, error) => {
        logger.LogError(error);
        return "Failed";
    },
    state: _logger
);
```

#### Switch - Execute Side Effects

Execute actions without transforming the result:

```csharp
// Synchronous side effects
result.Switch(
    onSuccess: user  => Console.WriteLine($"User: {user.Name}"),
    onFailure: error => Console.WriteLine($"Error: {error.ErrorMessage}")
);

// Async side effects
await result.SwitchAsync(
    onSuccess: async user  => await NotifySuccessAsync(user),
    onFailure: async error => await NotifyErrorAsync(error)
);
```

#### OnSuccess / OnFailure - Conditional Execution

Execute actions conditionally while preserving the result for chaining:

```csharp
// Fluent chaining
var result = await operation()
    .OnSuccess(v => _logger.LogInfo($"Success: {v}"))
    .OnFailure(e => _logger.LogError($"Failed: {e}"));

// Async variants
await result
    .OnSuccessAsync(async v => await NotifyAsync(v))
    .OnFailureAsync(async e => await LogAsync(e));

return result.Match(
    onSuccess: v => Ok(v),
    onFailure: e => BadRequest(e)
);
```

### Working with Async Operations

All operations integrate seamlessly with `ValueTask`:

```csharp
// Method returning async Result
public async ValueTask<Result<User, AppError>> GetUserAsync(int id) {
    var user = await _database.QueryAsync(id);
    return user ?? Result.Failure<User, AppError>(AppError.NotFound());
}

// Chain async operations without awaiting intermediate results
var message = await GetUserAsync(123)
    .MatchAsync(
        onSuccess: user  => $"Found: {user.Name}",
        onFailure: error => $"Error: {error.ErrorMessage}"
    );

// Chain side effects
await GetUserAsync(123)
    .OnSuccessAsync(user  => _logger.LogInfo($"Found: {user.Name}"))
    .OnFailureAsync(error => _logger.LogError($"Not found: {error.ErrorMessage}"));
```

### Exception Conversion

#### Try Pattern - Exceptions → Results

Convert exception-throwing code to Results:

```csharp
// Synchronous try
var result = Result.Try(
    operation: () => JsonSerializer.Deserialize<Config>(json),
    onError: ex => new ExceptionError(ex)
);

// Async try
var result = await Result.TryAsync(
    operation: async () => await httpClient.GetAsync(url),
    onError: ex => new ExceptionError(ex)
);

// With state (no closures)
var result = Result.Try(
    operation: logger => RiskyOperation(logger),
    onError: (logger, ex) => {
        logger.LogError(ex);
        return new ExceptionError(ex);
    },
    state: _logger
);

// Void operations return Success
var result = Result.Try(
    operation: () => File.Delete(tempFile),
    onError: ex => new ExceptionError(ex)
);  // Result<Success, ExceptionError>
```

#### Unwrap - Results → Exceptions

Convert Results back to exceptions at API boundaries:

```csharp
// For IResultError types
public string GetValue() {
    var result = GetResult();  // Returns Result<string, MyError>
    return result.Unwrap();    // Throws exception on failure
}

// For IVariantException types (throws the specific exception)
public string GetValue() {
    var result = GetResult();  // Returns Result<string, AppendError>
    return result.UnwrapVariant();  // Throws the appropriate exception
}

// Async variants
public async Task<string> GetValueAsync() {
    return await GetResultAsync().UnwrapAsync();
}
```

### Functional Operations

#### Map - Transform Success Value

```csharp
// Transform success value, preserve error
Result<string, Error> nameResult = userResult.Map(user => user.Name);

// Async variant
Result<string, Error> nameResult = await userResult.MapAsync(
    async user => await FormatNameAsync(user)
);
```

#### MapError - Transform Error Value

```csharp
// Transform error, preserve success value
Result<User, AppError> result = dbResult.MapError(
    dbError => new AppError(dbError.Message)
);
```

#### Bind - Chain Result-Returning Operations

```csharp
// Monadic bind (FlatMap) for railway-oriented programming
var finalResult = await ValidateUser(data)
    .Bind(user => SaveUser(user))
    .Bind(saved => NotifyUser(saved));

// Each operation returns Result<T, Error>
// Errors short-circuit the chain
```

## IVariant System

### What are Discriminated Unions?

A discriminated union represents a value that can be one of several different types. C# lacks native support for this, but Variant provides type-safe discriminated unions through the `IVariant` interface and source generators.

```csharp
// A variant can hold one of multiple types
IVariant<string, int, bool> value;

// Pattern match on which type it holds
value.Match(
    case1: str  => Console.WriteLine($"String: {str}"),
    case2: num  => Console.WriteLine($"Number: {num}"),
    case3: flag => Console.WriteLine($"Bool: {flag}")
);
```

### IVariantException - Discriminated Union of Exceptions

The most important use case for Variant in Kurrent Client is `IVariantException`, which combines discriminated unions with exception handling:

```csharp
// Define a variant exception type (source generator creates implementation)
public partial class AppendSessionError : IVariantException<
    StreamNotFoundException,
    StreamRevisionConflictException,
    StreamTombstonedException,
    MaxAppendRecordSizeExceededException,
    KurrentException>;
```

**Key Features:**
- Implements `IResultError`, so can be used as error type in `Result<TValue, TError>`
- Holds one of the specified exception types
- Delegates `ErrorCode` and `ErrorMessage` to the contained exception
- Enables type-safe pattern matching on all exception cases

**Why This Matters:**

A single operation might fail with multiple different exception types. Instead of catching and re-throwing, you return a Result with a variant exception:

```csharp
public async ValueTask<Result<AppendSuccess, AppendSessionError>> AppendAsync(...) {
    try {
        var success = await PerformAppend(...);
        return success;  // Implicit conversion to Result
    }
    catch (StreamNotFoundException ex) {
        return ex;  // Implicit conversion to AppendSessionError, then to Result
    }
    catch (StreamRevisionConflictException ex) {
        return ex;  // Same pattern for all exception types
    }
    // ... handle other exceptions
}
```

### Defining Error Types for Your Operations

```csharp
// Simple error type (single exception)
public partial class CreateSchemaError : IVariantException<KurrentException> { }

// Complex error type (multiple exceptions)
public partial class AppendSessionError : IVariantException<
    StreamNotFoundException,
    StreamAlreadyExistsException,
    StreamTombstonedException,
    StreamRevisionConflictException,
    StreamAlreadyInAppendSessionException,
    MaxAppendRecordSizeExceededException,
    MaxAppendTransactionSizeExceededException,
    AppendSessionNoRequestsException,
    KurrentException>;
```

## Real-World Patterns

### Stream Operations Example

```csharp
public class StreamsClient {
    public async ValueTask<Result<AppendSessionSuccess, AppendSessionError>>
        BeginAppendSessionAsync(string stream) {

        // Validate
        if (string.IsNullOrEmpty(stream)) {
            return new ArgumentException("Stream name required");
        }

        // Perform operation
        var result = await Result.TryAsync(
            operation: async () => await _grpcClient.BeginAppendAsync(stream),
            onError: ex => AppendSessionError.FromException(ex)
        );

        return result;
    }
}

// Usage
var result = await client.BeginAppendSessionAsync("orders");

result.Match(
    onSuccess: success => ProcessAppend(success),
    onFailure: error => error.Match(
        case1: (StreamNotFoundException e) => HandleNotFound(e.StreamName),
        case2: (StreamRevisionConflictException e) => RetryWithRevision(e.ActualRevision),
        case3: (StreamTombstonedException e) => HandleTombstoned(e.StreamName),
        // ... handle other error cases
    )
);
```

### Registry Operations Example

```csharp
public class RegistryClient {
    public async ValueTask<Result<Schema, CreateSchemaError>>
        CreateSchemaAsync(SchemaDefinition definition) {

        var result = await Result.TryAsync(
            operation: async () => await _grpcClient.CreateSchemaAsync(definition),
            onError: ex => new CreateSchemaError { Case1 = (KurrentException)ex }
        );

        return result;
    }
}

// Usage with fluent chaining
return await registry.CreateSchemaAsync(schemaDefinition)
    .OnSuccess(schema => _logger.LogInfo($"Created schema: {schema.Name}"))
    .OnFailure(error => _logger.LogError($"Failed: {error.ErrorMessage}"))
    .Match(
        onSuccess: schema => Ok(schema),
        onFailure: error => StatusCode(500, error.ErrorMessage)
    );
```

### Railway-Oriented Programming

Chain multiple operations that can fail:

```csharp
var result = await ValidateInput(data)
    .Bind(valid => TransformData(valid))
    .Bind(transformed => SaveToDatabase(transformed))
    .Bind(saved => PublishEvent(saved));

result.Match(
    onSuccess: final => Ok(final),
    onFailure: error => BadRequest(error)  // Error from any step
);
```

### Early Return Pattern

For more imperative code:

```csharp
public async ValueTask<Result<ProcessedData, AppError>> ProcessAsync(InputData data) {
    var validateResult = ValidateInput(data);
    if (validateResult.IsFailure) {
        return validateResult.Error;  // Early return
    }

    var transformResult = await TransformAsync(validateResult.Value);
    if (transformResult.IsFailure) {
        return transformResult.Error;  // Early return
    }

    return await SaveAsync(transformResult.Value);
}
```

### Logging and Metrics Pattern

Use OnSuccess/OnFailure for observability without breaking the chain:

```csharp
return await operation()
    .OnSuccess(v => {
        _metrics.IncrementSuccessCount();
        _logger.LogInfo($"Operation succeeded: {v}");
    })
    .OnFailure(e => {
        _metrics.IncrementFailureCount();
        _logger.LogError($"Operation failed: {e.ErrorMessage}");
    })
    .Match(
        onSuccess: v => Ok(v),
        onFailure: e => MapToHttpResponse(e)
    );
```

## API Quick Reference

### Result Creation
```csharp
Result.Success<TValue, TError>(value)
Result.Failure<TValue, TError>(error)
// Implicit: TValue → Result<TValue, TError>
// Implicit: TError → Result<TValue, TError>
```

### Result Checking
```csharp
result.IsSuccess      // bool
result.IsFailure      // bool
result.Value          // TValue (throws if failure)
result.Error          // TError (throws if success)
```

### Pattern Matching
```csharp
result.Match(onSuccess, onFailure)
result.Match(onSuccess, onFailure, state)
await result.MatchAsync(onSuccess, onFailure)
await resultTask.MatchAsync(onSuccess, onFailure)
```

### Side Effects
```csharp
result.Switch(onSuccess, onFailure)
await result.SwitchAsync(onSuccess, onFailure)
result.OnSuccess(action)
result.OnFailure(action)
await result.OnSuccessAsync(action)
await result.OnFailureAsync(action)
```

### Functional Operations
```csharp
result.Map(transform)
result.MapError(transform)
result.Bind(operation)
await result.BindAsync(operation)
```

### Exception Conversion
```csharp
Result.Try(operation, onError)
await Result.TryAsync(operation, onError)
result.Unwrap()  // IResultError
result.UnwrapVariant()  // IVariantException
await resultTask.UnwrapAsync()
```

## Performance Notes

### Result<TValue, TError> is a Struct
- **Stack allocated** - zero heap allocation for success/failure cases
- **Value semantics** - copied by value, immutable
- **Size**: ~24 bytes (nullable TValue + nullable TError + bool)
- **Best for**: Return types, hot paths, high-throughput scenarios

### ValueTask Integration
- All async operations use `ValueTask` for optimal performance
- Minimal allocations for successful fast paths
- Efficient chaining without intermediate awaits

### When to Use Result vs Exceptions

**Use Results when:**
- Error is an expected outcome (validation, not found, conflict)
- You need to compose multiple operations that can fail
- Performance is critical (hot paths)
- You want explicit error handling in the type signature

**Use Exceptions when:**
- Error is truly exceptional (programming errors, system failures)
- Interoperating with legacy code that expects exceptions
- At API boundaries where exceptions are expected
- Converting back with `Unwrap()` or `UnwrapVariant()`

## Error Contracts

### IResultError Interface

All error types must implement `IResultError`:

```csharp
public interface IResultError {
    ErrorCode ErrorCode { get; }
    string ErrorMessage { get; }
    ImmutableDictionary<string, object?> Metadata { get; }
    Exception GetOrCreateException();
    void Throw();
}
```

### Built-in Error Types

**`ResultError`** - Basic implementation with fluent builder:
```csharp
var error = new ResultError()
    .WithCode("USER_NOT_FOUND")
    .WithMessage("User with ID 123 not found")
    .WithMetadata("userId", 123);
```

**`ExceptionError`** - Wraps any exception:
```csharp
var error = new ExceptionError(exception);
// Preserves stack trace for re-throwing
```

**`ErrorCode`** - Strongly-typed error codes:
```csharp
var code = ErrorCode.FromType<StreamNotFoundException>();
// Generates: "STREAM_NOT_FOUND_EXCEPTION"
```

## Contributing

Variant is a supporting library for the Kurrent Client project. For questions or contributions, please reach out to the Kurrent development team.

### Source Code
- **Repository**: Part of kurrent-client monorepo
- **Location**: `src/Variant/`
- **Target Frameworks**: net8.0, net9.0, net10.0

### Source Generators
Variant relies on source generators to create implementations of `IVariant` and `IVariantException` types. 
The `partial` keyword is required on variant type definitions.

---
