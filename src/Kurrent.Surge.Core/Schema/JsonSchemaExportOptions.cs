using System.Text.Json;

namespace Kurrent.Surge.Schema;

/// <summary>
/// Controls what an <see cref="IJsonSchemaExporter"/> emits.
/// </summary>
/// <remarks>
/// Not <c>JsonSchemaExporterOptions</c>: that name belongs to
/// <see cref="System.Text.Json.Schema.JsonSchemaExporterOptions"/>, which
/// <see cref="SystemJsonSchemaExporter"/> uses from this same namespace. Declaring it here would
/// shadow the BCL type and break that exporter.
/// <para>
/// There is no dialect setting. Every document is draft 2020-12, because that is the only draft the
/// validator reads.
/// </para>
/// </remarks>
[PublicAPI]
public class JsonSchemaExportOptions {
    /// <summary>
    /// The serializer whose naming and conversion rules the exported property names follow.
    /// </summary>
    /// <remarks>
    /// Read by an exporter that starts from a CLR type. An exporter driven by schema text takes its
    /// names from the schema and ignores this.
    /// </remarks>
    public JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>
    /// Leaves a nullable member out of <c>required</c>.
    /// </summary>
    public bool NullableIsOptional { get; set; } = true;

    /// <summary>
    /// Sorts object keys so two structurally identical schemas export byte-identically.
    /// </summary>
    /// <remarks>
    /// Off follows declaration order, which is the readable one. Array items are never sorted:
    /// <c>oneOf</c> order and <c>enum</c> order both carry meaning.
    /// </remarks>
    public bool Canonical { get; set; }

    /// <summary>
    /// Writes an enum as a <c>oneOf</c> over its names and an integer, rather than names alone.
    /// </summary>
    /// <remarks>
    /// For a format that already knows whether its enums are open — protobuf reads it from the
    /// syntax — this is derived rather than chosen, and the setting is ignored.
    /// </remarks>
    public bool EnumAsOneOf { get; set; }

    internal static JsonSchemaExportOptions Default { get; } = new();
}
