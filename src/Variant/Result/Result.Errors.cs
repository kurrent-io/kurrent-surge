using System.Collections;
using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using static System.StringComparison;

namespace Kurrent;

/// <summary>
/// Contract for types that provide structured error information to Result types.
/// Implementing this interface enables use as error type in <see cref="Result{TValue, TError}"/>.
/// </summary>
[PublicAPI]
public interface IResultError {
    /// <summary>
    /// Optional unique code identifying the error type.
    /// </summary>
    ErrorCode ErrorCode { get; }

    /// <summary>
    /// Human-readable description of the error.
    /// </summary>
    string ErrorMessage { get; }

    /// <summary>
    /// Metadata associated with this error.
    /// </summary>
    ImmutableDictionary<string, object?> Metadata { get; }

    /// <summary>
    /// Gets the existing exception or creates a new one based on the error information.
    /// </summary>
    Exception GetOrCreateException();

    /// <summary>
    /// Re-throws the exception with its preserved stack trace.
    /// </summary>
    void Throw() {
        var exception = GetOrCreateException();

        if (exception is CapturedException captured)
            captured.Throw();

        throw exception;
    }
}

[PublicAPI]
public record ResultError : IResultError {
    public static readonly string ReservedExceptionKey = "$ResultException";
    public static readonly string ReservedErrorCodeKey = "$ResultErrorCode";

    /// <summary>
    /// Optional error code. Empty string by default.
    /// </summary>
    public virtual ErrorCode ErrorCode { get; protected init; } = ErrorCode.None;

    /// <summary>
    /// Error message. Empty string by default.
    /// </summary>
    public virtual string ErrorMessage { get; protected init; } = "";

    /// <summary>
    /// Metadata dictionary.
    /// </summary>
    public ImmutableDictionary<string, object?> Metadata { get; protected init; } = ImmutableDictionary<string, object?>.Empty;

    /// <summary>
    /// Gets the existing exception or creates a new one based on the error information.
    /// </summary>
    public virtual Exception GetOrCreateException() {
        // Check for captured exception
        if (Metadata.GetValueOrDefault(ReservedExceptionKey) is Exception ex)
            return ex;

        // Create new exception if none stored
        ex = new Exception($"Operation failed with error code '{ErrorCode}': {ErrorMessage}") {
            Data = { [ReservedErrorCodeKey] = ErrorCode }
        };

        foreach (var (key, value) in Metadata)
            ex.Data[key] = value;

        return ex;
    }

    /// <summary>
    /// Re-throws the original exception with its preserved stack trace.
    /// </summary>
    public void Throw() {
        var exception = GetOrCreateException();

        if (exception is CapturedException captured)
            captured.Throw();

        throw exception;
    }

    public static IResultError Create(ErrorCode errorCode, string errorMessage, Dictionary<string, object?>? metadata = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        var meta = (metadata is not null ? EnsureMetadataIsValid(metadata) : ImmutableDictionary<string, object?>.Empty)
            .Add(ReservedErrorCodeKey, errorCode);

        return new ResultError {
            ErrorCode    = errorCode,
            ErrorMessage = errorMessage,
            Metadata     = meta.Add(ReservedErrorCodeKey, errorCode)
        };
    }

    public static IResultError Create(ExceptionDispatchInfo info, ErrorCode? errorCode = null) {
        var ex = new CapturedException(info);

        var code = errorCode is null || errorCode == ErrorCode.None
            ? ErrorCode.FromEx(ex)
            : errorCode.GetValueOrDefault();

        var meta = ImmutableDictionary<string, object?>.Empty
            .Add(ReservedErrorCodeKey, code)
            .Add(ReservedExceptionKey, ex);

        return new ResultError {
            ErrorCode    = code,
            ErrorMessage = ex.Message,
            Metadata     = meta
        };
    }

    public static IResultError Create(Exception exception, ErrorCode? errorCode = null) =>
        Create(ExceptionDispatchInfo.Capture(exception), errorCode);

    static ImmutableDictionary<string, object?> EnsureMetadataIsValid(Dictionary<string, object?> dictionary) {
        return dictionary.ToImmutableDictionary(
            static x => {
                if (string.IsNullOrWhiteSpace(x.Key))
                    throw new ArgumentException("Metadata dictionary must not contain empty or whitespace keys.");

                if (x.Key.Equals(ReservedExceptionKey))
                    throw new ArgumentException($"Metadata dictionary must not contain reserved key '{ReservedExceptionKey}'.");

                if (x.Key.Equals(ReservedErrorCodeKey))
                    throw new ArgumentException($"Metadata dictionary must not contain reserved key '{ReservedErrorCodeKey}'.");

                return x.Key;
            },
            static x => x.Value
        );
    }
}

