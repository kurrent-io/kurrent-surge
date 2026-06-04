using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kurrent.CodeAnalysis.Extraction;

static class SymbolExtensions {
    public static string GetFullName(this ITypeSymbol symbol) {
        if (symbol is IArrayTypeSymbol arr)
            return arr.ElementType.GetFullName() + "[]";

        if (symbol is IPointerTypeSymbol ptr)
            return ptr.PointedAtType.GetFullName() + "*";

        if (symbol is ITypeParameterSymbol tp)
            return tp.Name;

        if (symbol.ContainingType is { } outer)
            return outer.GetFullName() + "." + symbol.Name;

        if (symbol.ContainingNamespace is { IsGlobalNamespace: false } ns)
            return ns.ToDisplayString() + "." + symbol.Name;

        return symbol.Name;
    }

    public static string GetNamespace(this ITypeSymbol symbol) {
        var current = symbol;

        while (current.ContainingType is { } outer)
            current = outer;

        return current.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : "";
    }

    public static TypeKind GetTypeKind(this ITypeSymbol symbol) {
        if (symbol is IArrayTypeSymbol)
            return TypeKind.Array;

        if (symbol is IPointerTypeSymbol)
            return TypeKind.Pointer;

        if (symbol is IDynamicTypeSymbol)
            return TypeKind.Dynamic;

        if (symbol is ITypeParameterSymbol)
            return TypeKind.TypeParameter;

        if (symbol is IErrorTypeSymbol)
            return TypeKind.Error;

        return symbol.TypeKind switch {
            Microsoft.CodeAnalysis.TypeKind.Class when symbol.IsRecord  => TypeKind.Record,
            Microsoft.CodeAnalysis.TypeKind.Class                       => TypeKind.Class,
            Microsoft.CodeAnalysis.TypeKind.Struct when symbol.IsRecord => TypeKind.RecordStruct,
            Microsoft.CodeAnalysis.TypeKind.Struct                      => TypeKind.Struct,
            Microsoft.CodeAnalysis.TypeKind.Interface                   => TypeKind.Interface,
            Microsoft.CodeAnalysis.TypeKind.Enum                        => TypeKind.Enum,
            Microsoft.CodeAnalysis.TypeKind.Delegate                    => TypeKind.Delegate,
            _                                                           => TypeKind.Error
        };
    }

    public static TypeTraits GetTraits(this ITypeSymbol symbol) {
        var traits = TypeTraits.None;

        traits |= symbol.DeclaredAccessibility switch {
            Accessibility.Public               => TypeTraits.Public,
            Accessibility.Internal             => TypeTraits.Internal,
            Accessibility.Protected            => TypeTraits.Protected,
            Accessibility.Private              => TypeTraits.Private,
            Accessibility.ProtectedOrInternal  => TypeTraits.Protected | TypeTraits.Internal,
            Accessibility.ProtectedAndInternal => TypeTraits.Protected | TypeTraits.Internal,
            _                                  => TypeTraits.None
        };

        if (symbol.IsStatic)
            traits |= TypeTraits.Static;

        if (symbol.IsAbstract && symbol.TypeKind != Microsoft.CodeAnalysis.TypeKind.Interface)
            traits |= TypeTraits.Abstract;

        if (symbol.IsSealed && !symbol.IsStatic)
            traits |= TypeTraits.Sealed;

        if (symbol is INamedTypeSymbol nt && nt.IsReadOnly)
            traits |= TypeTraits.ReadOnly;

        if (IsPartial(symbol))
            traits |= TypeTraits.Partial;

        if (IsFileLocal(symbol))
            traits |= TypeTraits.File;

        return traits;
    }

    static bool IsPartial(ITypeSymbol symbol) {
        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences) {
            if (syntaxRef.GetSyntax() is TypeDeclarationSyntax decl) {
                foreach (var mod in decl.Modifiers) {
                    if (mod.IsKind(SyntaxKind.PartialKeyword))
                        return true;
                }
            }
        }

        return false;
    }

    static bool IsFileLocal(ITypeSymbol symbol) {
        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences) {
            if (syntaxRef.GetSyntax() is TypeDeclarationSyntax decl) {
                foreach (var mod in decl.Modifiers) {
                    if (mod.IsKind(SyntaxKind.FileKeyword))
                        return true;
                }
            }
        }

        return false;
    }

    public static bool IsCompilerGenerated(this ITypeSymbol symbol) =>
        symbol.IsImplicitlyDeclared || symbol.Name.IndexOf('<') >= 0;

    static readonly HashSet<string> IgnoredBaseTypes = new(StringComparer.Ordinal) {
        "System.Object",
        "System.ValueType",
        "System.Enum",
        "System.Delegate",
        "System.MulticastDelegate"
    };

    public static bool IsIgnoredBaseType(this ITypeSymbol symbol) =>
        IgnoredBaseTypes.Contains(symbol.GetFullName());
}