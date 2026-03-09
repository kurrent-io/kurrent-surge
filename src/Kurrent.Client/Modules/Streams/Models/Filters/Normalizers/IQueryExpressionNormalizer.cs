namespace Kurrent.Client.Streams;

#if NET9_0_OR_GREATER

public interface IQueryExpressionNormalizer {
    NormalizeResult Normalize(ReadOnlySpan<char> query, Span<char> buffer);
}

#endif
