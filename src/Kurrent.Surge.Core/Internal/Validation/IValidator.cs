using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Exceptions;
using static System.String;

// ReSharper disable MemberCanBePrivate.Global

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

/// <summary>
/// Represents the outcome of a validation operation.
/// Either valid (no problems) or invalid (one or more problems with an error code).
/// </summary>
[PublicAPI]
[DebuggerDisplay("{IsValid ? \"Valid\" : $\"Invalid ({Problems.Length} problems)\"}")]
public sealed class ValidationResult {
    /// <summary>
    /// A singleton representing a successful validation result with no problems.
    /// </summary>
    public static readonly ValidationResult Valid = new();

    ValidationResult() {
        ErrorCode = ErrorCode.None;
        Problems  = [];
    }

    ValidationResult(ValidationProblem[] warnings) {
        ErrorCode = ErrorCode.None;
        Problems  = warnings;
    }

    ValidationResult(ErrorCode errorCode, string errorMessage, ValidationProblem[] problems) {
        ErrorCode    = errorCode;
        ErrorMessage = errorMessage;
        Problems     = problems;
    }

    /// <summary>
    /// The error code categorizing this validation failure. <see cref="ErrorCode.None"/> when valid.
    /// </summary>
    public ErrorCode ErrorCode { get; }

    /// <summary>
    /// A human-readable summary of the validation errors.
    /// </summary>
    public string ErrorMessage { get; } = "";

    /// <summary>
    /// The validation problems that occurred during the validation process.
    /// </summary>
    public ValidationProblem[] Problems { get; }

    /// <summary>
    /// Whether the validation passed with no error-level problems.
    /// </summary>
    public bool IsValid => !Array.Exists(Problems, static p => p.Severity == RuleSeverity.Error);

    /// <summary>
    /// Whether the validation failed.
    /// </summary>
    public bool IsInvalid => !IsValid;

    /// <summary>
    /// Throws a <see cref="ValidationException"/> if the result is invalid.
    /// </summary>
    public void EnsureValid() {
        if (IsInvalid)
            throw new ValidationException(ErrorCode, ErrorMessage, Problems);
    }

    /// <summary>
    /// Creates a failure result from an error code and one or more validation problems.
    /// </summary>
    public static ValidationResult Failure(ErrorCode code, params ValidationProblem[] problems) {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(problems.Length, 0);
        return new(code, BuildErrorMessage(problems), problems);
    }

    /// <summary>
    /// Creates a failure result from one or more validation problems, using a generic error code.
    /// </summary>
    public static ValidationResult Failure(params ValidationProblem[] problems) =>
        Failure(ErrorCode.From("VALIDATION_FAILURE"), problems);

    /// <summary>
    /// Creates a valid result that carries advisory warning problems.
    /// </summary>
    public static ValidationResult WithWarnings(params ValidationProblem[] problems) {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(problems.Length, 0);
        return new(problems);
    }

    /// <summary>
    /// Creates a failure result from a single problem message.
    /// </summary>
    public static ValidationResult FromProblem(ErrorCode code, string message) =>
        Failure(code, ValidationProblem.Create(code, message));

    public static ValidationResult FromValue<T>(object? value, string reason) =>
        Failure(ErrorCodeCache<T>.Value, ValidationProblem.FromValue<T>(value, reason));
    
    public override string ToString() => IsValid ? "Valid" : ErrorMessage;

    /// <summary>
    /// Allows <c>return Results.Success</c> to implicitly convert.
    /// </summary>
    public static implicit operator ValidationResult(Success _) => Valid;

    static string BuildErrorMessage(ValidationProblem[] problems) {
        var arr = problems.Select(x => $"{Environment.NewLine} -- {x.FieldName}: {x.Message}");
        return $"Validation failed: {Join("", arr)}";
    }
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
    /// The severity of this validation problem. Defaults to <see cref="RuleSeverity.Error"/> (enum value 0).
    /// </summary>
    public RuleSeverity Severity { get; internal init; }

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
    public static ValidationProblem CreateForField(
        string fieldName, string reason,
        object? attemptedValue = null, 
        string? code = null,
        RuleSeverity severity = RuleSeverity.Error) {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new() {
            FieldName      = fieldName,
            Message        = reason,
            AttemptedValue = attemptedValue!,
            Code           = IsNullOrWhiteSpace(code) ? $"INVALID_{fieldName.Underscore().ToUpperInvariant()}" : code,
            Severity       = severity
        };
    }

    /// <summary>
    /// Creates a new <see cref="ValidationProblem"/> without a specific field.
    /// This is useful for general validation problems that do not pertain to a specific field.
    /// </summary>
    public static ValidationProblem Create(string code, string reason) {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new() {
            Code    = code,
            Message = reason
        };
    }
    
    public static ValidationProblem FromValue<T>(object? value, string reason) {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new() {
            Code           = ErrorCodeCache<T>.Value,
            Message        = reason,
            AttemptedValue = value!,
        };
    }
}
