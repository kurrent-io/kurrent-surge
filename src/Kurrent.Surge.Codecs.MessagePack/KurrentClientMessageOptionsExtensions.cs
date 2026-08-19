using Kurrent.Surge.Client;

namespace Kurrent.Surge.Codecs.MessagePack;

/// <summary>
/// Registers <see cref="MessagePackSchemaCodec"/> (official MessagePack-CSharp) through the
/// same <see cref="KurrentClientMessageOptions.UseCodec"/> path every built-in and opt-in codec
/// uses. Import this namespace to bring <c>UseMessagePack()</c> into scope on
/// <c>options.Messages</c>.
/// </summary>
[PublicAPI]
public static class KurrentClientMessageOptionsExtensions {
    extension(KurrentClientMessageOptions options) {
        public KurrentClientMessageOptions UseMessagePack(MessagePackSchemaCodecOptions? config = null) =>
            options.UseCodec(new MessagePackSchemaCodec(config));
    }
}
