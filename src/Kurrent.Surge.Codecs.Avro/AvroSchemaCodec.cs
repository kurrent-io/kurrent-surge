using System.Runtime.InteropServices;
using Avro.IO;
using Avro.Specific;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Avro payload codec over source-generated encode/decode code.
/// </summary>
/// <remarks>
/// The generated code drives Apache.Avro's byte primitives directly. Apache's
/// <c>SpecificDatumReader</c>/<c>SpecificDatumWriter</c> are deliberately not used: they walk the schema
/// at runtime through <c>Avro.Specific.ObjectCreator</c>, which resolves types with
/// <c>Type.GetType(string)</c> and builds generics with <c>MakeGenericType</c>. Both require runtime
/// code generation, which Native AOT does not have.
/// </remarks>
public sealed class AvroSchemaCodec : ISchemaCodec {
    const int InitialCapacity = 256;

    /// <summary>Beyond this, the scratch buffer is dropped rather than kept for the thread's lifetime.</summary>
    const int RetainedCapacityLimit = 64 * 1024;

    // One scratch buffer per thread. Neither method re-enters the codec — generated code drives the byte
    // primitives directly — so a single buffer per thread needs no further synchronisation.
    [ThreadStatic] static MemoryStream? WriteBuffer;

    /// <inheritdoc />
    public SchemaDataFormat DataFormat => SchemaDataFormat.Avro;

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Encode(object? value) {
        var record = value.EnsureValueIsAvroRecord();
        var codec  = GetCodec(record.GetType());

        var stream = WriteBuffer ??= new MemoryStream(InitialCapacity);

        stream.SetLength(0);

        codec.Encode(record, new BinaryEncoder(stream));

        var payload = stream.ToArray();

        // A single outsized message must not pin its buffer to this thread forever.
        if (stream.Capacity > RetainedCapacityLimit) WriteBuffer = null;

        return payload;
    }

    /// <inheritdoc />
    public object Decode(ReadOnlyMemory<byte> data, Type resolvedType) {
        var codec = GetCodec(resolvedType);

        // Measured against a reusable stream: MemoryStream decodes about a third faster, and the 64 bytes
        // a reusable one saves do not pay for that. See AvroCodecBenchmarks.
        using var stream = MemoryMarshal.TryGetArray(data, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(data.ToArray());

        return codec.Decode(new BinaryDecoder(stream));
    }

    static AvroCodecEntry GetCodec(Type messageType) =>
        AvroCodecRegistry.TryGet(messageType, out var codec)
            ? codec
            : throw new AvroCodecNotGeneratedException(messageType);
}
