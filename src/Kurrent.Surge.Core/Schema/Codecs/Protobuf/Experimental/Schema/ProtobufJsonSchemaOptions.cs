using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;

/// <summary>
/// Controls what <see cref="ProtobufJsonSchema.Export(ProtoSchemaSet, string, ProtobufJsonSchemaOptions?)"/> emits.
/// </summary>
[PublicAPI]
public sealed class ProtobufJsonSchemaOptions {
    /// <summary>
    /// Emits the <c>x-protobuf</c> annotations.
    /// </summary>
    /// <remarks>
    /// Off produces a portable schema that no longer round-trips: a field number is unrecoverable
    /// without the annotation, and a <c>.proto</c> file cannot be written without one.
    /// </remarks>
    public bool Annotations { get; init; } = true;

    /// <summary>
    /// Sorts object keys and the definitions container so two structurally identical schemas export
    /// byte-identically.
    /// </summary>
    /// <remarks>Off follows the declaration order, which is the readable one.</remarks>
    public bool Canonical { get; init; }

    /// <summary>
    /// Declares <c>$schema</c> at the root.
    /// </summary>
    /// <remarks>
    /// Off for embedding the result inside a larger document, which declares the draft itself. It does
    /// not change the vocabulary or where named types are hoisted — <see cref="Dialect"/> owns both.
    /// </remarks>
    public bool DeclareDialect { get; init; } = true;

    /// <summary>
    /// The JSON Schema draft the document targets.
    /// </summary>
    /// <remarks>
    /// One draft, top to bottom. Declaring one draft while emitting another's keywords produces a
    /// document that no validator reads the way it was written.
    /// </remarks>
    public JsonSchemaDialect Dialect { get; init; } = JsonSchemaDialect.Draft04;

    /// <summary>
    /// The writer settings the exported schema describes.
    /// </summary>
    /// <remarks>
    /// The schema describes the JSON a writer actually emits, and three of these change that JSON:
    /// <c>UseProtoFieldNames</c> changes every property key, <c>EnumsAsIntegers</c> turns enum values
    /// from strings into numbers, and <c>EmitDefaults</c> decides which fields are always present.
    /// A schema exported under settings the writer does not share rejects that writer's own output.
    /// </remarks>
    public ProtobufJsonWriterOptions Writer { get; init; } = ProtobufJsonWriterOptions.Default;

    internal static ProtobufJsonSchemaOptions Default { get; } = new();
}
