# Error handling

<CopyMarkdownButton />

`Kurrent.Client` reports failures as exceptions. Operations throw well-typed exception classes you can catch by type. There is no Result wrapper or special error type.

## Throwing model

Every public method on `client.Streams` either returns a value (or `IAsyncEnumerable`) on success, or throws on failure. Catch by type when you want to handle a specific case, and let the rest propagate.

```csharp
try {
    await client.Streams.AppendStream(record, ExpectedStreamState.NoStream);
}
catch (StreamAlreadyExistsException) {
    // Stream already exists. Handle the duplicate case.
}
catch (StreamRevisionConflictException ex) {
    // Optimistic concurrency conflict.
    Console.WriteLine($"Expected {ex.ExpectedRevision}, was {ex.ActualRevision}");
}
catch (StreamTombstonedException) {
    // Stream was permanently deleted, so we cannot append.
}
```

## Common stream exceptions

These are the most frequently caught exceptions. All live under `Kurrent.Client.Streams`.

| Exception                                    | When it is thrown                                                                |
|----------------------------------------------|----------------------------------------------------------------------------------|
| `StreamNotFoundException`                    | Reading a stream that does not exist, or appending with `Exists` when it does not. |
| `StreamAlreadyExistsException`               | Appending with `NoStream` when the stream already has records.                   |
| `StreamDeletedException`                     | Reading a stream that was soft-deleted.                                          |
| `StreamTombstonedException`                  | Reading or writing a stream that was permanently deleted.                        |
| `StreamRevisionConflictException`            | Appending with an exact revision that does not match the current state.          |
| `LogPositionNotFoundException`               | `InspectRecord` or a global read referenced a position that does not exist.      |
| `MaxAppendRecordSizeExceededException`       | A single record exceeds the server's record size limit.                          |
| `MaxAppendTransactionSizeExceededException`  | A multi-record transaction exceeds the server's transaction size limit.          |

## Cancellation

`OperationCanceledException` (or `TaskCanceledException`) propagates whenever the cancellation token you pass into a call (or the enumeration of an `IAsyncEnumerable`) is cancelled. Treat it the same way you would any other cancellation in .NET. Let it bubble up unless you have a specific reason to swallow it.

## Validation errors

Builders (`AppendRecord.New`, `AppendStreamRequest.For`, `ReadStreamRequest.New`, etc.) call `Build()` synchronously. Invalid configurations throw `ValidationException` with a list of problems describing what is wrong:

```csharp
try {
    var record = AppendRecord.New
        .ForStream("orders")
        // missing WithValue(...)
        .Build();
}
catch (ValidationException ex) {
    foreach (var problem in ex.Problems) {
        Console.WriteLine($"{problem.Field}: {problem.Message}");
    }
}
```

Validation runs again when the request reaches the server, so passing a record built elsewhere still surfaces the same diagnostics.

## gRPC and connectivity errors

Errors that originate from the underlying gRPC channel (network failures, deadline exceeded, server errors that do not map to a domain exception) surface as `Grpc.Core.RpcException` or specific connectivity exceptions. The resilience profile (`KurrentClientResilienceOptions`) controls retries and deadlines for these categories. See [Connect › Tune resilience](./connect.md#tune-resilience).

## Subscriptions and async enumerables

Read methods return `IAsyncEnumerable<ReadRecordsResult>`. The exception is thrown the first time you `MoveNextAsync` (or iterate with `await foreach`), not at the call site:

```csharp
try {
    var request = ReadStreamRequest.New.ForStream("order-123").Build();

    await foreach (var item in client.Streams.ReadStream(request)) {
        // process record / heartbeat
    }
}
catch (StreamNotFoundException) {
    // Surfaced from the enumerator, not the call.
}
```

If you need to consume the stream defensively, wrap the enumeration loop, not the call to `ReadStream`.
