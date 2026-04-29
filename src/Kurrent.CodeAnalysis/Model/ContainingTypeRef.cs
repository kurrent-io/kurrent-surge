namespace Kurrent.CodeAnalysis;

/// <summary>
/// Outer-to-inner entry describing a type that nests the target type. Used by generators that
/// must emit the target wrapped in matching <c>partial</c> declarations.
/// </summary>
/// <remarks>
/// <see cref="TypeTraits.Partial"/> is forced on every ref because wrapper declarations must be
/// <c>partial</c> to contain generator output, regardless of whether the user wrote <c>partial</c>
/// at the containing-type source.
/// </remarks>
public sealed record ContainingTypeRef(
    string      Name,
    TypeKind    Kind,
    TypeTraits  Traits,
    string[]    TypeParameterNames) {

    public bool Equals(ContainingTypeRef? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Name   == other.Name
            && Kind   == other.Kind
            && Traits == other.Traits
            && TypeParameterNames.AsSpan().SequenceEqual(other.TypeParameterNames.AsSpan());
    }

    public override int GetHashCode() {
        var hash = HashCombine.Start;
        hash = HashCombine.Add(hash, Name);
        hash = HashCombine.Add(hash, Kind);
        hash = HashCombine.Add(hash, Traits);
        hash = HashCombine.AddRange(hash, TypeParameterNames);
        return hash;
    }
}
