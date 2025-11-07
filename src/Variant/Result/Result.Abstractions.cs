namespace Kurrent;

/// <summary>
/// Non-generic base interface for all result types, providing common properties for success/failure state checking.
/// </summary>
[PublicAPI]
public interface IResult {
    /// <summary>
    /// Gets a value indicating whether the operation was successful.
    /// </summary>
    bool IsSuccess { get; }
}

/// <summary>
/// Generic interface for result types that can either succeed with a value or fail with an error.
/// Provides type-safe access to success values and error values while maintaining the common result contract.
/// </summary>
/// <typeparam name="TValue">The type of the success value.</typeparam>
/// <typeparam name="TError">The type of the error value.</typeparam>
[PublicAPI]
public interface IResult<out TValue, out TError> : IResult  where TError : IResultError {
    /// <summary>
    /// Gets the success value.
    /// Throws an <see cref="InvalidOperationException"/> if the result is not a success.
    /// </summary>
    TValue Value { get; }

    /// <summary>
    /// Gets the error value.
    /// Throws an <see cref="InvalidOperationException"/> if the result is not an error.
    /// </summary>
    TError Error { get; }
}

/// <summary>
/// Marker interface to enable source generation of constructors and implicit operators.
/// </summary>
public interface IResultBase<out TValue, out TError> : IResult<TValue, TError> where TError : IResultError;
