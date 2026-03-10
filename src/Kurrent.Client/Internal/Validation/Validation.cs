using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Humanizer;
using Kurrent.Client.Exceptions;

namespace Kurrent.Validation;

public sealed partial record ValidationResult : IResultBase<Success, ValidationError>;

[PublicAPI]
[DebuggerDisplay("{ErrorCode} | {ErrorMessage}")]
public record  ValidationError : ResultError {
    public static readonly ValidationError None = new(ErrorCode.None, "");

    public ValidationError(ErrorCode errorCode, string errorMessage, params ValidationProblem[] problems) {
        ErrorCode    = errorCode;
        ErrorMessage = errorMessage;
        Problems     = problems;
    }

    /// <summary>
    /// The validation problems that occurred during the validation process.
    /// </summary>
    public ValidationProblem[] Problems { get; private init; }

    public override Exception GetOrCreateException() =>
        new ValidationException(ErrorCode, ErrorMessage, Problems);

    public static ValidationError Create(ErrorCode code, params ValidationProblem[] problems) {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(problems.Length, 0);

        return new ValidationError(code, BuildErrorMessage(), problems);

        string BuildErrorMessage() {
            var arr = problems.Select(x => $"{Environment.NewLine} -- {x.FieldName}: {x.Message}");
            return $"Validation failed: {string.Join(string.Empty, arr)}";
        }
    }

    public static ValidationError Create(params ValidationProblem[] problems) =>
        Create(ErrorCode.From("VALIDATION_ERROR"), problems);

    public static ValidationError FromProblem(ErrorCode code, string message) =>
        Create(ValidationProblem.Create(code, message));
}

[PublicAPI]
[DebuggerDisplay("{Code} | {Message}")]
public readonly record struct ValidationProblem {
    public static readonly ValidationProblem None;

    /// <summary>
    /// The code associated with the validation problem.
    /// This can be used to categorize or identify the type of validation problem.
    /// </summary>
    public string Code { get; private init; }

    /// <summary>
    /// The message describing the validation problem.
    /// </summary>
    public string Message { get; private init; }

    /// <summary>
    /// The name of the field that caused the validation problem.
    /// </summary>
    public string FieldName { get; private init; }

    /// <summary>
    /// The field value that caused the problem.
    /// </summary>
    [MaybeNull]
    public object AttemptedValue { get; private init; }

    /// <summary>
    /// Indicates whether this validation problem is associated with a specific field.
    /// </summary>
    public bool HasPropertyName => !string.IsNullOrWhiteSpace(FieldName);

    /// <summary>
    /// Indicates whether this validation problem has an associated value.
    /// </summary>
    public bool HasAttemptedValue => AttemptedValue is not null;

    public override string ToString() => Message;

    /// <summary>
    /// Creates a new <see cref="ValidationProblem"/> for a specific field.
    /// </summary>
    public static ValidationProblem CreateForField(string fieldName, string message, object? attemptedValue = null, string? code = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new ValidationProblem {
            FieldName   = fieldName,
            Message   = message,
            AttemptedValue = attemptedValue!,
            Code      = string.IsNullOrWhiteSpace(code) ? $"INVALID_{fieldName.Underscore().ToUpperInvariant()}" : code
        };
    }

    /// <summary>
    /// Creates a new <see cref="ValidationProblem"/> without a specific field.
    /// This is useful for general validation problems that do not pertain to a specific field.
    /// </summary>
    public static ValidationProblem Create(string code, string message) {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new ValidationProblem {
            Code    = code,
            Message = message
        };
    }
}

public interface IValidator<in T> where T : class {
    ValidationResult ValidateOptions(T instance);
}

// [StructLayout(LayoutKind.Sequential, Size = 1)]
// public readonly struct ValidationSuccess;
//
// public static class Results {
//     public static Success Success => default;
//     public static Created Created => default;
//     public static Deleted Deleted => default;
//     public static Updated Updated => default;
// }

[PublicAPI]
public class ValidationException : KurrentException {
    public ValidationException(ErrorCode errorCode, string message, params ValidationProblem[] errors) : base(errorCode, message, null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(errors.Length, 0);

        ErrorCode = errorCode;
        Errors    = errors;
    }

    public string              ErrorCode { get; }
    public ValidationProblem[] Errors    { get; }
}
