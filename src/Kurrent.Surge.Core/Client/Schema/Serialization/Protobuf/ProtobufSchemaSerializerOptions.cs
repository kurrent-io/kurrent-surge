namespace Kurrent.Surge.Client.Schema.Serialization.Protobuf;

public record ProtobufSchemaSerializerOptions : SchemaSerializerOptions {
	public static ProtobufSchemaSerializerOptions Default => new();

	/// <summary>
	/// An optional <see cref="ProtoSerializerContext"/> containing pre-registered protobuf
	/// message parsers and descriptors for AOT-compatible deserialization.
	/// </summary>
	public ProtoSerializerContext? Context { get; set; }
}
