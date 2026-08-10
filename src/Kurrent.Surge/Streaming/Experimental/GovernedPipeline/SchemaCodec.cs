using System.Runtime.CompilerServices;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// Experimental bridge: exposes the live serializers' synchronous cores as codecs, so the
/// governed pipelines run the exact production encode/decode logic. Dies when the client
/// composes real <see cref="ISchemaCodec"/> implementations at construction.
/// </summary>
static class SerializerCoreCodecs {
    public static SchemaCodecProvider From(ISchemaSerializerProvider serializerProvider) {
        var codecs = new List<ISchemaCodec>();

        foreach (var format in Enum.GetValues<SchemaDataFormat>()) {
            if (serializerProvider.SupportsDataFormat(format) && serializerProvider.GetSerializer(format) is SchemaSerializerBase serializer)
                codecs.Add(new SerializerCoreCodec(serializer, format));
        }

        return new(codecs);
    }

    sealed class SerializerCoreCodec(SchemaSerializerBase serializer, SchemaDataFormat format) : ISchemaCodec {
        public SchemaDataFormat DataFormat => format;

        public ReadOnlyMemory<byte> Encode(object? value) => SerializeCore(serializer, value);

        public object Decode(ReadOnlyMemory<byte> data, Type resolvedType) => DeserializeCore(serializer, data, resolvedType);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Serialize")]
        static extern ReadOnlyMemory<byte> SerializeCore(SchemaSerializerBase instance, object? value);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Deserialize")]
        static extern object DeserializeCore(SchemaSerializerBase instance, ReadOnlyMemory<byte> data, Type resolvedType);
    }
}
