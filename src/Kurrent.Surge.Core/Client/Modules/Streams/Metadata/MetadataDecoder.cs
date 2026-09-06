using Kurrent.Toolkit;
// ReSharper disable SwitchStatementHandlesSomeKnownEnumValuesWithDefault
// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Kurrent.Surge.Codecs.Json;
using Kurrent.Surge.Errors;
using Kurrent.Surge.Schema;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// Provides a base implementation for decoding metadata from serialized byte representations.
/// Serves as an abstraction for specific metadata decoding implementations.
/// </summary>
/// <remarks>
/// Decoded metadata carries only what the payload contains — schema name and format are not
/// injected. The record mapper builds the schema info from the wire values; metadata that
/// embeds its own schema keys (link records, custom decoders) takes precedence there.
/// </remarks>
public abstract class MetadataDecoder : IMetadataDecoder {
    static readonly SchemaName LinkSchemaName = "$>";

    /// <inheritdoc />
    public ValueTask<Metadata> Decode(ReadOnlyMemory<byte> bytes, MetadataDecoderContext context) {
        if (bytes.IsEmpty)
            return new Metadata().AsValueTask();

        try {
            if (context.Schema == LinkSchemaName)
                return DeserializeLinkMetadata(bytes).AsValueTask();

            return DecodeCore(bytes, context).AsValueTask();
        }
        catch (Exception ex) when (ex is not MetadataDecodingException) {
            throw new MetadataDecodingException(GetType(), context, ex);
        }
    }

    static Metadata DeserializeLinkMetadata(ReadOnlyMemory<byte> bytes) {
        // [0] = {DebugViewDictionaryItem<string, JsonValue>} [$v]=1:-1:1:4
        // [1] = {DebugViewDictionaryItem<string, JsonValue>} [$c]=10868
        // [2] = {DebugViewDictionaryItem<string, JsonValue>} [$p]=10868
        // [3] = {DebugViewDictionaryItem<string, JsonValue>} [$causedBy]=373c6630-d8fe-ff2c-16af-e286265e7e98

        var deserialized = JsonSerializer.Deserialize<Dictionary<string, JsonValue>>(
            bytes.Span, KurrentJsonContext.Default.Options)!;

        return new Metadata()
            .With(SystemMetadataKeys.SchemaName, LinkSchemaName)
            .With(SystemMetadataKeys.SchemaFormat, SchemaDataFormat.Json)
            .With("$v", deserialized["$v"].ToString())
            .With("$c", deserialized["$c"].GetValue<long>())
            .With("$p", deserialized["$p"].GetValue<long>())
            .WithIf("$o", () => StreamName.From(deserialized["$o"].ToString()), deserialized.ContainsKey("$o"))
            .With("$causedBy", deserialized["$causedBy"].GetValue<Guid>());
    }

    protected abstract Metadata DecodeCore(ReadOnlyMemory<byte> bytes, MetadataDecoderContext context);
}

/// <summary>
/// Defines a contract for decoding metadata from serialized byte representations.
/// Implementors of this interface should provide functionality to convert serialized binary data
/// into <see cref="Metadata"/> objects.
/// </summary>
public interface IMetadataDecoder {
    /// <summary>
    /// Decodes metadata from a byte array.
    /// </summary>
    /// <param name="bytes">The serialized metadata bytes.</param>
    /// <param name="context">
    /// The context for decoding metadata, providing additional information such as the originating stream's name,
    /// </param>
    /// <returns>The decoded <see cref="Metadata"/> object.</returns>
    ValueTask<Metadata> Decode(ReadOnlyMemory<byte> bytes, MetadataDecoderContext context);
}

/// <summary>
/// Encapsulates metadata decoding context, providing necessary information to decode metadata.
/// </summary>
[PublicAPI]
public readonly record struct MetadataDecoderContext(
    StreamName Stream,
    SchemaName Schema,
    SchemaDataFormat DataFormat);

[PublicAPI]
public class MetadataDecodingException(Type decoderType, MetadataDecoderContext decoderContext, Exception innerException)
    : KurrentException(Errors.ErrorCode.FromType<MetadataDecodingException>(), ErrorMessage(decoderType, decoderContext, innerException), innerException) {

    public Type                   DecoderType    { get; } = decoderType;
    public MetadataDecoderContext DecoderContext { get; } = decoderContext;

    static string ErrorMessage(Type type, MetadataDecoderContext ctx, Exception ex) =>
        $"Failed to decode '{ctx.Schema}' record metadata " +
        $"from '{ctx.Stream}' using instance of '{type.Name}': {ex.Message}";

    [DoesNotReturn, StackTraceHidden]
    public static void Throw(Type decoderType, MetadataDecoderContext decoderContext, Exception innerException) =>
        throw new MetadataDecodingException(decoderType, decoderContext, innerException);
}
