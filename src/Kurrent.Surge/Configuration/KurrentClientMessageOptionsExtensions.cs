using Kurrent.Surge.Client.Schema.Serialization.Bytes;
using Kurrent.Surge.Client.Schema.Serialization.Json;
using Kurrent.Surge.Client.Schema.Serialization.Protobuf;

namespace Kurrent.Surge.Client;

/// <summary>
/// Registers the SDK's built-in schema serializers through the same
/// <see cref="KurrentClientMessageOptions.UseSerializer"/> path any opt-in serializer package uses.
/// </summary>
[PublicAPI]
public static class KurrentClientMessageOptionsExtensions {
    extension(KurrentClientMessageOptions options) {
        public KurrentClientMessageOptions UseBytes() =>
            options.UseSerializer(_ => new BytesPassthroughSerializer());

        public KurrentClientMessageOptions UseJson() =>
            options.UseSerializer(schemaManager => new JsonSchemaSerializer(options.Json, schemaManager));

        public KurrentClientMessageOptions UseProtobuf() =>
            options.UseSerializer(schemaManager => new ProtobufSchemaSerializer(options.Protobuf, schemaManager));
    }
}
