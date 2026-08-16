using System.Runtime.InteropServices;
using Avro.IO;
using Avro.Specific;
using Kurrent.Surge.Codecs;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Schema.Serialization.Avro;

/// <summary>
/// Avro payload codec backed by the official Apache.Avro library, in Specific mode: the
/// message type is an avrogen-generated class implementing <see cref="ISpecificRecord"/>, whose
/// <see cref="ISpecificRecord.Schema"/> is baked in at generation time — no schema derivation
/// and no reflection-based schema construction.
/// </summary>
public sealed class AvroSchemaCodec : ISchemaCodec {
    /// <inheritdoc />
    public SchemaDataFormat DataFormat => SchemaDataFormat.Avro;

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Encode(object? value) {
        var record         = value.EnsureValueIsAvroRecord();
        var (_, writer, _) = AvroCodecs.System.GetCodec(record.GetType());

        using var stream = new MemoryStream();
        writer.Write(record, new BinaryEncoder(stream));
        return stream.ToArray();
    }

    /// <inheritdoc />
    public object Decode(ReadOnlyMemory<byte> data, Type resolvedType) {
        var (_, _, reader) = AvroCodecs.System.GetCodec(resolvedType);
        var record         = (ISpecificRecord)Activator.CreateInstance(resolvedType)!;

        using var stream = ToStream(data);
        return reader.Read(record, new BinaryDecoder(stream))!;
    }

    static MemoryStream ToStream(ReadOnlyMemory<byte> data) =>
        MemoryMarshal.TryGetArray(data, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(data.ToArray());
}
