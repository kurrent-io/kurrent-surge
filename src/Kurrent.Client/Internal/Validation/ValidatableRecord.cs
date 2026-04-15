namespace Kurrent.Validation;

/// <summary>
/// Base record for types that carry invariants validated via cached validation.
/// Provides lazy, cached, thread-safe validation with automatic invalidation on mutation.
/// <para>
/// Derived records call <see cref="Invalidate"/> in their <c>init</c> setters for validated
/// properties and implement <see cref="Validate"/> with the actual validation logic.
/// </para>
/// </summary>
[PublicAPI]
public abstract record ValidatableRecord {
    volatile ValidationResult? _validationResult;

    /// <summary>
    /// Call from every <c>init</c> setter that participates in validation
    /// to invalidate the cached result.
    /// </summary>
    protected void Invalidate() => _validationResult = null;

    /// <summary>
    /// Returns the cached validation result, computing it on first call
    /// or after invalidation. Thread-safe via volatile + benign race.
    /// </summary>
    public ValidationResult GetValidationResult() {
        var result = _validationResult;
        if (result is not null)
            return result;

        result = Validate();
        _validationResult = result;
        return result;
    }

    /// <summary>
    /// Validates and throws <see cref="ValidationException"/> on failure.
    /// </summary>
    public virtual void EnsureValid() => GetValidationResult().Unwrap();

    /// <summary>
    /// Implement the actual validation logic. Must be pure — no side effects,
    /// deterministic output for the same field state.
    /// </summary>
    protected abstract ValidationResult Validate();
}
