namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Structural comparison for the members a record's synthesized equality gets wrong.
/// </summary>
/// <remarks>
/// A C# record compares a <c>byte[]</c>, a list or a dictionary member by reference, so two decoded
/// messages carrying identical data are unequal. Generated types implement <c>Equals</c> and
/// <c>GetHashCode</c> themselves and call these, which makes value equality mean what it says.
/// <para>
/// The comparison of each element arrives as a delegate rather than an
/// <see cref="System.Collections.Generic.IEqualityComparer{T}"/>, so the generator can nest them for a
/// list of lists or a map of records without building a comparer type per shape.
/// </para>
/// </remarks>
[PublicAPI]
public static class AvroEquality {
    public static bool BytesEqual(byte[]? left, byte[]? right) =>
        ReferenceEquals(left, right) || (left is not null && right is not null && left.AsSpan().SequenceEqual(right));

    public static int BytesHash(byte[]? value) {
        if (value is null) return 0;

        var hash = new HashCode();

        hash.AddBytes(value);

        return hash.ToHashCode();
    }

    public static bool ListEqual<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right, Func<T, T, bool> equals) {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.Count != right.Count) return false;

        for (var index = 0; index < left.Count; index++)
            if (!equals(left[index], right[index]))
                return false;

        return true;
    }

    public static int ListHash<T>(IReadOnlyList<T>? value, Func<T, int> hash) {
        if (value is null) return 0;

        var result = new HashCode();

        result.Add(value.Count);

        // Indexed rather than enumerated: foreach over the interface boxes the list's struct enumerator.
        for (var index = 0; index < value.Count; index++) result.Add(hash(value[index]));

        return result.ToHashCode();
    }

    public static bool MapEqual<TValue>(
        IReadOnlyDictionary<string, TValue>? left,
        IReadOnlyDictionary<string, TValue>? right,
        Func<TValue, TValue, bool> equals) {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.Count != right.Count) return false;

        // Enumerating through the interface boxes the dictionary's struct enumerator. The decoder always
        // produces a Dictionary, so the concrete path carries the common case without allocating.
        if (left is Dictionary<string, TValue> concrete) {
            foreach (var entry in concrete)
                if (!right.TryGetValue(entry.Key, out var value) || !equals(entry.Value, value))
                    return false;

            return true;
        }

        foreach (var entry in left)
            if (!right.TryGetValue(entry.Key, out var other) || !equals(entry.Value, other))
                return false;

        return true;
    }

    /// <remarks>
    /// A map has no order, so the per-entry hashes are combined with a commutative operation rather than
    /// fed to <see cref="HashCode"/> in sequence.
    /// </remarks>
    public static int MapHash<TValue>(IReadOnlyDictionary<string, TValue>? value, Func<TValue, int> hash) {
        if (value is null) return 0;

        var result = value.Count;

        if (value is Dictionary<string, TValue> concrete) {
            foreach (var entry in concrete) result ^= HashCode.Combine(entry.Key, hash(entry.Value));

            return result;
        }

        foreach (var entry in value) result ^= HashCode.Combine(entry.Key, hash(entry.Value));

        return result;
    }
}
