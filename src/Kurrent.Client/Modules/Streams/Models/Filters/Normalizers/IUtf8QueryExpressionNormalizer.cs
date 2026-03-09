using DotNext.Buffers;

namespace Kurrent.Client.Streams;

#if NET9_0_OR_GREATER

public interface IUtf8QueryExpressionNormalizer {
    Utf8NormalizeResult Normalize(ReadOnlySpan<byte> query, ref BufferWriterSlim<byte> writer);
}

public readonly struct Utf8NormalizeResult {
    public bool    IsValid { get; init; }
    public string? Error   { get; init; }

    public static Utf8NormalizeResult Success() =>
        new() { IsValid = true };

    public static Utf8NormalizeResult Failure(string error) =>
        new() { IsValid = false, Error = error };
}

#endif
