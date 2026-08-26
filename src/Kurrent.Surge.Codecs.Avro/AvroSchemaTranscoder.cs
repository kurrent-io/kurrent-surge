using System.Runtime.CompilerServices;
using System.Text.Json;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Rewrites an Avro payload as JSON, guided by Avro schema text.
/// </summary>
/// <remarks>
/// Parsing a schema costs more than transcoding a payload does, so the resolved writer is cached against
/// the <see cref="SchemaSource"/> it came from. A caller that holds one source and reuses it pays for
/// that once; one that builds a fresh source per record pays every time. The table holds the source
/// weakly, so nothing is kept alive by having been transcoded.
/// </remarks>
[PublicAPI]
public sealed class AvroSchemaTranscoder : ISchemaTranscoder {
    public static AvroSchemaTranscoder Instance { get; } = new();

    readonly ConditionalWeakTable<SchemaSource, AvroJsonWriter> _writers = [];

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The schema is not valid Avro, or more than one was supplied.</exception>
    public void ToJson(SchemaSource schema, ReadOnlyMemory<byte> payload, Utf8JsonWriter writer) =>
        _writers.GetValue(schema, Resolve).Write(payload, writer);

    static AvroJsonWriter Resolve(SchemaSource schema) => new(AvroJsonSchema.Single(schema));
}
