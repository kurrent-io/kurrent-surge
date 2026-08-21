using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kurrent.Surge.Codecs.Avro.Generators.Model;

namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Converts between an Avro schema and a JSON Schema 2020-12 document, in both directions.
/// </summary>
/// <remarks>
/// The exported document carries an <c>x-avro</c> object on the nodes whose Avro facts the standard
/// keywords cannot express, so <see cref="Import(string)"/> reproduces the schema it came from. 2020-12
/// ignores unknown keywords, so a validator that has never heard of <c>x-avro</c> reaches the same verdict
/// as one that has.
/// <para>
/// This is not an <c>ISchemaExporter</c>. That interface takes a CLR <see cref="Type"/>, and no CLR type is
/// involved here — the Avro schema text is the only input.
/// </para>
/// </remarks>
[PublicAPI]
public static partial class AvroJsonSchema {
    /// <summary>The dialect every exported document declares.</summary>
    public const string Dialect = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>Converts Avro schema JSON to a JSON Schema 2020-12 document.</summary>
    /// <param name="avroSchema">The Avro schema, as JSON text.</param>
    /// <param name="options">Emission options. Defaults to annotated, non-canonical, dialect declared.</param>
    /// <exception cref="ArgumentException">The schema is not valid Avro.</exception>
    public static string Export(string avroSchema, AvroJsonSchemaOptions? options = null) =>
        Serialize(ExportAsJsonNode(avroSchema, options));

    /// <summary>Converts Avro schema JSON to a JSON Schema 2020-12 document.</summary>
    /// <remarks>The overload to use when the result is embedded in a larger document.</remarks>
    /// <exception cref="ArgumentException">The schema is not valid Avro.</exception>
    public static JsonNode ExportAsJsonNode(string avroSchema, AvroJsonSchemaOptions? options = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(avroSchema);

        return ExportDocument(avroSchema, options ?? AvroJsonSchemaOptions.Default);
    }

    /// <summary>Converts a JSON Schema 2020-12 document to Avro schema JSON.</summary>
    /// <remarks>
    /// A document with no <c>x-avro</c> annotations still converts, by inference from the standard
    /// keywords. Only an annotated document is guaranteed to reproduce the Avro schema it was exported
    /// from.
    /// </remarks>
    /// <exception cref="ArgumentException">The document is not valid JSON.</exception>
    /// <exception cref="AvroJsonSchemaException">The document cannot be expressed as an Avro schema.</exception>
    public static string Import(string jsonSchema) {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonSchema);

        var node = JsonNode.Parse(jsonSchema)
                ?? throw new ArgumentException("The JSON Schema is null.", nameof(jsonSchema));

        return Import(node);
    }

    /// <inheritdoc cref="Import(string)"/>
    public static string Import(JsonNode jsonSchema) => Serialize(ImportDocument(jsonSchema));

    static string Serialize(JsonNode node) {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            node.WriteTo(writer);

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    static AvroSchemaDocument Parse(string avroSchema, string parameterName) =>
        AvroSchemaParser.TryParse(avroSchema, out var document, out var error, out var offset)
            ? document
            : throw new ArgumentException($"The Avro schema could not be parsed at offset {offset}: {error}", parameterName);
}
