using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;

/// <summary>
/// Converts a protobuf message schema into a JSON Schema document.
/// </summary>
/// <remarks>
/// The exported document carries an <c>x-protobuf</c> object on the nodes whose protobuf facts the
/// standard keywords cannot express. Every draft ignores an unknown keyword, so a validator that has
/// never heard of <c>x-protobuf</c> reaches the same verdict as one that has.
/// <para>
/// The unit is one message and the types it reaches, not one file. A <c>.proto</c> file declares many
/// top-level messages and a message's closure may span several files, so a document describes the
/// closure of the message it was asked for.
/// </para>
/// <para>
/// This is not an <c>ISchemaExporter</c>. That interface takes a CLR <see cref="Type"/>, and no CLR
/// type is involved here — the schema text is the only input.
/// </para>
/// </remarks>
[PublicAPI]
public static partial class ProtobufJsonSchema {
    /// <summary>The draft 4 dialect, declared by default.</summary>
    public const string Draft04Dialect = "http://json-schema.org/draft-04/schema#";

    /// <summary>The draft 2020-12 dialect.</summary>
    public const string Draft202012Dialect = "https://json-schema.org/draft/2020-12/schema";

    internal static string DialectOf(JsonSchemaDialect dialect) =>
        dialect is JsonSchemaDialect.Draft202012 ? Draft202012Dialect : Draft04Dialect;

    /// <summary>Where a draft hoists its named types.</summary>
    /// <remarks>
    /// A <c>$ref</c> is a JSON Pointer, so either name resolves under either draft. They are kept apart
    /// anyway: a document declares one draft and reads entirely as that draft, or it reads as neither.
    /// </remarks>
    internal static string ContainerOf(JsonSchemaDialect dialect) =>
        dialect is JsonSchemaDialect.Draft202012 ? "$defs" : "definitions";

    /// <summary>Converts a message declared in <paramref name="schema"/> into a JSON Schema document.</summary>
    /// <param name="schema">The schema set the message is declared in.</param>
    /// <param name="messageTypeName">The fully qualified message name, such as <c>orders.v1.Order</c>.</param>
    /// <param name="options">Emission options. Defaults to annotated, non-canonical, dialect declared.</param>
    /// <exception cref="ArgumentException">The message is not declared in the schema set.</exception>
    public static string Export(ProtoSchemaSet schema, string messageTypeName, ProtobufJsonSchemaOptions? options = null) =>
        Serialize(ExportAsJsonNode(schema, messageTypeName, options));

    /// <inheritdoc cref="Export(ProtoSchemaSet, string, ProtobufJsonSchemaOptions?)"/>
    /// <remarks>The overload to use when the result is embedded in a larger document.</remarks>
    public static JsonNode ExportAsJsonNode(ProtoSchemaSet schema, string messageTypeName, ProtobufJsonSchemaOptions? options = null) {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageTypeName);

        return ExportDocument(schema, messageTypeName, options ?? ProtobufJsonSchemaOptions.Default);
    }

    /// <summary>Converts a message declared in a single <c>.proto</c> source into a JSON Schema document.</summary>
    /// <param name="protoSchema">A single <c>.proto</c> source that imports nothing outside the well-known types.</param>
    /// <param name="messageTypeName">The fully qualified message name.</param>
    /// <param name="options">Emission options.</param>
    /// <exception cref="ProtoParseException">The schema could not be parsed.</exception>
    /// <exception cref="ArgumentException">The schema is not valid, or declares no such message.</exception>
    public static string Export(string protoSchema, string messageTypeName, ProtobufJsonSchemaOptions? options = null) =>
        Export(ProtoSchemaSet.Build(protoSchema), messageTypeName, options);

    static string Serialize(JsonNode node) {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            node.WriteTo(writer);

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
