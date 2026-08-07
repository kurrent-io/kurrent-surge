using Kurrent.Surge.Client;

namespace Kurrent.Surge.Client.Schema.Serialization.Avro;

/// <summary>
/// Registers <see cref="AvroSchemaSerializer"/> through the same
/// <see cref="KurrentClientMessageOptions.UseSerializer"/> path every built-in and opt-in serializer
/// uses. Import this namespace to bring <c>UseAvro()</c> into scope on <c>options.Messages</c>.
/// </summary>
[PublicAPI]
public static class KurrentClientMessageOptionsExtensions {
    extension(KurrentClientMessageOptions options) {
        public KurrentClientMessageOptions UseAvro(AvroSchemaSerializerOptions? config = null) =>
            options.UseSerializer(schemaManager => new AvroSchemaSerializer(config ?? AvroSchemaSerializerOptions.Default, schemaManager));
    }
}
