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
    
    // TODO SS: Source generator — replace these base types with [SelfValidating] attribute
    // ─────────────────────────────────────────────────────────────────────────────
    //
    // GOAL
    // Emit the validation plumbing (cache + Validate + EnsureValid + ResetValidation)
    // as a partial, so consumers can use it on records OR classes without picking a base.
    //
    // USAGE SHAPE
    //   [SelfValidating]                         // manual RunValidation
    //   public partial record Foo { ... }
    //
    //   [SelfValidating(typeof(BarValidator))]   // validator-bound, no RunValidation needed
    //   public partial class Bar { ... }
    //
    // WHAT TO GENERATE (per annotated type)
    // 1. Partial declaration implementing ISelfValidating.
    // 2. `volatile ValidationResult? _validationResult;` field.
    // 3. `protected void ResetValidation()` → nulls the cache.
    // 4. `public ValidationResult Validate()` → cached, volatile + benign race pattern.
    // 5. `public void EnsureValid()` → calls Validate().EnsureValid().
    // 6. If attribute has a validator type:
    //      emit `protected ValidationResult RunValidation() => Validator.Validate(this);`
    //      plus a `static readonly TValidator Validator = new();` field.
    //    Otherwise: require the user to provide `RunValidation()` in their partial
    //      (diagnostic if missing).
    //
    // DIAGNOSTICS
    // - KV001: type must be declared `partial`.
    // - KV002: validator type must implement IValidator<TValidator, T> with parameterless ctor.
    // - KV003: RunValidation missing (and no validator supplied).
    // - KV004 (optional, nice-to-have): non-init/non-private setter on a [Validated] property
    //   without ResetValidation wiring.
    //
    // STRETCH (do later, not v1)
    // - [Validated] property attribute: auto-wire ResetValidation() into init setters.
    // - Emit a struct-based cache helper for AOT-friendly layout if perf matters.
    // - Source-generated validator binding so `Validator.Validate(this)` is typed
    //   without the `(T)(object)this` cast.
    //
    // OPEN QUESTIONS (decide before writing the generator)
    // - Should Validate() be virtual? Currently sealed-by-default via partial — fine.
    // - Do we want an incremental generator (IIncrementalGenerator) from day one? Yes.
    // - Roslyn target version? Match whatever KurrentDB's analyzer projects already use.
    // - Ship in same package as Kurrent.Validation, or separate Kurrent.Validation.SourceGenerators?
    //   Separate is cleaner — consumers get generator via PrivateAssets="all".
    //
    // PREREQUISITE
    // Count actual usage sites before building this. If <10 types use SelfValidating*,
    // the two-base duplication below is cheaper than maintaining a generator.
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
public abstract record SelfValidating<T, TValidator> : SelfValidating
    where TValidator : class, IValidator<TValidator, T>, new()
    where T : class {

    static readonly TValidator Validator = new();

    /// <inheritdoc/>
    protected override ValidationResult RunValidation() =>
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

/// <summary>
/// Provides a base class for self-validating classes, implementing validation logic
/// using a specified validator type.
/// </summary>
/// <typeparam name="T">
/// The type of the inheriting class. This must be a reference type.
/// </typeparam>
/// <typeparam name="TValidator">
/// The type of the validator to be used for validation. This must be
/// a class implementing <see cref="IValidator{TValidator,T}"/> and include a parameterless constructor.
/// </typeparam>
[PublicAPI]
public abstract class SelfValidatingClass<T, TValidator> : SelfValidatingClass
    where TValidator : class, IValidator<TValidator, T>, new()
    where T : class {

    static readonly TValidator Validator = new();

    /// <inheritdoc/>
    protected override ValidationResult RunValidation() =>
        Validator.Validate((T)(object)this);
}

/// <summary>
/// Base class for types that carry invariants validated via cached validation.
/// Provides lazy, cached, thread-safe validation with automatic invalidation on mutation.
/// <para>
/// Derived classes call <see cref="ResetValidation"/> in their setters for validated
/// properties and implement <see cref="RunValidation"/> with the actual validation logic.
/// </para>
/// </summary>
[PublicAPI]
public abstract class SelfValidatingClass : ISelfValidating {
    volatile ValidationResult? _validationResult;

    /// <summary>
    /// Call from every setter that participates in validation
    /// to invalidate the cached result.
    /// </summary>
    protected void ResetValidation() => _validationResult = null;

    /// <summary>
    /// Returns the cached validation result, computing it on first call
    /// or after invalidation. Thread-safe via volatile + benign race.
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