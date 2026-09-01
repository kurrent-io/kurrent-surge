using System.Text.Json.Nodes;
using Kurrent.Surge.Codecs.Avro.Generators.Model;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Converts an Avro schema into a JSON Schema document.
/// </summary>
/// <remarks>
/// The document describes what <see cref="AvroJsonWriter"/> writes, so a transcoded payload validates
/// against it. A logical type is written as the base type the encoding carries — a date is a day count
/// because that is what the mapping emits, not a date because that is what it means.
/// </remarks>
[PublicAPI]
public sealed partial class AvroJsonSchema : IJsonSchemaExporter {
    /// <summary>The draft every exported document declares.</summary>
    public const string Dialect = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>Where named types are hoisted, and what a <c>$ref</c> points into.</summary>
    internal const string Container = "$defs";

    public static AvroJsonSchema Instance { get; } = new();

    // Held privately rather than shared: JsonSchemaExportOptions is mutable, so a singleton anything
    // outside this class can reach is a singleton anything outside it can reconfigure.
    static readonly JsonSchemaExportOptions DefaultOptions = new();

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The schema is not valid Avro, or more than one was supplied.</exception>
    public JsonNode Export(SchemaSource schema, JsonSchemaExportOptions? options = null) =>
        ExportDocument(Single(schema), options ?? DefaultOptions);

    /// <remarks>
    /// An Avro document declares one root type and resolves its named references inside itself, so
    /// <see cref="SchemaSource.MessageTypeName"/> is never read. A source carrying several documents is
    /// rejected rather than resolved to the first, which is what the contract on
    /// <see cref="SchemaSource"/> requires.
    /// </remarks>
    internal static string Single(SchemaSource schema) =>
        schema.Schemas is [var text]
            ? text
            : throw new ArgumentException(
                $"An Avro document declares one root type, so exactly one schema is expected; {schema.Schemas.Count} were supplied.",
                nameof(schema));

    static AvroSchemaDocument Parse(string avroSchema, string parameterName) =>
        AvroSchemaParser.TryParse(avroSchema, out var document, out var error, out var offset)
            ? document
            : throw new ArgumentException($"The Avro schema could not be parsed at offset {offset}: {error}", parameterName);
}
