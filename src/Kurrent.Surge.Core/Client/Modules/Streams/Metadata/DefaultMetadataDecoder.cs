using JsonSerializer = Kurrent.Surge.Codecs.Json.JsonSerializer;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// The default metadata decoder that deserializes metadata from JSON-encoded byte arrays.
/// This allows for backward compatibility with previous metadata formats.
/// Only booleans, Guids, DateTimes, and TimeSpans (strings containing a ':') will be typed;
/// numbers are deserialized as strings, and binary values stay base64 strings — both are
/// recovered through the typed retrieval path (<c>TryGetValue&lt;T&gt;</c>).
/// </summary>
[PublicAPI]
public sealed class DefaultMetadataDecoder : MetadataDecoder {
    static readonly JsonSerializer Serializer = new();

    protected override Metadata DecodeCore(ReadOnlyMemory<byte> bytes, MetadataDecoderContext context) =>
        Serializer.Deserialize<Metadata>(bytes) ?? new();
}
