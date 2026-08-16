using Kurrent.Surge.Codecs;
using Kurrent.Surge.Schema;
using Nerdbank.MessagePack;
using PolyType;

namespace Kurrent.Surge.Client.Schema.Serialization.MessagePack;

/// <summary>
/// MessagePack payload codec backed by Nerdbank.MessagePack. A configured
/// <see cref="ITypeShapeProvider"/> is required — there is no reflection fallback.
/// </summary>
public sealed class MessagePackSchemaCodec(NerdbankMessagePackSchemaCodecOptions options) : ISchemaCodec {
    MessagePackSerializer Serializer        { get; } = options.MessagePackSerializer;
    ITypeShapeProvider    TypeShapeProvider { get; } = options.TypeShapeProvider;

    /// <inheritdoc />
    public SchemaDataFormat DataFormat => SchemaDataFormat.MessagePack;

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Encode(object? value) =>
        Serializer.SerializeObject(value, TypeShapeProvider.GetTypeShapeOrThrow(value!.GetType()));

    /// <inheritdoc />
    public object Decode(ReadOnlyMemory<byte> data, Type resolvedType) =>
        Serializer.DeserializeObject(data, TypeShapeProvider.GetTypeShapeOrThrow(resolvedType))!;
}
