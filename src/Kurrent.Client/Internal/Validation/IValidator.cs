using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Humanizer;
using static System.String;

namespace Kurrent.Validation;

/// <summary>
/// Defines the contract for a fluent validator that can validate instances of <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="TValidator">The concrete validator type (CRTP). Must have a parameterless constructor.</typeparam>
/// <typeparam name="T">The type of the object to be validated.</typeparam>
public interface IValidator<TValidator, in T>
    where T : class
    where TValidator : IValidator<TValidator, T>, new() {

    /// <summary>
    /// Validates the given instance and returns a <see cref="ValidationResult"/>.
    /// </summary>
    ValidationResult Validate(T instance);

    /// <summary>
    /// Error code used to categorize validation failures for this type.
    /// </summary>
    string ErrorCode { get; }
}

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
            return $"Validation failed: {Join("", arr)}";
        }
    }

    public static ValidationError Create(params ValidationProblem[] problems) =>
        Create(ErrorCode.From("VALIDATION_ERROR"), problems);

    public static ValidationError FromProblem(ErrorCode code, string message) =>
        Create(code, ValidationProblem.Create(code, message));
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
    public bool HasPropertyName => !IsNullOrWhiteSpace(FieldName);

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
            Code      = IsNullOrWhiteSpace(code) ? $"INVALID_{fieldName.Underscore().ToUpperInvariant()}" : code
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


