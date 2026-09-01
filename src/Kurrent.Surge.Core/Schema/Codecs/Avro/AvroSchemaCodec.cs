using Kurrent.Surge.Codecs.Avro.Binary;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Avro payload codec over source-generated encode/decode code.
/// </summary>
/// <remarks>
/// The generated code drives our own byte primitives. Apache's
/// <c>SpecificDatumReader</c>/<c>SpecificDatumWriter</c> are deliberately not used: they walk the schema
/// at runtime through <c>Avro.Specific.ObjectCreator</c>, which resolves types with
/// <c>Type.GetType(string)</c> and builds generics with <c>MakeGenericType</c>. Both require runtime
/// code generation, which Native AOT does not have.
/// </remarks>
public sealed class AvroSchemaCodec : ISchemaCodec {
    /// <summary>Beyond this, the encoder is dropped rather than kept for the thread's lifetime.</summary>
    const int RetainedCapacityLimit = 64 * 1024;

    // One encoder per thread, and the buffer it owns with it. Neither method re-enters the codec —
    // generated code drives the byte primitives directly — so a single encoder per thread needs no
    // further synchronisation.
    [ThreadStatic] static AvroBinaryEncoder? Writer;

    /// <inheritdoc />
    public SchemaDataFormat DataFormat => SchemaDataFormat.Avro;

    /// <inheritdoc />
    /// <remarks>
    /// The registry lookup is the validation. Testing for <c>ISpecificRecord</c> first said nothing the
    /// lookup does not say better — a type can implement it and still have no generated codec — and it
    /// tied the codec to an interface generated types no longer carry by default.
    /// <para>
    /// The copy out of the encoder is unavoidable: this returns a <see cref="ReadOnlyMemory{T}"/> the
    /// caller may hold, while the encoder's buffer belongs to the next message on this thread.
    /// </para>
    /// </remarks>
    public ReadOnlyMemory<byte> Encode(object? value) {
        ArgumentNullException.ThrowIfNull(value);

        var codec = GetCodec(value.GetType());

        var encoder = Writer ??= new AvroBinaryEncoder();

        encoder.Reset();

        codec.Encode(value, encoder);

        var payload = encoder.WrittenSpan.ToArray();

        // A single outsized message must not pin its buffer to this thread forever.
        if (encoder.Capacity > RetainedCapacityLimit) Writer = null;

        return payload;
    }

    /// <inheritdoc />
    /// <remarks>
    /// No stream. The decoder reads the payload's memory directly, so decoding allocates nothing beyond
    /// the message itself — the <c>MemoryStream</c> this used to wrap the payload in was pure overhead
    /// once Apache's <c>Decoder</c>, which could only read a stream, was no longer in the path.
    /// </remarks>
    public object Decode(ReadOnlyMemory<byte> data, Type resolvedType) =>
        GetCodec(resolvedType).Decode(new AvroBinaryDecoder(data));

    static AvroCodecEntry GetCodec(Type messageType) =>
        AvroCodecRegistry.TryGet(messageType, out var codec)
            ? codec
            : throw new AvroCodecNotGeneratedException(messageType);
}
