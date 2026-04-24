using Google.Protobuf;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Schema.Serialization.Protobuf;

public record ProtobufSchemaSerializerOptions : SchemaSerializerOptions {
	public static ProtobufSchemaSerializerOptions Default => new();

	/// <summary>
	/// An optional <see cref="ProtoSerializerContext"/> containing pre-registered protobuf
	/// message parsers and descriptors for AOT-compatible deserialization.
	/// </summary>
	public ProtoSerializerContext? Context { get; set; }
}

public class ProtobufSchemaSerializer(ProtobufSchemaSerializerOptions options, SchemaManager schemaManager)
	: SchemaSerializerBase(options, schemaManager) {
	public override SchemaDataFormat DataFormat => SchemaDataFormat.Protobuf;

	protected override ReadOnlyMemory<byte> Serialize(object? value) =>
		value.EnsureValueIsProtoMessage().ToByteArray();

	protected override object Deserialize(ReadOnlyMemory<byte> data, Type resolvedType) =>
		resolvedType.EnsureTypeIsProtoMessage().GetProtoMessageParser().ParseFrom(data.Span);

	protected object Deserialize(Stream data, Type resolvedType) =>
		resolvedType.EnsureTypeIsProtoMessage().GetProtoMessageParser().ParseFrom(data);
}
