namespace Kurrent.Surge.Codecs.Protobuf;

public sealed class ProtobufSchemaCodecOptions : SchemaCodecOptions {
	public static ProtobufSchemaCodecOptions Default => new();

	/// <summary>
	/// An optional <see cref="ProtobufCodecContext"/> containing pre-registered protobuf
	/// message parsers and descriptors for AOT-compatible deserialization.
	/// </summary>
	public ProtobufCodecContext? Context { get; set; }
}
