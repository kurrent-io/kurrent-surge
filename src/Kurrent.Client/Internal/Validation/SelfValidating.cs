// ReSharper disable ReplaceConditionalExpressionWithNullCoalescing

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
    ValidationResult RunValidation();

    /// <summary>
    /// Ensures the current object is valid by invoking its validation logic
    /// and throwing an exception if validation fails.
    /// </summary>
    /// <exception cref="Exception">
    /// Thrown if the validation process indicates that the object is invalid.
    /// The exception type and message are determined by the specific validation errors.
    /// </exception>
    void EnsureValid() => RunValidation().Unwrap();
}

// /// <summary>
// /// Provides a base class for self-validating records, implementing validation logic
// /// using a specified validator type.
// /// </summary>
// /// <typeparam name="T">
// /// The type of the inheriting class. This must be a class that implements
// /// <see cref="ISelfValidating"/>.
// /// </typeparam>
// /// <typeparam name="TValidator">
// /// The type of the validator to be used for validation. This must be
// /// a class implementing <see cref="IValidator{TValidator,T}"/> and include a parameterless constructor.
// /// </typeparam>
// [PublicAPI]
// public abstract record SelfValidating<T, TValidator> : ISelfValidating
//     where TValidator : class, IValidator<TValidator, T>, new()
//     where T : class, ISelfValidating {
//
//     static readonly TValidator Validator = new();
//
//     /// <inheritdoc/>
//     public ValidationResult RunValidation() =>
//         Validator.Validate((T)(object)this);
//
//     /// <inheritdoc/>
//     public void EnsureValid() => RunValidation().Unwrap();
// }

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
public abstract record SelfValidating<T, TValidator> : SelfValidating
    where TValidator : class, IValidator<TValidator, T>, new()
    where T : class, ISelfValidating {

    static readonly TValidator Validator = new();

    /// <inheritdoc/>
    public override ValidationResult RunValidation() =>
        Validator.Validate((T)(object)this);
}

/// <summary>
/// Base record for types that carry invariants validated via cached validation.
/// Provides lazy, cached, thread-safe validation with automatic invalidation on mutation.
/// <para>
/// Derived records call <see cref="ResetValidation"/> in their <c>init</c> setters for validated
/// properties and implement <see cref="RunValidation"/> with the actual validation logic.
/// </para>
/// </summary>
[PublicAPI]
public abstract record SelfValidating : ISelfValidating {
    volatile ValidationResult? _validationResult;

    /// <summary>
    /// Call from every <c>init</c> setter that participates in validation
    /// to invalidate the cached result.
    /// </summary>
    protected void ResetValidation() => _validationResult = null;

    /// <summary>
    /// Returns the cached validation result, computing it on first call
    /// or after invalidation. Thread-safe via volatile + benign race.
    /// </summary>
    internal ValidationResult Validate() =>
        _validationResult is { } result ? result : _validationResult = RunValidation();

    /// <summary>
    /// Implement the actual validation logic. Must be pure — no side effects,
    /// deterministic output for the same field state.
    /// </summary>
    public abstract ValidationResult RunValidation();

    /// <summary>
    /// Ensures the current object is valid by invoking its validation logic
    /// and throwing an exception if validation fails.
    /// </summary>
    /// <exception cref="Exception">
    /// Thrown if the validation process indicates that the object is invalid.
    /// The exception type and message are determined by the specific validation errors.
    /// </exception>
    public virtual void EnsureValid() => RunValidation().Unwrap();
}
