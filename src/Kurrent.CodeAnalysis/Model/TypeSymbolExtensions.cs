using Microsoft.CodeAnalysis;

namespace Kurrent.CodeAnalysis.Extraction;

/// <summary>
/// Primary extension entry point for converting Roslyn <see cref="ITypeSymbol"/> values into
/// value-equatable <see cref="TypeSymbol"/> snapshots. Fully recursive with cycle detection —
/// every reached type is extracted with its interfaces, base type, and type arguments populated,
/// except that re-entrant symbols short-circuit to a minimal <c>Leaf</c> (name + kind only).
/// </summary>
public static class TypeSymbolExtensions {
    /// <summary>
    /// Produce a value-equatable <see cref="TypeSymbol"/> snapshot of <paramref name="symbol"/>.
    /// </summary>
    public static TypeSymbol ToTypeSymbol(this ITypeSymbol symbol) =>
        ExtractCore(symbol, new HashSet<ISymbol>(SymbolEqualityComparer.Default));

    static TypeSymbol ExtractCore(ITypeSymbol symbol, HashSet<ISymbol> visited) {
        if (!visited.Add(symbol))
            return Leaf(symbol);

        try {
            return new TypeSymbol(
                Name: symbol.Name,
                Namespace: symbol.GetNamespace(),
                FullName: symbol.GetFullName(),
                Kind: symbol.GetTypeKind(),
                Traits: symbol.GetTraits(),
                Accessibility: symbol.DeclaredAccessibility,
                Special: GetSpecial(symbol),
                TypeArguments: ExtractTypeArguments(symbol, visited),
                BaseType: ExtractBaseType(symbol, visited),
                ArrayElementType: ExtractArrayElementType(symbol, visited),
                Interfaces: ExtractInterfaces(symbol, visited),
                Attributes: ExtractAttributes(symbol, visited),
                ContainingTypes: ExtractContainingTypes(symbol));
        }
        finally {
            visited.Remove(symbol);
        }
    }

    static TypeSymbol Leaf(ITypeSymbol symbol) =>
        new(
            Name: symbol.Name,
            Namespace: symbol.GetNamespace(),
            FullName: symbol.GetFullName(),
            Kind: symbol.GetTypeKind(),
            Traits: symbol.GetTraits(),
            Accessibility: symbol.DeclaredAccessibility,
            Special: GetSpecial(symbol),
            TypeArguments: Array.Empty<TypeSymbol>(),
            BaseType: null,
            ArrayElementType: null,
            Interfaces: Array.Empty<TypeSymbol>(),
            Attributes: Array.Empty<AttributeRef>(),
            ContainingTypes: Array.Empty<ContainingTypeRef>());

    static SpecialType GetSpecial(ITypeSymbol symbol) =>
        symbol is INamedTypeSymbol nt && nt.IsGenericType
            ? nt.OriginalDefinition.SpecialType
            : symbol.SpecialType;

    static TypeSymbol? ExtractArrayElementType(ITypeSymbol symbol, HashSet<ISymbol> visited) =>
        symbol is IArrayTypeSymbol arr ? ExtractCore(arr.ElementType, visited) : null;

    static TypeSymbol[] ExtractTypeArguments(ITypeSymbol symbol, HashSet<ISymbol> visited) {
        if (symbol is not INamedTypeSymbol named || named.TypeArguments.Length == 0)
            return Array.Empty<TypeSymbol>();

        var args = new TypeSymbol[named.TypeArguments.Length];

        for (var i = 0; i < named.TypeArguments.Length; i++)
            args[i] = ExtractCore(named.TypeArguments[i], visited);

        return args;
    }

    static TypeSymbol? ExtractBaseType(ITypeSymbol symbol, HashSet<ISymbol> visited) {
        if (symbol.BaseType is not { } baseType)
            return null;

        if (baseType.IsIgnoredBaseType())
            return null;

        return ExtractCore(baseType, visited);
    }

