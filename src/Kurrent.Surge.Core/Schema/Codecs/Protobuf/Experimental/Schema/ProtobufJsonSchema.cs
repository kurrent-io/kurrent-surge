using System.Text.Json.Nodes;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;

/// <summary>
/// Converts a protobuf message schema into a JSON Schema document.
/// </summary>
/// <remarks>
/// The unit is one message and the types it reaches, not one file. A <c>.proto</c> file declares many
/// top-level messages and a message's closure may span several of them, so a document describes the
/// closure of the message it was asked for.
/// <para>
/// The document describes what <see cref="Indexed.ProtobufJsonWriter"/> writes, so a transcoded
/// payload validates against it. A logical form is written as the base type the wire carries — a
/// 64-bit integer is a digit string because that is what the mapping emits, not an integer because
/// that is what it means.
/// </para>
/// </remarks>
[PublicAPI]
public sealed partial class ProtobufJsonSchema : IJsonSchemaExporter {
    /// <summary>The draft every exported document declares.</summary>
    public const string Dialect = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>Where named types are hoisted, and what a <c>$ref</c> points into.</summary>
    internal const string Container = "$defs";

    public static ProtobufJsonSchema Instance { get; } = new();

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The schema is not valid, or declares no such message.</exception>
    /// <exception cref="ProtoParseException">A schema could not be parsed.</exception>
    public JsonNode Export(SchemaSource schema, JsonSchemaExportOptions? options = null) {
        if (schema.MessageTypeName is not { Length: > 0 } messageTypeName)
            throw new ArgumentException(
                "A protobuf document declares many messages, so the one to export must be named.",
                nameof(schema));

        return ExportDocument(Resolve(schema), messageTypeName, options ?? JsonSchemaExportOptions.Default);
    }

    /// <remarks>
    /// Schemas arrive as text with no names, so an import between two of them cannot be matched to a
    /// path. Each is resolved on its own against the well-known types, which is what the contract on
    /// <see cref="SchemaSource"/> requires.
    /// </remarks>
    internal static ProtoSchemaSet Resolve(SchemaSource schema) =>
        ProtoSchemaSet.Build(schema.Schemas.Select((text, index) => new ProtoFile($"schema{index}.proto", text)).ToArray());
}
