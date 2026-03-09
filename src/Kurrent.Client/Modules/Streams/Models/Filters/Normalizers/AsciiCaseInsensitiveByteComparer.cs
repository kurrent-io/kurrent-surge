namespace Kurrent.Client.Streams;

#if NET10_0_OR_GREATER

sealed class AsciiCaseInsensitiveByteComparer
    : IEqualityComparer<byte[]>,
      IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]> {

    public static readonly AsciiCaseInsensitiveByteComparer Instance = new();

    public bool Equals(byte[]? x, byte[]? y) {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;

        return Equals((ReadOnlySpan<byte>)x, y);
    }

    public int GetHashCode(byte[] obj) => GetHashCode((ReadOnlySpan<byte>)obj);

    public bool Equals(ReadOnlySpan<byte> alternate, byte[] other) {
        if (alternate.Length != other.Length) return false;

        for (var i = 0; i < alternate.Length; i++) {
            if (ToLower(alternate[i]) != ToLower(other[i]))
                return false;
        }

        return true;
    }

    public int GetHashCode(ReadOnlySpan<byte> alternate) {
        var hash = new HashCode();

        foreach (var b in alternate)
            hash.Add(ToLower(b));

        return hash.ToHashCode();
    }

    public byte[] Create(ReadOnlySpan<byte> alternate) => alternate.ToArray();

    internal static byte ToLower(byte b) =>
        b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b | 0x20) : b;
}

#endif
