using System.Collections.Immutable;

namespace Kurrent.Client.Streams;

public sealed record NormalizerOptions {
    public ImmutableArray<KeyValuePair<string, string>> ColumnReplacements   { get; init; } = [];
    public ImmutableArray<KeyValuePair<string, string>> TableReplacements    { get; init; } = [];
    public string?                                      ForbiddenTablePrefix { get; init; }
}

public readonly ref struct NormalizeResult {
    public ReadOnlySpan<char> Value   { get; init; }
    public bool               IsValid { get; init; }
    public string?            Error   { get; init; }

    public static NormalizeResult Success(ReadOnlySpan<char> normalized) =>
        new() {
            Value   = normalized,
            IsValid = true
        };

    public static NormalizeResult Failure(string error) =>
        new() {
            Value   = ReadOnlySpan<char>.Empty,
            IsValid = false,
            Error   = error
        };
}

public sealed class QueryExpressionNormalizer {
    readonly NormalizerOptions _options;

    public QueryExpressionNormalizer(NormalizerOptions options) {
        _options = options;
    }

    public NormalizeResult Normalize(ReadOnlySpan<char> query, Span<char> buffer) {
        var written = 0;

        for (var i = 0; i < query.Length; i++) {
            buffer[written++] = query[i];
        }

        return NormalizeResult.Success(buffer[..written]);
    }
}
