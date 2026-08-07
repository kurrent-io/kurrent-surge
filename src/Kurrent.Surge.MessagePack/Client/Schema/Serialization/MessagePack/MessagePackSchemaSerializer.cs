using MessagePack;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Schema.Serialization.MessagePack;

/// <summary>
/// A schema serializer backed by the official MessagePack-CSharp library, restricted to
/// source-generated resolvers — see <see cref="MessagePackSchemaSerializerOptions"/> for why.
/// </summary>
public class MessagePackSchemaSerializer(MessagePackSchemaSerializerOptions options, ISchemaManager schemaManager)
    : SchemaSerializerBase(options, schemaManager) {
    MessagePackSerializerOptions Options { get; } = options.MessagePackSerializerOptions;

    public override SchemaDataFormat DataFormat => SchemaDataFormat.MessagePack;

    protected override ReadOnlyMemory<byte> Serialize(object? value) =>
        MessagePackSerializer.Serialize(value!.GetType(), value, Options);

    protected override object Deserialize(ReadOnlyMemory<byte> data, Type resolvedType) =>
        MessagePackSerializer.Deserialize(resolvedType, data, Options)!;
}
