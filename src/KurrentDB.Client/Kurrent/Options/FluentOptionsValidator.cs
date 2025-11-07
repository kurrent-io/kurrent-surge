using FluentValidation;
using Humanizer;
using Kurrent.Validation;
using static System.String;

namespace Kurrent;

[PublicAPI]
public abstract class FluentOptionsValidator<TValidator, TOptions> : AbstractValidator<TOptions>, IOptionsValidator<TValidator, TOptions> where TOptions : class, IOptions where TValidator : IOptionsValidator<TValidator, TOptions>, new() {
    public static readonly TValidator Instance = new TValidator();

    public virtual string ErrorCode => $"INVALID_{nameof(TOptions).Underscore().ToUpperInvariant()}";

    public ValidationResult ValidateOptions(TOptions instance) {
        var validationResult = Validate(instance);

        // ignore warnings
        var isValid = validationResult.IsValid
                   || validationResult.Errors.All(x => x.Severity != Severity.Error);

        if (isValid)
            return Results.Success;

        // TODO SS: it would be kewl if we found a way to get these warnings and log them.

        var problems = validationResult.Errors
            .Where(err => err.Severity == Severity.Error)
            .Select(err => IsNullOrWhiteSpace(err.PropertyName)
                ? ValidationProblem.Create(err.ErrorCode,err.ErrorMessage)
                : ValidationProblem.CreateForField(err.PropertyName, err.ErrorMessage, err.AttemptedValue, err.ErrorCode))
            .ToArray();

        return ValidationError.Create(ErrorCode, problems);
    }
}
