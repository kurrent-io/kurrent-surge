using System.Runtime.ExceptionServices;

namespace Talyn;

/// <summary>
/// Callback delegate for fire-and-forget operations that receive a success/failure outcome.
/// </summary>
/// <param name="result">The outcome indicating success or containing an exception.</param>
/// <returns>A task that completes when callback processing is finished.</returns>
public delegate ValueTask OnOutcome(Outcome result);

/// <summary>
/// Callback delegate for fire-and-forget operations that receive a typed outcome.
/// </summary>
/// <typeparam name="T">The result type on success.</typeparam>
/// <param name="result">The outcome containing either a value or an exception.</param>
/// <returns>A task that completes when callback processing is finished.</returns>
public delegate ValueTask OnOutcome<T>(Outcome<T> result);

/// <summary>
/// Represents the result of an operation that can either succeed or fail with an exception.
/// </summary>
/// <remarks>
/// This is a lightweight, allocation-free result type that preserves the original exception stack trace
/// using <see cref="ExceptionDispatchInfo"/>. Use <see cref="ThrowIfFailed"/> to rethrow the original
/// exception with its stack trace intact.
/// </remarks>
public readonly struct Outcome {
    readonly ExceptionDispatchInfo? _edi;

    Outcome(ExceptionDispatchInfo? edi) => _edi = edi;

    /// <summary>
    /// <c>true</c> if the operation succeeded; <c>false</c> if it failed.
    /// </summary>
    public bool IsSuccess => _edi is null;

    /// <summary>
    /// The exception that caused the failure.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessing on a success outcome.</exception>
    public Exception Error => _edi?.SourceException ?? throw new InvalidOperationException("No error on success result");

    /// <summary>
    /// Creates a successful outcome.
    /// </summary>
    /// <returns>A success outcome.</returns>
    public static Outcome Success() => new(null);

    /// <summary>
    /// Creates a failed outcome from an exception.
    /// </summary>
    /// <param name="ex">The exception that caused the failure.</param>
    /// <returns>A failure outcome containing the exception.</returns>
    public static Outcome Failure(Exception ex) => new(ExceptionDispatchInfo.Capture(ex));

    /// <summary>
    /// Throws the captured exception if this outcome represents a failure.
    /// The original stack trace is preserved.
    /// </summary>
    public void ThrowIfFailed() => _edi?.Throw();
}

/// <summary>
/// Represents the result of an operation that can either succeed with a value or fail with an exception.
/// </summary>
/// <typeparam name="T">The result type on success.</typeparam>
/// <remarks>
/// This is a lightweight, allocation-free result type that preserves the original exception stack trace
/// using <see cref="ExceptionDispatchInfo"/>. Use <see cref="GetValueOrThrow"/> to get the value or
/// rethrow the original exception with its stack trace intact.
/// </remarks>
public readonly struct Outcome<T> {
    readonly T?                     _response;
    readonly ExceptionDispatchInfo? _edi;
    readonly bool                   _hasValue;

    Outcome(T response) {
        _response = response;
        _edi      = null;
        _hasValue = true;
    }

    Outcome(ExceptionDispatchInfo edi) {
        _response = default;
        _edi      = edi;
        _hasValue = false;
    }

    /// <summary>
    /// <c>true</c> if the operation succeeded; <c>false</c> if it failed.
    /// </summary>
    public bool IsSuccess => _hasValue;

    /// <summary>
    /// The response value on success.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessing on a failure outcome.</exception>
    public T Response => _hasValue ? _response! : throw new InvalidOperationException("No response on failure result");

    /// <summary>
    /// The exception that caused the failure.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessing on a success outcome.</exception>
    public Exception Error => _edi?.SourceException ?? throw new InvalidOperationException("No error on success result");

    /// <summary>
    /// Creates a successful outcome with the specified response.
    /// </summary>
    /// <param name="response">The response value.</param>
    /// <returns>A success outcome containing the response.</returns>
    public static Outcome<T> Success(T response) => new(response);

    /// <summary>
    /// Creates a failed outcome from an exception.
    /// </summary>
    /// <param name="ex">The exception that caused the failure.</param>
    /// <returns>A failure outcome containing the exception.</returns>
    public static Outcome<T> Failure(Exception ex) => new(ExceptionDispatchInfo.Capture(ex));

    /// <summary>
    /// Returns the response value if successful, or rethrows the captured exception.
    /// The original stack trace is preserved when rethrowing.
    /// </summary>
    /// <returns>The response value.</returns>
    public T GetValueOrThrow() {
        _edi?.Throw();
        return _response!;
    }
}

/// <summary>
/// Attributed response from a receiver with metadata about execution.
/// </summary>
/// <typeparam name="TResponse">The response type from the receiver.</typeparam>
/// <param name="ReceiverType">The type of the receiver that produced this response.</param>
/// <param name="Result">The outcome containing either the response value or an exception.</param>
/// <param name="Duration">The time taken by the receiver to process the request.</param>
public readonly record struct ReceiverReply<TResponse>(Type ReceiverType, Outcome<TResponse> Result, TimeSpan Duration) {
    /// <summary>
    /// Creates a successful reply from a response value.
    /// </summary>
    /// <param name="response">The response value.</param>
    /// <param name="receiverType">The type of the receiver.</param>
    /// <param name="start">The timestamp when processing started (from <see cref="TimeProvider.GetTimestamp"/>).</param>
    /// <returns>A successful receiver reply with the response and duration.</returns>
    public static ReceiverReply<TResponse> FromResult(TResponse response, Type receiverType, long start) =>
        new(
            receiverType,
            Outcome<TResponse>.Success(response),
            TimeProvider.System.GetElapsedTime(start)
        );

    /// <summary>
    /// Creates a failed reply from an exception.
    /// </summary>
    /// <param name="error">The exception that occurred.</param>
    /// <param name="receiverType">The type of the receiver.</param>
    /// <param name="start">The timestamp when processing started (from <see cref="TimeProvider.GetTimestamp"/>).</param>
    /// <returns>A failed receiver reply with the exception and duration.</returns>
    public static ReceiverReply<TResponse> FromError(Exception error, Type receiverType, long start) =>
        new(
            receiverType,
            Outcome<TResponse>.Failure(error),
            TimeProvider.System.GetElapsedTime(start)
        );
}
