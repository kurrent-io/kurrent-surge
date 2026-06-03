namespace Kurrent.CodeAnalysis;

/// <summary>
/// Minimal, allocation-free hash combiner for source-generator model records. The framework
/// <c>System.HashCode</c> isn't available on netstandard2.0, so we roll our own. A readonly
/// fluent struct: each step returns a fresh value, so a whole hash code collapses into one chain
/// and the implicit <c>int</c> conversion drops the trailing <c>ToHashCode()</c>.
/// <code>
/// public override int GetHashCode() =>
///     HashCode.New.Combine(Name).Combine(Kind).Combine(TypeArguments);
/// </code>
/// The fixed seed keeps results stable within a compilation — enough for the incremental-generator
/// cache equality this backs.
/// </summary>
public readonly struct HashCode {
    const int Seed  = 17;
    const int Prime = 31;

    readonly int _hash;

    HashCode(int hash) => _hash = hash;

    /// <summary>The starting point of a combine chain.</summary>
    public static HashCode New => new(Seed);

    /// <summary>Folds a single value into the running hash.</summary>
    public HashCode Combine<T>(T? value) =>
        new(unchecked(_hash * Prime + (value?.GetHashCode() ?? 0)));

    /// <summary>
    /// Folds each element in order. An array argument binds here, not to the scalar overload, so
    /// its contents are hashed element-wise rather than by reference (which would be non-deterministic).
    /// </summary>
    public HashCode Combine<T>(T[] values) {
        var hash = values.Aggregate(_hash, (current, v) => unchecked(current * Prime + (v?.GetHashCode() ?? 0)));
        return new(hash);
    }

    /// <summary>Returns the combined hash code.</summary>
    public int ToHashCode() => _hash;

    public static implicit operator int(HashCode hashCode) => hashCode._hash;
}
