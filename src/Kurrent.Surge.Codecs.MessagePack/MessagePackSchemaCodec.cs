using Kurrent.Surge.Schema;
using MessagePack;

namespace Kurrent.Surge.Codecs.MessagePack;

/// <summary>
/// MessagePack payload codec backed by the official MessagePack-CSharp library, restricted to
/// source-generated resolvers — see <see cref="MessagePackSchemaCodecOptions"/> for why.
/// </summary>
public sealed class MessagePackSchemaCodec(MessagePackSchemaCodecOptions? options = null) : ISchemaCodec {
    MessagePackSerializerOptions Options { get; } = (options ?? MessagePackSchemaCodecOptions.Default).MessagePackSerializerOptions;

    /// <inheritdoc />
    public SchemaDataFormat DataFormat => SchemaDataFormat.MessagePack;

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Encode(object? value) =>
        MessagePackSerializer.Serialize(value!.GetType(), value, Options);

    /// <inheritdoc />
    public object Decode(ReadOnlyMemory<byte> data, Type resolvedType) =>
        MessagePackSerializer.Deserialize(resolvedType, data, Options)!;
}
