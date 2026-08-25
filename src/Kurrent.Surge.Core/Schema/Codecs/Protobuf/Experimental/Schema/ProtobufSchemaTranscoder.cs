using System.Runtime.CompilerServices;
using System.Text.Json;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Indexed;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;

/// <summary>
/// Rewrites a protobuf payload as canonical JSON, guided by <c>.proto</c> text.
/// </summary>
/// <remarks>
/// Resolving a schema costs roughly three times what transcoding a payload does, so the resolved
/// writer is cached against the <see cref="SchemaSource"/> it came from. A caller that holds one
/// source and reuses it pays for that once; one that builds a fresh source per record pays every
/// time. The table holds the source weakly, so nothing is kept alive by having been transcoded.
/// </remarks>
[PublicAPI]
public sealed class ProtobufSchemaTranscoder : ISchemaTranscoder {
    public static ProtobufSchemaTranscoder Instance { get; } = new();

    readonly ConditionalWeakTable<SchemaSource, ProtobufJsonWriter> _writers = [];

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The schema names no message, or does not declare it.</exception>
    /// <exception cref="ProtoParseException">A schema could not be parsed.</exception>
    public void ToJson(SchemaSource schema, ReadOnlyMemory<byte> payload, Utf8JsonWriter writer) {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(writer);

        _writers.GetValue(schema, Resolve).Write(payload, writer);
    }

    static ProtobufJsonWriter Resolve(SchemaSource schema) {
        if (schema.MessageTypeName is not { Length: > 0 } messageTypeName)
            throw new ArgumentException(
                "A protobuf document declares many messages, so the one to transcode must be named.",
                nameof(schema));

        return new ProtobufJsonWriter(ProtobufJsonSchema.Resolve(schema), messageTypeName);
    }
}
