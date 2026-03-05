using Kurrent.Client.Streams;

namespace Kurrent.Surge.Transformers;

[PublicAPI]
public static class RedactionExtensions {
    extension(Span<byte> data) {
        public Span<byte> Redact() {
            data.Fill(0xFF);
            return data;
        }
    }

    extension(byte[] data) {
        public byte[] Redact() => data.AsSpan().Redact().ToArray();
    }

    extension(ReadOnlyMemory<byte> data) {
        public ReadOnlyMemory<byte> Redact() => data.ToArray().Redact();
    }

    extension(Record record) {
        public Record Redact() => record with {
            Data       = record.Data.Redact(),
            Value      = null!,
            ValueType  = null!,
            IsRedacted = true
        };
    }
}
