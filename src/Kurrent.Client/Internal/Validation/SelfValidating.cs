namespace Kurrent.Validation;

public interface ISelfValidating {
    /// <summary>
    /// Validates the current object and returns a <see cref="ValidationResult"/>
    /// representing the outcome of the validation process.
    /// </summary>
    /// <returns>
    /// A <see cref="ValidationResult"/> containing information about
    /// whether the object is valid and any validation errors if present.
    /// </returns>
    ValidationResult Validate();

    /// <summary>
    /// Ensures the current object is valid by invoking its validation logic
    /// and throwing an exception if validation fails.
    /// </summary>
    /// <exception cref="Exception">
    /// Thrown if the validation process indicates that the object is invalid.
    /// The exception type and message are determined by the specific validation errors.
    /// </exception>
    void EnsureValid() => Validate().Unwrap();
}

/// <summary>
/// Provides a base class for self-validating records, implementing validation logic
/// using a specified validator type.
/// </summary>
/// <typeparam name="T">
/// The type of the inheriting class. This must be a class that implements
/// <see cref="ISelfValidating"/>.
/// </typeparam>
/// <typeparam name="TValidator">
/// The type of the validator to be used for validation. This must be
/// a class implementing <see cref="IValidator{TValidator,T}"/> and include a parameterless constructor.
/// </typeparam>
[PublicAPI]
public abstract record SelfValidating<T, TValidator> : ISelfValidating
    where TValidator : class, IValidator<TValidator, T>, new()
    where T : class, ISelfValidating {

    static readonly TValidator Validator = new();

    /// <inheritdoc/>
    public ValidationResult Validate() =>
        Validator.Validate((T)(object)this);

    /// <inheritdoc/>
    public void EnsureValid() => Validate().Unwrap();
}
