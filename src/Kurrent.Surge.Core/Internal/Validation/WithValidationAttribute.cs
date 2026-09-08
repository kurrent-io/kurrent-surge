namespace Kurrent.Surge.Validation;

/// <summary>
/// Marks a partial type for generated validation wiring: the <see cref="ISelfValidating"/>
/// implementation, the validator, and the call into the type's rules.
/// </summary>
/// <remarks>
/// The type supplies its rules through a <c>static partial void ConfigureRules(Validator rules)</c>
/// method. <see cref="ISelfValidating.Validate"/> runs them on every call, so the result always
/// describes the object's current state and the type's properties need no special shape.
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class WithValidationAttribute : Attribute;
