using Google.Protobuf;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Schema.Serialization.Protobuf;

public class ProtobufSchemaSerializer(ProtobufSchemaSerializerOptions options, ISchemaManager schemaManager)
	: SchemaSerializerBase(options, schemaManager) {
	public override SchemaDataFormat DataFormat => SchemaDataFormat.Protobuf;

	protected override ReadOnlyMemory<byte> Serialize(object? value) =>
		value.EnsureValueIsProtoMessage().ToByteArray();

	protected override object Deserialize(ReadOnlyMemory<byte> data, Type resolvedType) =>
		resolvedType.EnsureTypeIsProtoMessage().GetProtoMessageParser().ParseFrom(data.Span);

	protected object Deserialize(Stream data, Type resolvedType) =>
		resolvedType.EnsureTypeIsProtoMessage().GetProtoMessageParser().ParseFrom(data);
}
