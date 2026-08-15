using Google.Protobuf;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Schema.Serialization.Protobuf;

/// <summary>
/// Protobuf payload codec for <see cref="IMessage"/>-implementing types.
/// </summary>
public sealed class ProtobufSchemaCodec : ISchemaCodec {
	/// <inheritdoc />
	public SchemaDataFormat DataFormat => SchemaDataFormat.Protobuf;

	/// <inheritdoc />
	public ReadOnlyMemory<byte> Encode(object? value) =>
		value.EnsureValueIsProtoMessage().ToByteArray();

	/// <inheritdoc />
	public object Decode(ReadOnlyMemory<byte> data, Type resolvedType) =>
		resolvedType.EnsureTypeIsProtoMessage().GetProtoMessageParser().ParseFrom(data.Span);
}
