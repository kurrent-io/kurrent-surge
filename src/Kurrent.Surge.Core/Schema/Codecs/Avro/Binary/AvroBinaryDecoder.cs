using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Kurrent.Surge.Codecs.Avro.Binary;

/// <summary>
/// Reads the Avro binary encoding.
/// </summary>
/// <remarks>
/// Implements the Avro specification, "Binary encoding" — https://avro.apache.org/docs/1.12.0/specification/
/// <para>
/// Byte-for-byte agreement with Apache.Avro is asserted for every primitive in
/// <c>Kurrent.Surge.Codecs.Avro.Tests</c>.
/// </para>
/// </remarks>
/// <remarks>
/// Reads from memory rather than a <see cref="Stream"/>. A payload arrives as a
/// <see cref="ReadOnlyMemory{T}"/> already, and routing it through a stream costs an allocation per
/// message plus a virtual dispatch per byte — a ten-byte varint pays ten of them. Indexing a span costs
/// neither.
/// </remarks>
[PublicAPI]
public sealed class AvroBinaryDecoder(ReadOnlyMemory<byte> data) {
    int _position;

    /// <summary>How many bytes have been consumed.</summary>
    public int Position => _position;

    /// <summary>Rewinds to the start, so one decoder can read the same payload again.</summary>
    public void Reset() => _position = 0;

    /// <summary>Null occupies no bytes at all.</summary>
    public void ReadNull() { }

    public bool ReadBoolean() =>
        ReadByte() switch {
            0 => false,
            1 => true,
            var other => throw new InvalidDataException($"Expected a boolean, which is 0 or 1, but the payload held {other}")
        };

    public int ReadInt() => (int)ReadLong();

    /// <remarks>
    /// The inverse of the encoder's zig-zag: the low bit carries the sign, so it is broadcast to a mask
    /// and exclusive-ORed against the value shifted back down.
    /// <para>
    /// The span is taken once and indexed directly. Going byte-by-byte through a stream here was the
    /// single hottest cost in the format, since every int, length prefix, union index and block count
    /// routes through this method.
    /// </para>
    /// </remarks>
    public long ReadLong() {
        var span     = data.Span;
        var position = _position;

        ulong bits  = 0;
        var   shift = 0;

        while (true) {
            if ((uint)position >= (uint)span.Length)
                throw new EndOfStreamException("The payload ended in the middle of a value");

            var current = span[position++];

            bits |= (current & 0x7FUL) << shift;

            if ((current & 0x80) == 0) break;

            shift += 7;
        }

        _position = position;

        var value = (long)bits;

        return -(value & 0x01L) ^ ((value >> 1) & 0x7FFFFFFFFFFFFFFFL);
    }

    public float ReadFloat() => BinaryPrimitives.ReadSingleLittleEndian(Take(sizeof(float)));

    public double ReadDouble() => BinaryPrimitives.ReadDoubleLittleEndian(Take(sizeof(double)));

    /// <summary>Bytes are a length followed by that many bytes of data.</summary>
    public byte[] ReadBytes() => Take(ReadLength()).ToArray();

    /// <summary>Reads a byte string and returns it without copying.</summary>
    /// <remarks>
    /// The allocating overload exists because a <c>bytes</c> field's property really is a
    /// <c>byte[]</c>. This one is for a caller that reads the value rather than keeping it — a
    /// decimal's unscaled magnitude, for instance — and would otherwise allocate an array per field per
    /// message. The span points into the payload and is valid for as long as the payload is.
    /// </remarks>
    public ReadOnlySpan<byte> ReadBytesSpan() => Take(ReadLength());

    /// <summary>A string is its UTF-8 bytes, written as a byte string.</summary>
    /// <remarks>
    /// Decoded straight from the payload. Reading through a stream forces a copy into a scratch buffer
    /// first, and the string allocation is then unavoidable on top of it; here only the string is
    /// allocated.
    /// </remarks>
    public string ReadString() {
        var value = Take(ReadLength());

        return value.IsEmpty ? "" : Encoding.UTF8.GetString(value);
    }

    public int ReadEnum() => ReadInt();

    public int ReadUnionIndex() => ReadInt();

    /// <summary>A fixed is its bytes, with no length prefix — the schema fixes the size.</summary>
    public void ReadFixed(Span<byte> destination) => Take(destination.Length).CopyTo(destination);

    /// <summary>Reads a fixed of <paramref name="count"/> bytes and returns it without copying.</summary>
    /// <inheritdoc cref="ReadBytesSpan" path="/remarks"/>
    public ReadOnlySpan<byte> ReadFixedSpan(int count) => Take(count);

    /// <summary>A <c>uuid</c> backed by a <c>fixed(16)</c>, in RFC 4122 byte order.</summary>
    /// <remarks>
    /// Read straight off the payload, with no intermediate buffer. The big-endian flag is required:
    /// the default constructor reads the first three fields little-endian and would byte-swap every value.
    /// </remarks>
    public Guid ReadUuid() => new(Take(16), bigEndian: true);

    #region ->> Blocked collections <<-

    public long ReadArrayStart() => ReadItemCount();

    public long ReadArrayNext() => ReadItemCount();

    public long ReadMapStart() => ReadItemCount();

    public long ReadMapNext() => ReadItemCount();

    /// <remarks>
    /// A negative count means the writer emitted the block's byte size after it, so a reader that does
    /// not want the block can seek past it. The count itself is the absolute value. Dropping this makes
    /// every sized-block stream decode as garbage.
    /// </remarks>
    long ReadItemCount() {
        var count = ReadLong();

        if (count >= 0) return count;

        ReadLong();

        return -count;
    }

    #endregion // Blocked collections

    /// <summary>Reads a length prefix and rejects one the payload cannot possibly satisfy.</summary>
    /// <remarks>
    /// Reading from memory makes this free: the remaining length is known exactly, so a corrupt or
    /// hostile payload declaring a multi-gigabyte value is rejected before anything is allocated rather
    /// than after the allocation is attempted.
    /// </remarks>
    int ReadLength() {
        var length    = ReadLong();
        var remaining = data.Length - _position;

        if (length < 0)
            throw new InvalidDataException($"The payload declared a length of {length}, which is not a valid size");

        if (length > remaining)
            throw new InvalidDataException($"The payload declared a length of {length} but holds only {remaining} more bytes");

        return (int)length;
    }

    byte ReadByte() {
        if (_position >= data.Length) throw new EndOfStreamException("The payload ended in the middle of a value");

        return data.Span[_position++];
    }

    /// <summary>Consumes <paramref name="count"/> bytes and returns them without copying.</summary>
    ReadOnlySpan<byte> Take(int count) {
        if (count > data.Length - _position)
            throw new EndOfStreamException("The payload ended in the middle of a value");

        var value = data.Span.Slice(_position, count);

        _position += count;

        return value;
    }
}
