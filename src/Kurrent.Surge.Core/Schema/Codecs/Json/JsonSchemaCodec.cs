using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Codecs.Json;

/// <summary>
/// JSON payload codec backed by System.Text.Json with source-generated metadata.
/// </summary>
public sealed class JsonSchemaCodec : ISchemaCodec {
	/// <summary>
	/// Initializes a new instance of the <see cref="JsonSchemaCodec"/> class.
	/// </summary>
	/// <param name="options">
	/// The JSON serialization options. Defaults to <see cref="JsonSchemaCodecOptions.Default"/>.
	/// </param>
	public JsonSchemaCodec(JsonSchemaCodecOptions? options = null) {
		options ??= JsonSchemaCodecOptions.Default;
		Serializer = new(options.JsonSerializerOptions, options.UseProtobufFormatter);
	}

	JsonSerializer Serializer { get; }

	/// <inheritdoc />
	public SchemaDataFormat DataFormat => SchemaDataFormat.Json;

	/// <inheritdoc />
	public ReadOnlyMemory<byte> Encode(object? value) =>
		Serializer.Serialize(value);

	/// <inheritdoc />
	public object Decode(ReadOnlyMemory<byte> data, Type resolvedType) =>
		Serializer.Deserialize(data, resolvedType)!;
}
