namespace Kurrent.Client.Streams;

#if NET9_0_OR_GREATER

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

#endif
