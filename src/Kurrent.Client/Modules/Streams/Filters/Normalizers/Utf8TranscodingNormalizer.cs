using System.Buffers;
using System.Text;
using DotNext.Buffers;

namespace Kurrent.Client.Streams;

#if NET9_0_OR_GREATER

public sealed class Utf8TranscodingNormalizer(LegacyQueryExpressionNormalizer inner) : IUtf8QueryExpressionNormalizer {
    const int StackThreshold = 4096;

    public void Normalize(ReadOnlySpan<byte> query, ref BufferWriterSlim<byte> writer) {
        var charCount = Encoding.UTF8.GetCharCount(query);

        char[]? rentedInput = null;
        var charInput = charCount <= StackThreshold
            ? stackalloc char[charCount]
            : (rentedInput = ArrayPool<char>.Shared.Rent(charCount)).AsSpan(0, charCount);

        try {
            Encoding.UTF8.GetChars(query, charInput);

            char[]? rentedOutput = null;
            var outputLen  = charCount * 2 + 1;
            var charOutput = outputLen <= StackThreshold
                ? stackalloc char[outputLen]
                : (rentedOutput = ArrayPool<char>.Shared.Rent(outputLen)).AsSpan(0, outputLen);

            try {
                var result = inner.Normalize(charInput, charOutput);

                if (!result.IsValid)
                    throw new QueryNormalizationException(result.Error!);

                var utf8Count = Encoding.UTF8.GetByteCount(result.Value);
                var dest      = writer.GetSpan(utf8Count);

                Encoding.UTF8.GetBytes(result.Value, dest);
                writer.Advance(utf8Count);
            }
            finally {
                if (rentedOutput is { })
                    ArrayPool<char>.Shared.Return(rentedOutput);
            }
        }
        finally {
            if (rentedInput is { })
                ArrayPool<char>.Shared.Return(rentedInput);
        }
    }
}

#endif
