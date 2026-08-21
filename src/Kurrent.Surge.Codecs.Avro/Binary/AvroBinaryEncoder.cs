using System.Buffers.Binary;
using System.Text;

namespace Kurrent.Surge.Codecs.Avro.Binary;

/// <summary>
/// Writes the Avro binary encoding into a growable buffer it owns.
/// </summary>
/// <remarks>
/// Implements the Avro specification, "Binary encoding" — https://avro.apache.org/docs/1.12.0/specification/
/// <para>
/// Byte-for-byte agreement with Apache.Avro is asserted for every primitive in
/// <c>Kurrent.Surge.Codecs.Avro.Tests</c>. That test is the reason owning this is safe: the format is
/// small and frozen, and any drift from it fails loudly.
/// </para>
/// <para>
/// Writes into its own array rather than a <see cref="Stream"/>. A varint costs up to ten virtual
/// <c>Write</c> dispatches through a stream and none through a span, and the payload has to be copied
/// out of the stream's buffer afterwards anyway. <see cref="Reset"/> makes one encoder serve every
/// message on a thread, so the buffer is allocated once and grown to the steady state of the traffic.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class AvroBinaryEncoder {
    /// <summary>The buffer size a new encoder starts at.</summary>
    public const int DefaultCapacity = 256;

    /// <summary>A zig-zag encoded 64-bit value occupies at most ten bytes.</summary>
    const int MaxVarintBytes = 10;

    byte[] _buffer;
    int    _position;

    public AvroBinaryEncoder(int capacity = DefaultCapacity) {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);

        // WriteLong reserves the varint worst case in one check and then writes without bounds tests,
        // which a capacity below that would break.
        _buffer = GC.AllocateUninitializedArray<byte>(Math.Max(capacity, MaxVarintBytes));
    }

    /// <summary>How many bytes have been written.</summary>
    public int WrittenCount => _position;

    /// <summary>How large the buffer has grown.</summary>
    public int Capacity => _buffer.Length;

    /// <summary>The bytes written so far.</summary>
    /// <remarks>Valid until the next write or <see cref="Reset"/>, both of which may replace the buffer.</remarks>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _position);

    /// <inheritdoc cref="WrittenSpan"/>
    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _position);

    /// <summary>Discards what was written, so one encoder can write the next message.</summary>
    /// <remarks>Keeps the buffer at the size it has grown to.</remarks>
    public void Reset() => _position = 0;

    /// <summary>Null occupies no bytes at all.</summary>
    public void WriteNull() { }

    public void WriteBoolean(bool value) => Advance(1)[0] = value ? (byte)1 : (byte)0;

    public void WriteInt(int value) => WriteLong(value);

    /// <remarks>
    /// Variable-length zig-zag: the sign moves into the low bit so small negatives stay short, then
    /// seven bits per byte with the high bit marking continuation.
    /// </remarks>
    public void WriteLong(long value) {
        if (MaxVarintBytes > _buffer.Length - _position) Grow(MaxVarintBytes);

        var buffer   = _buffer;
        var position = _position;

        var bits = (ulong)((value << 1) ^ (value >> 63));

        while ((bits & ~0x7FUL) != 0) {
            buffer[position++] = (byte)((bits & 0x7F) | 0x80);
            bits >>= 7;
        }

        buffer[position++] = (byte)bits;

        _position = position;
    }

    /// <remarks>Four bytes, little-endian IEEE 754 — the layout Java's <c>floatToRawIntBits</c> gives.</remarks>
    public void WriteFloat(float value) => BinaryPrimitives.WriteSingleLittleEndian(Advance(sizeof(float)), value);

    /// <remarks>Eight bytes, little-endian IEEE 754.</remarks>
    public void WriteDouble(double value) => BinaryPrimitives.WriteDoubleLittleEndian(Advance(sizeof(double)), value);

    /// <summary>Bytes are a length followed by that many bytes of data.</summary>
    public void WriteBytes(ReadOnlySpan<byte> value) {
        WriteLong(value.Length);
        value.CopyTo(Advance(value.Length));
    }

    /// <summary>A string is its UTF-8 bytes, written as <see cref="WriteBytes"/> does.</summary>
    /// <remarks>
    /// Transcoded straight into the output. The length prefix has to be written first and its width
    /// depends on the byte count, so the string is measured before it is encoded; both passes are
    /// vectorised and neither allocates.
    /// </remarks>
    public void WriteString(string value) {
        var count = Encoding.UTF8.GetByteCount(value);

        WriteLong(count);
        Encoding.UTF8.GetBytes(value.AsSpan(), Advance(count));
    }

    public void WriteEnum(int value) => WriteLong(value);

    public void WriteUnionIndex(int value) => WriteLong(value);

    /// <summary>A fixed is its bytes, with no length prefix — the schema fixes the size.</summary>
    public void WriteFixed(ReadOnlySpan<byte> value) => value.CopyTo(Advance(value.Length));

    /// <summary>Reserves <paramref name="count"/> bytes of the output for the caller to fill.</summary>
    /// <remarks>
    /// For a value that is built rather than copied — a padded decimal, a duration's three little-endian
    /// words — so it is assembled in place instead of in a scratch buffer that is then copied here.
    /// <para>
    /// The bytes are uninitialised and the caller must write all of them. The span is valid until the
    /// next write, which may replace the buffer.
    /// </para>
    /// </remarks>
    public Span<byte> WriteFixedSpan(int count) => Advance(count);

    /// <summary>Writes a length prefix and reserves that many bytes for the caller to fill.</summary>
    /// <inheritdoc cref="WriteFixedSpan" path="/remarks"/>
    public Span<byte> WriteBytesSpan(int count) {
        WriteLong(count);

        return Advance(count);
    }

    /// <summary>A <c>uuid</c> backed by a <c>fixed(16)</c>, in RFC 4122 byte order.</summary>
    /// <remarks>
    /// Written straight into the output. <c>Guid.ToByteArray()</c> without the big-endian flag is
    /// little-endian across the first three fields and would byte-swap every value on the wire.
    /// </remarks>
    public void WriteUuid(Guid value) => value.TryWriteBytes(Advance(16), bigEndian: true, out _);

    #region ->> Blocked collections <<-

    public void WriteArrayStart() { }

    public void WriteMapStart() { }

    public void StartItem() { }

    /// <remarks>
    /// A zero count writes nothing. An empty collection is therefore just the terminating zero that
    /// <see cref="WriteArrayEnd"/> writes, and emitting the count here as well would encode an empty
    /// block that readers treat as the end of the collection.
    /// </remarks>
    public void SetItemCount(long value) {
        if (value > 0) WriteLong(value);
    }

    public void WriteArrayEnd() => WriteLong(0);

    public void WriteMapEnd() => WriteLong(0);

    #endregion // Blocked collections

    /// <summary>Claims <paramref name="count"/> bytes and returns them.</summary>
    Span<byte> Advance(int count) {
        if (count > _buffer.Length - _position) Grow(count);

        var span = _buffer.AsSpan(_position, count);

        _position += count;

        return span;
    }

    void Grow(int required) {
        // Long arithmetic because doubling past a gigabyte overflows int, and a length prefix comes off
        // the wire, so the required size is not always one this process chose.
        var capacity = Math.Max((long)_buffer.Length * 2, (long)_position + required);

        if (capacity > Array.MaxLength)
            throw new OutOfMemoryException($"An Avro payload of {capacity} bytes exceeds the largest array the runtime can allocate");

        var grown = GC.AllocateUninitializedArray<byte>((int)capacity);

        _buffer.AsSpan(0, _position).CopyTo(grown);

        _buffer = grown;
    }
}