    static TypeSymbol[] ExtractInterfaces(ITypeSymbol symbol, HashSet<ISymbol> visited) {
        if (symbol.AllInterfaces.Length == 0)
            return Array.Empty<TypeSymbol>();

        var list = new List<TypeSymbol>(symbol.AllInterfaces.Length);

        foreach (var iface in symbol.AllInterfaces)
            list.Add(ExtractCore(iface, visited));

        list.Sort(static (a, b) => string.CompareOrdinal(a.FullName, b.FullName));

        return list.ToArray();
    }

    static AttributeRef[] ExtractAttributes(ITypeSymbol symbol, HashSet<ISymbol> visited) {
        var attrs = symbol.GetAttributes();

        if (attrs.Length == 0)
            return Array.Empty<AttributeRef>();

        var result = new List<AttributeRef>(attrs.Length);

        foreach (var a in attrs) {
            if (a.AttributeClass is null)
                continue;

            var ctorArgs = new ConstantValue[a.ConstructorArguments.Length];

            for (var i = 0; i < a.ConstructorArguments.Length; i++)
                ctorArgs[i] = ConvertConstant(a.ConstructorArguments[i]);

            var namedArgs = new NamedArg[a.NamedArguments.Length];

            for (var i = 0; i < a.NamedArguments.Length; i++)
                namedArgs[i] = new NamedArg(a.NamedArguments[i].Key, ConvertConstant(a.NamedArguments[i].Value));

            result.Add(
                new AttributeRef(
                    Type: Leaf(a.AttributeClass),
                    ConstructorArgs: ctorArgs,
                    NamedArgs: namedArgs));
        }

        return result.ToArray();
    }

    static ContainingTypeRef[] ExtractContainingTypes(ITypeSymbol symbol) {
        var chain   = new List<INamedTypeSymbol>();
        var current = symbol.ContainingType;

        while (current is not null) {
            chain.Add(current);
            current = current.ContainingType;
        }

        if (chain.Count == 0)
            return Array.Empty<ContainingTypeRef>();

        chain.Reverse();

        var result = new ContainingTypeRef[chain.Count];

        for (var i = 0; i < chain.Count; i++) {
            var c      = chain[i];
            var traits = c.GetTraits() | TypeTraits.Partial;

            var tpNames = c.TypeParameters.Length == 0
                ? Array.Empty<string>()
                : c.TypeParameters.Select(tp => tp.Name).ToArray();

            result[i] = new ContainingTypeRef(
                Name: c.Name,
                Kind: c.GetTypeKind(),
                Traits: traits,
                TypeParameterNames: tpNames);
        }

        return result;
    }

    static ConstantValue ConvertConstant(TypedConstant tc) {
        var typeName = tc.Type?.GetFullName() ?? "System.Object";

        if (tc.IsNull)
            return new ConstantValue.Null(typeName);

        return tc.Kind switch {
            TypedConstantKind.Primitive => new ConstantValue.Primitive(tc.Value, typeName),
            TypedConstantKind.Type => new ConstantValue.TypeOf(
                ReferencedTypeFullName: tc.Value is ITypeSymbol ts ? ts.GetFullName() : "",
                TypeFullName: typeName),
            TypedConstantKind.Enum => new ConstantValue.Enum(
                UnderlyingValue: Convert.ToInt64(tc.Value),
                MemberName: null,
                TypeFullName: typeName),
            TypedConstantKind.Array => BuildArray(tc, typeName),
            _                       => new ConstantValue.Primitive(tc.Value, typeName)
        };
    }

    static ConstantValue BuildArray(TypedConstant tc, string typeName) {
        if (tc.Values.IsDefaultOrEmpty)
            return new ConstantValue.Array(Array.Empty<ConstantValue>(), typeName);

        var values = new ConstantValue[tc.Values.Length];

        for (var i = 0; i < tc.Values.Length; i++)
            values[i] = ConvertConstant(tc.Values[i]);

        return new ConstantValue.Array(values, typeName);
    }
}