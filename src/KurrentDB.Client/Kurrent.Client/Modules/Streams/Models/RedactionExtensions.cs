using Kurrent.Client.Streams;

namespace Kurrent.Surge.Transformers;

[PublicAPI]
public static class RedactionExtensions {
    public static Span<byte> Redact(this Span<byte> data) {
        data.Fill(0xFF);
        return data;
    }

    public static byte[] Redact(this byte[] data) =>
        Redact(data.AsSpan()).ToArray();

    public static ReadOnlyMemory<byte> Redact(this ReadOnlyMemory<byte> data) =>
        Redact(data.ToArray());

    public static Record Redact(this Record record) =>
        record with {
            Value      = null!,
            ValueType  = null!,
            Data       = record.Data.Redact(),
            IsRedacted = true
        };
}
