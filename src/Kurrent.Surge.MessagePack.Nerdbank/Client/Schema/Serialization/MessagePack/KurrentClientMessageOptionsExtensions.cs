using Kurrent.Surge.Client;

namespace Kurrent.Surge.Client.Schema.Serialization.MessagePack;

/// <summary>
/// Registers <see cref="NerdbankMessagePackSchemaSerializer"/> (Nerdbank.MessagePack) through the same
/// <see cref="KurrentClientMessageOptions.UseSerializer"/> path every built-in and opt-in serializer
/// uses. Import this namespace to bring <c>UseNerdbankMessagePack()</c> into scope on
/// <c>options.Messages</c>.
/// </summary>
[PublicAPI]
public static class KurrentClientMessageOptionsExtensions {
    extension(KurrentClientMessageOptions options) {
        /// <summary>
        /// No parameterless overload exists: <see cref="NerdbankMessagePackSchemaSerializerOptions.TypeShapeProvider"/>
        /// is required, with no reflection-based default to fall back to.
        /// </summary>
        public KurrentClientMessageOptions UseNerdbankMessagePack(NerdbankMessagePackSchemaSerializerOptions config) =>
            options.UseSerializer(schemaManager => new NerdbankMessagePackSchemaSerializer(config, schemaManager));
    }
}
