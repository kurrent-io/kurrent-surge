using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace ProtobufTranscoder.Wire;

/// <summary>Low-level protobuf wire format reader over a <see cref="ReadOnlySpan{T}">ReadOnlySpan&lt;byte&gt;</see>.</summary>
public ref struct CodedReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;
    private readonly int _limit;

    public CodedReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
        _limit = buffer.Length;
    }

    public bool IsAtEnd => _position >= _limit;

    public int Position => _position;

    public uint ReadTag()
    {
        if (IsAtEnd) return 0;
        return ReadRawVarint32();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetFieldNumber(uint tag) => (int)(tag >> 3);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static WireType GetWireType(uint tag) => (WireType)(tag & 0x7);

    public int ReadInt32() => (int)ReadRawVarint32();

    public long ReadInt64() => (long)ReadRawVarint64();

    public uint ReadUInt32() => ReadRawVarint32();

    public ulong ReadUInt64() => ReadRawVarint64();

    public int ReadSInt32() => DecodeZigZag32(ReadRawVarint32());

    public long ReadSInt64() => DecodeZigZag64(ReadRawVarint64());

    public bool ReadBool() => ReadRawVarint32() != 0;

    public float ReadFloat()
    {
        var raw = ReadRawLittleEndian32();
        return BitConverter.UInt32BitsToSingle(raw);
    }

    public double ReadDouble()
    {
        var raw = ReadRawLittleEndian64();
        return BitConverter.UInt64BitsToDouble(raw);
    }

    public uint ReadFixed32() => ReadRawLittleEndian32();

    public ulong ReadFixed64() => ReadRawLittleEndian64();

    public int ReadSFixed32() => (int)ReadRawLittleEndian32();

    public long ReadSFixed64() => (long)ReadRawLittleEndian64();

    public string ReadString()
    {
        var length = (int)ReadRawVarint32();
        if (length < 0)
            throw new DecodeException("Negative string length.");
        if (length == 0) return string.Empty;

        EnsureAvailable(length);
        var result = Encoding.UTF8.GetString(_buffer.Slice(_position, length));
        _position += length;
        return result;
    }

    public ReadOnlySpan<byte> ReadBytes()
    {
        var length = (int)ReadRawVarint32();
        if (length < 0)
            throw new DecodeException("Negative bytes length.");
        if (length == 0) return ReadOnlySpan<byte>.Empty;

        EnsureAvailable(length);
        var result = _buffer.Slice(_position, length);
        _position += length;
        return result;
    }

    public int ReadEnum() => (int)ReadRawVarint32();

    public CodedReader ReadMessage()
    {
        var length = (int)ReadRawVarint32();
        if (length < 0)
            throw new DecodeException("Negative message length.");

        EnsureAvailable(length);
        var subReader = new CodedReader(_buffer.Slice(_position, length));
        _position += length;
        return subReader;
    }

    /// <summary>Reads until the matching EndGroup tag and returns a reader scoped to the group bytes.</summary>
    public CodedReader ReadGroup(int fieldNumber)
    {
        var startPos = _position;
        while (true)
        {
            var nextTag = ReadTag();
            if (nextTag == 0)
                throw new DecodeException("Reached end of input while reading group.");

            if (GetWireType(nextTag) == WireType.EndGroup && GetFieldNumber(nextTag) == fieldNumber)
            {
                var groupLength = _position - startPos - TagSize(nextTag);
                return new CodedReader(_buffer.Slice(startPos, groupLength));
            }

            SkipField(nextTag);
        }
    }

    private static int TagSize(uint tag)
    {
        if (tag < 0x80) return 1;
        if (tag < 0x4000) return 2;
        if (tag < 0x200000) return 3;
        if (tag < 0x10000000) return 4;
        return 5;
    }

    public void SkipField(uint tag)
    {
        var wireType = GetWireType(tag);
        switch (wireType)
        {
            case WireType.Varint:
                ReadRawVarint64();
                break;
            case WireType.Fixed64:
                SkipRawBytes(8);
                break;
            case WireType.LengthDelimited:
                var length = (int)ReadRawVarint32();
                SkipRawBytes(length);
                break;
            case WireType.StartGroup:
                SkipGroup(tag);
                break;
            case WireType.EndGroup:
                break;
            case WireType.Fixed32:
                SkipRawBytes(4);
                break;
            default:
                throw new DecodeException($"Unknown wire type: {wireType}");
        }
    }

    private void SkipGroup(uint startTag)
    {
        var expectedFieldNumber = GetFieldNumber(startTag);
        while (true)
        {
            var nextTag = ReadTag();
            if (nextTag == 0)
                throw new DecodeException("Reached end of input while skipping group.");
            if (GetWireType(nextTag) == WireType.EndGroup && GetFieldNumber(nextTag) == expectedFieldNumber)
                return;
            SkipField(nextTag);
        }
    }

    private uint ReadRawVarint32()
    {
        EnsureAvailable(1);
        uint result = _buffer[_position];
        if (result < 0x80)
        {
            _position++;
            return result;
        }
        return ReadRawVarint32SlowPath();
    }

    private uint ReadRawVarint32SlowPath()
    {
        var pos = _position;
        uint result = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            if (pos >= _limit)
                throw new DecodeException("Truncated varint.");
            var b = _buffer[pos++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                _position = pos;
                return result;
            }
        }
        throw new DecodeException("Malformed varint (too many bytes).");
    }

    private ulong ReadRawVarint64()
    {
        EnsureAvailable(1);
        ulong first = _buffer[_position];
        if (first < 0x80)
        {
            _position++;
            return first;
        }
        return ReadRawVarint64SlowPath();
    }

    private ulong ReadRawVarint64SlowPath()
    {
        ulong result = 0;
        var pos = _position;
        for (var shift = 0; shift < 70; shift += 7)
        {
            if (pos >= _limit)
                throw new DecodeException("Truncated varint.");
            var b = _buffer[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                _position = pos;
                return result;
            }
        }
        throw new DecodeException("Malformed varint (too many bytes).");
    }

    private uint ReadRawLittleEndian32()
    {
        EnsureAvailable(4);
        var result = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
            _buffer.Slice(_position));
        _position += 4;
        return result;
    }

    private ulong ReadRawLittleEndian64()
    {
        EnsureAvailable(8);
        var result = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
            _buffer.Slice(_position));
        _position += 8;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodeZigZag32(uint n) => (int)(n >> 1) ^ -(int)(n & 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long DecodeZigZag64(ulong n) => (long)(n >> 1) ^ -(long)(n & 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureAvailable(int count)
    {
        if (_position + count > _limit)
            throw new DecodeException(
                $"Truncated message: need {count} bytes at position {_position}, but only {_limit - _position} remaining.");
    }

    private void SkipRawBytes(int count)
    {
        if (count < 0)
            throw new DecodeException("Negative skip size.");
        EnsureAvailable(count);
        _position += count;
    }
}
