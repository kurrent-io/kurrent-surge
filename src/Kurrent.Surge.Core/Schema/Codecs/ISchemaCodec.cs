using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Codecs;

/// <summary>
/// Encodes and decodes record payloads for one <see cref="SchemaDataFormat"/>.
/// </summary>
/// <remarks>
/// <para>
/// A codec is pure: bytes in, bytes out, no I/O. Schema resolution and registry work run in
/// the client's record pipelines before a codec is invoked, so <see cref="Decode"/> always
/// receives the resolved target type.
/// </para>
/// <para>
/// Implement this interface and register the instance through
/// <c>KurrentClientMessageOptions.UseCodec</c> to support a custom payload format.
/// </para>
/// </remarks>
public interface ISchemaCodec {
	/// <summary>
	/// Gets the data format this codec handles.
	/// </summary>
	SchemaDataFormat DataFormat { get; }

	/// <summary>
	/// Encodes a value into its wire payload.
	/// </summary>
	/// <param name="value">The value to encode.</param>
	/// <returns>The encoded payload.</returns>
	ReadOnlyMemory<byte> Encode(object? value);

	/// <summary>
	/// Decodes a wire payload into an instance of the resolved type.
	/// </summary>
	/// <param name="data">The payload to decode.</param>
	/// <param name="resolvedType">The type to decode into.</param>
	/// <returns>The decoded instance.</returns>
	object Decode(ReadOnlyMemory<byte> data, Type resolvedType);
}