/// <summary>
/// Represents a normalized error code. Error codes must:
/// <list type="bullet">
/// <item><description>Be between 3 and 63 characters in length.</description></item>
/// <item><description>Match the UPPER_SNAKE_CASE pattern (only uppercase letters, digits, and underscores; must start with a letter and end with a letter or digit).</description></item>
/// </list>
/// </summary>
public readonly partial record struct ErrorCode {
    public static readonly ErrorCode None    = new("");
    public static readonly ErrorCode Unknown = new("UNKNOWN");

    const string Pattern = "^[A-Z][A-Z0-9_]{1,61}[A-Z0-9]$";

    [GeneratedRegex(Pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex Regex();

    ErrorCode(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// Creates an <see cref="ErrorCode"/> from the given string value, normalizing it to UPPER_SNAKE_CASE
    /// and validating its format.
    /// </summary>
    public static ErrorCode From(string value) {
        var code = value
            .Trim()
            .Replace("Exception", "", OrdinalIgnoreCase)
            .Replace("ErrorCode", "", OrdinalIgnoreCase)
            .Replace("ErrorResult", "", OrdinalIgnoreCase)
            .Replace("ResultError", "", OrdinalIgnoreCase)
            .Replace("Error", "", OrdinalIgnoreCase)
            .Underscore()
            .ToUpperInvariant();

        // Regex now handles both pattern AND length validation
        if (!Regex().IsMatch(code))
            throw new InvalidErrorCodeException(
                $"Error code is invalid: {value}. " +
                $"It must be 3-63 characters and match UPPER_SNAKE_CASE pattern."
            );

        return new ErrorCode(code);
    }

    public static ErrorCode FromType(Type type) => From(type.Name);
    public static ErrorCode FromType<T>()       => FromType(typeof(T));

    public static ErrorCode FromEx(Exception ex) => FromType(ex.GetType());

    public static ErrorCode FromEnum<T>(T value) where T : struct, Enum      => From(Enum.GetName(value)!);
    public static ErrorCode FromEnum<T>(string value) where T : struct, Enum => FromEnum(Enum.Parse<T>(value));
    public static ErrorCode FromEnum<T>(int value) where T : struct, Enum    => FromEnum<T>(value.ToString());

    public static implicit operator string(ErrorCode code)  => code.Value;
    public static implicit operator ErrorCode(string value) => From(value);
}

public sealed class InvalidErrorCodeException(string message) : ArgumentException(message);

/// <summary>
/// Wraps an exception with preserved stack trace information for re-throwing.
/// </summary>
[PublicAPI]
public sealed class CapturedException(ExceptionDispatchInfo dispatchInfo) : Exception {
    /// <summary>
    /// Re-throws the original exception with its preserved stack trace.
    /// </summary>
    public void Throw() => dispatchInfo.Throw();

    public Exception SourceException => dispatchInfo.SourceException;

    #region ->> overrides that delegate to captured ex <<-

    public override string      Message    => dispatchInfo.SourceException.Message;
    public override string?     StackTrace => dispatchInfo.SourceException.StackTrace;
    public override string?     Source     => dispatchInfo.SourceException.Source;
    public override IDictionary Data       => dispatchInfo.SourceException.Data;
    public override string?     HelpLink   => dispatchInfo.SourceException.HelpLink;

    public override Exception GetBaseException() => dispatchInfo.SourceException.GetBaseException();
    public override string    ToString()         => dispatchInfo.SourceException.ToString();

    #endregion
}

/// <summary>
/// Wraps an exception for fluent Try API usage.
/// Preserves stack trace via ExceptionDispatchInfo.
/// </summary>
[PublicAPI]
public sealed record ExceptionError : ResultError {
    public ExceptionError(ExceptionDispatchInfo info) {
        Captured     = new CapturedException(info);
        ErrorCode    = ErrorCode.FromEx(info.SourceException);
        ErrorMessage = info.SourceException.Message;
    }

    public ExceptionError(Exception exception)
        : this(ExceptionDispatchInfo.Capture(exception)) { }

    CapturedException Captured { get; }

    public override Exception GetOrCreateException() => Captured;

    public static implicit operator ExceptionError(Exception ex) => new(ex);
    public static implicit operator ExceptionError(ExceptionDispatchInfo info) => new(info);
}
