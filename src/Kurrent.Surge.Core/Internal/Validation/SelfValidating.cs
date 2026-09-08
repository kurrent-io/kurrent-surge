// ReSharper disable ReplaceConditionalExpressionWithNullCoalescing

namespace Kurrent.Surge.Validation;

public interface ISelfValidating {
    /// <summary>
    /// Validates the current object and returns a <see cref="ValidationResult"/>
    /// representing the outcome of the validation process.
    /// </summary>
    /// <returns>
    /// A <see cref="ValidationResult"/> containing information about
    /// whether the object is valid and any validation errors if present.
    /// </returns>
    public ValidationResult Validate();

    /// <summary>
    /// Ensures the current object is valid by invoking its validation logic
    /// and throwing an exception if validation fails.
    /// </summary>
    /// <exception cref="Exception">
    /// Thrown if the validation process indicates that the object is invalid.
    /// The exception type and message are determined by the specific validation errors.
    /// </exception>
    public void EnsureValid() => Validate().EnsureValid();
}

/// <summary>
/// Provides a base record for self-validating records, implementing validation logic
/// using a specified validator type.
/// </summary>
/// <typeparam name="T">
/// The type of the inheriting record. This must be a reference type.
/// </typeparam>
/// <typeparam name="TValidator">
/// The type of the validator to be used for validation. This must be
/// a class implementing <see cref="IValidator{TValidator,T}"/> and include a parameterless constructor.
/// </typeparam>
[PublicAPI]
public abstract record SelfValidatingRecord<T, TValidator> : SelfValidatingRecord
    where TValidator : class, IValidator<TValidator, T>, new()
    where T : class {

    static readonly TValidator Validator = new();

    /// <inheritdoc/>
    protected override ValidationResult RunValidation() =>
        Validator.Validate((T)(object)this);
}

/// <summary>
/// Base record for types that carry invariants validated via cached validation.
/// Provides lazy, cached, thread-safe validation.
/// <para>
/// Derived records declare their validated properties as <c>init</c> and implement
/// <see cref="RunValidation"/> with the actual validation logic.
/// </para>
/// </summary>
/// <remarks>
/// A <c>with</c> expression yields an instance that validates on first use rather than
/// carrying the source's result forward.
/// </remarks>
[PublicAPI]
public abstract record SelfValidatingRecord : ISelfValidating {
    volatile ValidationResult? _validationResult;

    protected SelfValidatingRecord() { }

    // `with` routes through here. Not carrying the cached result is the point: a derived
    // instance must re-validate rather than inherit the source's verdict.
    protected SelfValidatingRecord(SelfValidatingRecord other) { }

    /// <summary>
    /// Returns the cached validation result, computing it on first call.
    /// Thread-safe via volatile + benign race.
    /// </summary>
    public ValidationResult Validate() =>
        _validationResult is { } result ? result : _validationResult = RunValidation();

    /// <summary>
    /// Implement the actual validation logic. Must be pure — no side effects,
    /// deterministic output for the same field state.
    /// </summary>
    protected abstract ValidationResult RunValidation();

    /// <summary>
    /// Ensures the current object is valid by invoking its validation logic
    /// and throwing an exception if validation fails.
    /// </summary>
    /// <exception cref="Exception">
    /// Thrown if the validation process indicates that the object is invalid.
    /// The exception type and message are determined by the specific validation errors.
    /// </exception>
    public virtual void EnsureValid() => Validate().EnsureValid();

    /// <summary>
    /// Ensures the current object is valid, logging warning-severity problems to
    /// <paramref name="logger"/> and throwing an exception if validation fails.
    /// </summary>
    public void EnsureValid(ILogger logger) => Validate().EnsureValid(logger);
}
