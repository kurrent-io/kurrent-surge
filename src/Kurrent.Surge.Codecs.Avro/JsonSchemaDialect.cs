namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// The JSON Schema draft an exported document targets.
/// </summary>
/// <remarks>
/// A document declares one draft and uses only that draft's vocabulary. The two differ in the
/// <c>$schema</c> they declare and in where named types are hoisted, and nothing else: the mapping reaches
/// for no keyword that draft 4 lacks.
/// </remarks>
[PublicAPI]
public enum JsonSchemaDialect {
    /// <summary>
    /// Draft 4, hoisting named types into <c>definitions</c>.
    /// </summary>
    /// <remarks>
    /// The default, because it is what the schema registry validates with. Draft 4 is also the widest
    /// floor: a validator for a later draft reads the declaration and applies draft 4 semantics.
    /// </remarks>
    Draft04 = 0,

    /// <summary>
    /// Draft 2020-12, hoisting named types into <c>$defs</c>.
    /// </summary>
    /// <remarks>
    /// For a consumer that wants the current draft, such as an OpenAPI 3.1 document. Nothing in the
    /// mapping needs it today; it exists for the keywords only 2020-12 has, and those are not emitted yet.
    /// </remarks>
    Draft202012 = 1
}
