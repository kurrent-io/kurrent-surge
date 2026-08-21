namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Controls what <see cref="AvroJsonSchema.Export(string, AvroJsonSchemaOptions?)"/> emits.
/// </summary>
[PublicAPI]
public sealed class AvroJsonSchemaOptions {
    /// <summary>
    /// Emits the <c>x-avro</c> annotations.
    /// </summary>
    /// <remarks>
    /// Off produces a portable schema that no longer round-trips: <c>float</c>, <c>fixed</c> and the
    /// colliding logical types are unrecoverable without the annotation.
    /// </remarks>
    public bool Annotations { get; init; } = true;

    /// <summary>
    /// Sorts object keys and <c>$defs</c> so two structurally identical schemas export byte-identically.
    /// </summary>
    /// <remarks>Off follows the Avro declaration order, which is the readable one.</remarks>
    public bool Canonical { get; init; }

    /// <summary>
    /// Declares <c>$schema</c> at the root.
    /// </summary>
    /// <remarks>Off for embedding the result inside a larger document.</remarks>
    public bool DeclareDialect { get; init; } = true;

    internal static AvroJsonSchemaOptions Default { get; } = new();
}
