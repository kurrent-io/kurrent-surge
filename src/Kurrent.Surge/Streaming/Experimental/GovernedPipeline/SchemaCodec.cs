using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// A pure payload codec: bytes in, bytes out. No governance, no context, no I/O — resolution
/// and registry work happen in the pipeline before a codec runs, so <see cref="Decode"/> always
/// receives a known type. Any I/O a codec seems to need is resolution work that belongs upstream.
/// </summary>
interface ISchemaCodec {
    SchemaDataFormat DataFormat { get; }

    ReadOnlyMemory<byte> Encode(object? value);

    object Decode(ReadOnlyMemory<byte> data, Type resolvedType);
}

/// <summary>
/// Frozen format → codec registry. Registering a codec is the entire custom-serializer
/// surface: two sync methods and a format.
/// </summary>
sealed class SchemaCodecProvider {
    readonly FrozenDictionary<SchemaDataFormat, ISchemaCodec> _codecs;

    public SchemaCodecProvider(IEnumerable<ISchemaCodec> codecs) =>
        _codecs = codecs.ToFrozenDictionary(codec => codec.DataFormat);

    public ISchemaCodec GetCodec(SchemaDataFormat format) =>
        _codecs.TryGetValue(format, out var codec)
            ? codec
            : throw new InvalidOperationException($"No codec registered for '{format}'.");

    /// <summary>
    /// Experimental bridge: exposes the live serializers' synchronous cores as codecs, so the
    /// governed pipelines run the exact production encode/decode logic. Real codecs replace
    /// this by promoting those cores to <see cref="ISchemaCodec"/> implementations.
    /// </summary>
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
