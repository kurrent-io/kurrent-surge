using Kurrent.Surge.Client;

namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Registers <see cref="AvroSchemaCodec"/> through the same
/// <see cref="KurrentClientMessageOptions.UseCodec"/> path every built-in and opt-in codec
/// uses. Import this namespace to bring <c>UseAvro()</c> into scope on <c>options.Messages</c>.
/// </summary>
[PublicAPI]
public static class KurrentClientMessageOptionsExtensions {
    extension(KurrentClientMessageOptions options) {
        public KurrentClientMessageOptions UseAvro() =>
            options.UseCodec(new AvroSchemaCodec());
    }
}
