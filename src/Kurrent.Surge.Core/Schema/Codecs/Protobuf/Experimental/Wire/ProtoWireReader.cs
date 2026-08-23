using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Wire;

/// <summary>
/// Protobuf wire types, as encoded in the low three bits of a tag.
/// </summary>
/// <remarks>See https://protobuf.dev/programming-guides/encoding/#structure.</remarks>
enum ProtoWireType {
    Varint          = 0,
    Fixed64         = 1,
    LengthDelimited = 2,
    StartGroup      = 3,
    EndGroup        = 4,
    Fixed32         = 5
}

/// <summary>
/// Reads protobuf wire format from a span.
/// </summary>
/// <remarks>
/// A <c>ref struct</c> over <see cref="ReadOnlySpan{T}"/> so a nested message is a slice rather
/// than a copy, and so the whole read allocates nothing.
/// </remarks>
ref struct ProtoWireReader(ReadOnlySpan<byte> buffer) {
    readonly ReadOnlySpan<byte> _buffer = buffer;

    int _position;

    public readonly bool IsAtEnd => _position >= _buffer.Length;

    public readonly int Position => _position;

    /// <summary>Reads the next tag, or zero at the end of the input.</summary>
    public uint ReadTag() => IsAtEnd ? 0 : (uint)ReadVarint();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FieldNumberOf(uint tag) => (int)(tag >> 3);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ProtoWireType WireTypeOf(uint tag) => (ProtoWireType)(tag & 0x7);

    public ulong ReadVarint() {
        EnsureAvailable(1);

        ulong first = _buffer[_position];

        if (first < 0x80) {
            _position++;
            return first;
        }

        ulong result = 0;
        var   pos    = _position;

        // Ten groups of seven bits carry a 64-bit value; anything longer is malformed.
        for (var shift = 0; shift < 70; shift += 7) {
            if (pos >= _buffer.Length) throw new InvalidDataException("The payload ends inside a varint.");

            var b = _buffer[pos++];

            result |= (ulong)(b & 0x7F) << shift;

            if ((b & 0x80) == 0) {
                _position = pos;
                return result;
            }
        }

        throw new InvalidDataException("The payload contains a varint longer than ten bytes.");
    }

    public uint ReadFixed32() {
        EnsureAvailable(4);

        var value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer[_position..]);

        _position += 4;

        return value;
    }

    public ulong ReadFixed64() {
        EnsureAvailable(8);

        var value = BinaryPrimitives.ReadUInt64LittleEndian(_buffer[_position..]);

        _position += 8;

        return value;
    }

    public ReadOnlySpan<byte> ReadLengthDelimited() {
        var length = checked((int)ReadVarint());

        if (length < 0) throw new InvalidDataException("The payload declares a negative length.");

        EnsureAvailable(length);

        var slice = _buffer.Slice(_position, length);

        _position += length;

        return slice;
    }

    public string ReadString() {
        var slice = ReadLengthDelimited();

        return slice.IsEmpty ? "" : Encoding.UTF8.GetString(slice);
    }

    /// <summary>Returns the bytes of a group, positioned past its end tag.</summary>
    public ReadOnlySpan<byte> ReadGroup(int fieldNumber) {
        var start = _position;

        while (true) {
            var tagStart = _position;
            var tag      = ReadTag();

            if (tag == 0) throw new InvalidDataException("The payload ends inside a group.");

            if (WireTypeOf(tag) == ProtoWireType.EndGroup && FieldNumberOf(tag) == fieldNumber)
                return _buffer[start..tagStart];

            Skip(WireTypeOf(tag), FieldNumberOf(tag));
        }
    }

    public void Skip(ProtoWireType wireType, int fieldNumber) {
        switch (wireType) {
            case ProtoWireType.Varint:
                ReadVarint();
                break;

            case ProtoWireType.Fixed64:
                SkipBytes(8);
                break;

            case ProtoWireType.LengthDelimited:
                SkipBytes(checked((int)ReadVarint()));
                break;

            case ProtoWireType.StartGroup:
                ReadGroup(fieldNumber);
                break;

            case ProtoWireType.EndGroup:
                break;

            case ProtoWireType.Fixed32:
                SkipBytes(4);
                break;

            default:
                throw new InvalidDataException($"The payload uses wire type {(int)wireType}, which is not defined.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int DecodeZigZag32(uint value) => (int)(value >> 1) ^ -(int)(value & 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long DecodeZigZag64(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);

    void SkipBytes(int count) {
        if (count < 0) throw new InvalidDataException("The payload declares a negative length.");

        EnsureAvailable(count);

        _position += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly void EnsureAvailable(int count) {
        if (_position + count > _buffer.Length)
            throw new InvalidDataException(
                $"The payload is truncated: {count} bytes needed at offset {_position}, {_buffer.Length - _position} available.");
    }
}
