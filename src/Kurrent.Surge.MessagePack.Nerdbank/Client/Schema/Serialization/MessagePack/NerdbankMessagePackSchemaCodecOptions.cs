using Kurrent.Surge.Codecs;
using PolyType;

namespace Kurrent.Surge.Client.Schema.Serialization.MessagePack;

[PublicAPI]
public record NerdbankMessagePackSchemaCodecOptions : SchemaCodecOptions {
	/// <summary>
	/// The type shape provider for the consumer's registered message types, typically the
	/// source-generated <c>Witness.GeneratedTypeShapeProvider</c> from a partial class carrying
	/// one <see cref="GenerateShapeForAttribute{T}"/> per message type. Nerdbank.MessagePack has
	/// no reflection-based fallback, so this cannot default to one: every type this serializer
	/// is asked to handle must resolve through the supplied provider.
	/// </summary>
	public required ITypeShapeProvider TypeShapeProvider { get; init; }

	/// <summary>
	/// The underlying Nerdbank.MessagePack serializer instance. Override to configure
	/// compression, reference preservation, security limits, or other serializer-wide settings.
	/// </summary>
	public global::Nerdbank.MessagePack.MessagePackSerializer MessagePackSerializer { get; init; } = new();
}
