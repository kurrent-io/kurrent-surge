namespace Kurrent;

public static partial class FunctionalResult {
    /// <summary>
    /// Unwraps the result, returning the value on success or throwing an exception on failure.
    /// </summary>
    public static TValue Unwrap<TValue, TError>(
        this Result<TValue, TError> result)
        where TError : IResultError {
        if (result.IsSuccess)
            return result.Value;

        var exception = result.Error.GetOrCreateException();
        if (exception is CapturedException captured)
            captured.Throw();

        throw exception;
    }

    /// <summary>
    /// Unwraps the result, returning the value on success or throwing an  exception on failure.
    /// </summary>
    public static async ValueTask<TValue> UnwrapAsync<TValue, TError>(
        this ValueTask<Result<TValue, TError>> result)
        where TError : IResultError =>
        (await result.ConfigureAwait(false)).Unwrap();

    /// <summary>
    /// Unwraps the result, returning the value on success or throwing the contained exception on failure.
    /// </summary>
    [Obsolete("Use Unwrap() instead. This method will be removed in a future version.")]
    public static TValue ThrowOnFailure<TValue, TError>(this Result<TValue, TError> result)
        where TError : IResultError =>
        result.Unwrap();

    /// <summary>
    /// Asynchronously unwraps the result, throwing the contained exception on failure.
    /// </summary>
    [Obsolete("Use UnwrapAsync() instead. This method will be removed in a future version.")]
    public static ValueTask<TValue> ThrowOnFailureAsync<TValue, TError>(this ValueTask<Result<TValue, TError>> result)
        where TError : IResultError =>
        result.UnwrapAsync();

    /// <summary>
    /// Completes the given TaskCompletionSource based on the Result.
    /// </summary>
    /// <param name="result"></param>
    /// <param name="tcs"></param>
    /// <typeparam name="TValue"></typeparam>
    /// <typeparam name="TError"></typeparam>
    public static void CompleteTask<TValue, TError>(
        this Result<TValue, TError> result, TaskCompletionSource<TValue> tcs)
        where TError : IResultError {
        if (result.IsSuccess)
            tcs.SetResult(result.Value);
        else {
            var exception = result.Error.GetOrCreateException();

            if (exception is CapturedException { SourceException: var source })
                exception = source;

            if (exception is OperationCanceledException canceled)
                tcs.SetCanceled(canceled.CancellationToken);
            else
                tcs.SetException(exception);
        }
    }

    /// <summary>
    /// Asynchronously completes the given TaskCompletionSource based on the Result.
    /// </summary>
    /// <param name="result"></param>
    /// <param name="tcs"></param>
    /// <typeparam name="TValue"></typeparam>
    /// <typeparam name="TError"></typeparam>
    public static async ValueTask CompleteTask<TValue, TError>(
        this ValueTask<Result<TValue, TError>> result, TaskCompletionSource<TValue> tcs)
        where TError : IResultError {
        (await result.ConfigureAwait(false)).CompleteTask(tcs);
    }
}
