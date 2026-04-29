namespace Kurrent.CodeAnalysis;

/// <summary>
/// Minimal hash combining helper. <see cref="System.HashCode"/> isn't available on
/// netstandard2.0 (and PolySharp doesn't polyfill it by default), so manual combining keeps
/// the library free of runtime-version concerns.
/// </summary>
public static class HashCombine {
    public const int Start = 17;
    const int        Prime = 31;

    public static int Add<T>(int hash, T? value) =>
        unchecked(hash * Prime + (value?.GetHashCode() ?? 0));

    public static int AddRange<T>(int hash, T[] values) {
        foreach (var v in values)
            hash = Add(hash, v);
        return hash;
    }
}
