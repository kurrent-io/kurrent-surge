using Kurrent.Surge.Client;

namespace Kurrent.Surge.Client.Schema.Serialization.MessagePack;

/// <summary>
/// Registers <see cref="MessagePackSchemaSerializer"/> (official MessagePack-CSharp) through the
/// same <see cref="KurrentClientMessageOptions.UseSerializer"/> path every built-in and opt-in
/// serializer uses. Import this namespace to bring <c>UseMessagePack()</c> into scope on
/// <c>options.Messages</c>.
/// </summary>
[PublicAPI]
public static class KurrentClientMessageOptionsExtensions {
    extension(KurrentClientMessageOptions options) {
        public KurrentClientMessageOptions UseMessagePack(MessagePackSchemaSerializerOptions? config = null) =>
            options.UseSerializer(schemaManager => new MessagePackSchemaSerializer(config ?? MessagePackSchemaSerializerOptions.Default, schemaManager));
    }
}
