namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Scratch buffers for the fixed-width logical types.
/// </summary>
/// <remarks>
/// A <c>uuid</c> or <c>decimal</c> backed by a fixed has to cross Apache's IO layer as a
/// <c>byte[]</c> — <c>Encoder.WriteFixed</c> and <c>Decoder.ReadFixed</c> take no span — so a naive
/// implementation allocates one array per field per message. Both accept an offset and a length, so a
/// buffer can be reused instead.
/// <para>
/// The buffer is valid only until the next call on the same thread. Every use in generated code fills it
/// and consumes it in the same statement sequence, and <c>BinaryEncoder</c> writes through to its stream
/// synchronously, so no two uses are ever live at once.
/// </para>
/// </remarks>
[PublicAPI]
public static class AvroBuffers {
    const int MinimumSize = 32;

    [ThreadStatic] static byte[]? Scratch;

    /// <summary>A buffer of at least <paramref name="size"/> bytes, valid until the next call.</summary>
    public static byte[] Rent(int size) {
        var buffer = Scratch;

        if (buffer is null || buffer.Length < size) Scratch = buffer = new byte[Math.Max(size, MinimumSize)];

        return buffer;
    }
}
