using Microsoft.CodeAnalysis;
using Kurrent.CodeAnalysis.Matching;

namespace Kurrent.CodeAnalysis;

/// <summary>
/// Value-equatable snapshot of a Roslyn <see cref="ITypeSymbol"/>, extended with semantic query
/// helpers. Flows through incremental generator pipelines safely — equality compares only the
/// value fields, so cache-sensitive downstream steps don't invalidate across compilations.
/// </summary>
/// <remarks>
/// <see cref="OriginalSymbol"/> is populated with the live <see cref="ITypeSymbol"/> for the
/// current compilation. Use it inside the <c>Select</c> lambda for edge cases not covered by
/// the value model (e.g. member walks, location reporting). Do NOT hold <see cref="OriginalSymbol"/>
/// past the current compilation boundary — Roslyn disposes compilations, and a cached
/// <see cref="TypeSymbol"/> carried from a previous compilation will have a stale symbol.
/// </remarks>
public sealed record TypeSymbol(
    string               Name,
    string               Namespace,
    string               FullName,
    TypeKind             Kind,
    TypeTraits           Traits,
    SpecialType          Special,
    TypeSymbol[]         TypeArguments,
    TypeSymbol?          BaseType,
    TypeSymbol?          ArrayElementType,
    TypeSymbol[]         Interfaces,
    AttributeRef[]       Attributes,
    ContainingTypeRef[]  ContainingTypes) {

    public required ITypeSymbol OriginalSymbol { get; init; }

    #region ->> Trait sugar <<-

    public bool IsPartial       => Traits.HasFlag(TypeTraits.Partial);
    public bool IsAbstract      => Traits.HasFlag(TypeTraits.Abstract);
    public bool IsSealed        => Traits.HasFlag(TypeTraits.Sealed);
    public bool IsStatic        => Traits.HasFlag(TypeTraits.Static);
    public bool IsReadOnly      => Traits.HasFlag(TypeTraits.ReadOnly);
    public bool IsPublic        => Traits.HasFlag(TypeTraits.Public);
    public bool IsInternal      => Traits.HasFlag(TypeTraits.Internal);
    public bool IsFile          => Traits.HasFlag(TypeTraits.File);

    public bool IsValueType     => Kind is TypeKind.Struct or TypeKind.RecordStruct or TypeKind.Enum;
    public bool IsReferenceType => Kind is TypeKind.Class or TypeKind.Record or TypeKind.Interface or TypeKind.Delegate;
    public bool IsGeneric       => TypeArguments.Length > 0;
    public bool IsArray         => Kind == TypeKind.Array;
    public bool IsNullableOfT   => Special == SpecialType.System_Nullable_T && TypeArguments.Length == 1;

    public bool IsOpenGeneric {
        get {
            foreach (var a in TypeArguments)
                if (a.Kind == TypeKind.TypeParameter) return true;
            return false;
        }
    }

    public bool Is(TypeTraits traits) => (Traits & traits) == traits;

    #endregion

    #region ->> Semantic queries <<-

    public bool Implements(string pattern) {
        var p = TypePattern.Parse(pattern);
        foreach (var iface in Interfaces)
            if (p.Matches(iface)) return true;
        return false;
    }

    public TypeSymbol? Interface(string pattern) {
        var p = TypePattern.Parse(pattern);
        foreach (var iface in Interfaces)
            if (p.Matches(iface)) return iface;
        return null;
    }

    public bool DerivesFrom(string pattern) {
        var p = TypePattern.Parse(pattern);
        return BaseType is not null && p.Matches(BaseType);
    }

    public bool HasAttribute(string pattern) {
        var p = TypePattern.Parse(pattern);
        foreach (var a in Attributes)
            if (p.Matches(a.Type)) return true;
        return false;
    }

    public AttributeRef? Attribute(string pattern) {
        var p = TypePattern.Parse(pattern);
        foreach (var a in Attributes)
            if (p.Matches(a.Type)) return a;
        return null;
    }

    #endregion

    #region ->> Manual Equals + GetHashCode (excludes OriginalSymbol) <<-

    public bool Equals(TypeSymbol? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Name      == other.Name
            && Namespace == other.Namespace
            && FullName  == other.FullName
            && Kind      == other.Kind
            && Traits    == other.Traits
            && Special   == other.Special
            && Equals(BaseType, other.BaseType)
            && Equals(ArrayElementType, other.ArrayElementType)
            && TypeArguments.AsSpan().SequenceEqual(other.TypeArguments.AsSpan())
            && Interfaces.AsSpan().SequenceEqual(other.Interfaces.AsSpan())
            && Attributes.AsSpan().SequenceEqual(other.Attributes.AsSpan())
            && ContainingTypes.AsSpan().SequenceEqual(other.ContainingTypes.AsSpan());
    }

    public override int GetHashCode() =>
        HashCode.New
            .Combine(Name)
            .Combine(Namespace)
            .Combine(FullName)
            .Combine(Kind)
            .Combine(Traits)
            .Combine(Special)
            .Combine(BaseType)
            .Combine(ArrayElementType)
            .Combine(TypeArguments)
            .Combine(Interfaces)
            .Combine(Attributes)
            .Combine(ContainingTypes);

    #endregion
}
