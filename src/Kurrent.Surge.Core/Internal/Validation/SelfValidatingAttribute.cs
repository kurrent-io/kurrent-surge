namespace Kurrent.Surge.Validation;

/// <summary>
/// Marks a partial options type for generated self-validation: a cached verdict that is discarded
/// whenever any property, wrapped collection, or nested options object changes.
/// </summary>
/// <remarks>
/// The annotated type must be a partial class, must declare its validated properties as partial,
/// and supplies its rules through a <c>static partial void ConfigureRules(Validator rules)</c>
/// method. Everything else is generated.
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SelfValidatingAttribute : Attribute;
