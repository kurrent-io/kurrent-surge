using Kurrent.Surge.Client;

namespace Kurrent.Surge.Client.Schema.Serialization.MessagePack;

/// <summary>
/// Registers <see cref="MessagePackSchemaCodec"/> (Nerdbank.MessagePack) through the same
/// <see cref="KurrentClientMessageOptions.UseCodec"/> path every built-in and opt-in codec
/// uses. Import this namespace to bring <c>UseNerdbankMessagePack()</c> into scope on
/// <c>options.Messages</c>.
/// </summary>
[PublicAPI]
public static class KurrentClientMessageOptionsExtensions {
    extension(KurrentClientMessageOptions options) {
        /// <summary>
        /// No parameterless overload exists: <see cref="NerdbankMessagePackSchemaCodecOptions.TypeShapeProvider"/>
        /// is required, with no reflection-based default to fall back to.
        /// </summary>
        public KurrentClientMessageOptions UseNerdbankMessagePack(NerdbankMessagePackSchemaCodecOptions config) =>
            options.UseCodec(new MessagePackSchemaCodec(config));
    }
}
