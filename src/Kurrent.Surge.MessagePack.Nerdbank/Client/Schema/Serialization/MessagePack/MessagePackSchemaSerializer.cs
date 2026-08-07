using Kurrent.Surge.Schema;
using Nerdbank.MessagePack;
using PolyType;

namespace Kurrent.Surge.Client.Schema.Serialization.MessagePack;

public class MessagePackSchemaSerializer(MessagePackSchemaSerializerOptions options, ISchemaManager schemaManager)
    : SchemaSerializerBase(options, schemaManager) {
    MessagePackSerializer Serializer        { get; } = options.MessagePackSerializer;
    ITypeShapeProvider    TypeShapeProvider { get; } = options.TypeShapeProvider;

    public override SchemaDataFormat DataFormat => SchemaDataFormat.MessagePack;

    protected override ReadOnlyMemory<byte> Serialize(object? value) =>
        Serializer.SerializeObject(value, TypeShapeProvider.GetTypeShapeOrThrow(value!.GetType()));

    protected override object Deserialize(ReadOnlyMemory<byte> data, Type resolvedType) =>
        Serializer.DeserializeObject(data, TypeShapeProvider.GetTypeShapeOrThrow(resolvedType))!;
}