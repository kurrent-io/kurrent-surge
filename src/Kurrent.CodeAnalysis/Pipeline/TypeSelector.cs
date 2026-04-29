using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kurrent.CodeAnalysis.Extraction;
using Kurrent.CodeAnalysis.Matching;

namespace Kurrent.CodeAnalysis.Pipeline;

/// <summary>
/// Fluent configuration for <see cref="KurrentGenerationExtensions.Types"/>. Mutable — each
/// method returns <c>this</c> after appending to the configuration. Multiple calls to the same
/// method compose: <c>OfKind(Record).OfKind(Class)</c> matches records OR classes,
/// <c>Is(Partial).Is(Public)</c> matches partial AND public.
/// </summary>
public sealed class TypeSelector {
    internal          string?                 AttributeFullName;
    internal          Func<SyntaxNode, bool>? SyntaxFilter;
    internal readonly List<TypeKind>          Kinds = new();

    internal TypeTraits RequiredTraits = TypeTraits.None;

    // Each entry is an OR-group of patterns from a single Implementing(params ...) call.
    // Across entries the semantics are AND — so two separate Implementing calls both have to match.
    internal readonly List<List<TypePattern>>      ImplementingPatternGroups = new();
    internal readonly List<Func<TypeSymbol, bool>> ImplementingPredicates    = new();
    internal readonly List<TypePattern>            DerivingFromPatterns      = new();
    internal readonly List<Func<TypeSymbol, bool>> DerivingFromPredicates    = new();
    internal readonly List<Func<TypeSymbol, bool>> MatchingPredicates        = new();

    public TypeSelector WithAttribute(string fullName) {
        AttributeFullName = NormalizeAttributeName(fullName);
        return this;
    }

    public TypeSelector WithSyntaxFilter(Func<SyntaxNode, bool> predicate) {
        SyntaxFilter = predicate;
        return this;
    }

    public TypeSelector OfKind(params TypeKind[] kinds) {
        Kinds.AddRange(kinds);
        return this;
    }

    public TypeSelector Is(TypeTraits traits) {
        RequiredTraits |= traits;
        return this;
    }

    public TypeSelector Implementing(params string[] patterns) {
        if (patterns is { Length: > 0 }) {
            var group = new List<TypePattern>(patterns.Length);

            foreach (var p in patterns)
                group.Add(TypePattern.Parse(p));

            ImplementingPatternGroups.Add(group);
        }

        return this;
    }

    public TypeSelector Implementing(Func<TypeSymbol, bool> predicate) {
        ImplementingPredicates.Add(predicate);
        return this;
    }

    public TypeSelector DerivingFrom(string pattern) {
        DerivingFromPatterns.Add(TypePattern.Parse(pattern));
        return this;
    }

    public TypeSelector DerivingFrom(Func<TypeSymbol, bool> predicate) {
        DerivingFromPredicates.Add(predicate);
        return this;
    }

    public TypeSelector Matching(Func<TypeSymbol, bool> predicate) {
        MatchingPredicates.Add(predicate);
        return this;
    }

    public bool Evaluate(TypeSymbol info) {
        if (Kinds.Count > 0 && !Kinds.Contains(info.Kind))
            return false;

        if (RequiredTraits != TypeTraits.None && (info.Traits & RequiredTraits) != RequiredTraits)
            return false;

        foreach (var group in ImplementingPatternGroups) {
            var matchedAny = false;

            foreach (var pattern in group) {
                foreach (var iface in info.Interfaces)
                    if (pattern.Matches(iface)) {
                        matchedAny = true;
                        break;
                    }

                if (matchedAny)
                    break;
            }

            if (!matchedAny)
                return false;
        }

        foreach (var predicate in ImplementingPredicates) {
            var matched = false;

            foreach (var iface in info.Interfaces)
                if (predicate(iface)) {
                    matched = true;
                    break;
                }

            if (!matched)
                return false;
        }

        if (DerivingFromPatterns.Count > 0) {
            if (info.BaseType is null)
                return false;

            foreach (var pattern in DerivingFromPatterns)
                if (!pattern.Matches(info.BaseType))
                    return false;
        }

        foreach (var predicate in DerivingFromPredicates) {
            if (info.BaseType is null || !predicate(info.BaseType))
                return false;
        }

        foreach (var predicate in MatchingPredicates)
            if (!predicate(info))
                return false;

        return true;
    }

    static string NormalizeAttributeName(string name) {
        name = name.Trim();
        return name.EndsWith("Attribute", StringComparison.Ordinal) ? name : name + "Attribute";
    }

    internal TypePipeline<TypeSymbol> Build(IncrementalGeneratorInitializationContext context) {
        var selfCopy         = this;
        var userSyntaxFilter = SyntaxFilter;

        IncrementalValuesProvider<TypeSymbol?> provider;

        if (AttributeFullName is not null) {
            provider = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    fullyQualifiedMetadataName: AttributeFullName,
                    predicate: (node, _) => IsTypeDeclaration(node) && (userSyntaxFilter?.Invoke(node) ?? true),
                    transform: static (ctx, ct) => {
                        ct.ThrowIfCancellationRequested();
                        var symbol = ctx.TargetSymbol as ITypeSymbol;

                        if (symbol is null)
                            return null;

                        if (symbol.IsCompilerGenerated())
                            return null;

                        return symbol.ToTypeSymbol();
                    });
        }
        else {
            provider = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (node, _) => IsTypeDeclaration(node) && (userSyntaxFilter?.Invoke(node) ?? true),
                    transform: static (ctx, ct) => {
                        ct.ThrowIfCancellationRequested();

                        if (ctx.Node is not BaseTypeDeclarationSyntax decl)
                            return null;

                        var symbol = ctx.SemanticModel.GetDeclaredSymbol(decl, ct) as ITypeSymbol;

                        if (symbol is null)
                            return null;

                        if (symbol.IsCompilerGenerated())
                            return null;

                        return symbol.ToTypeSymbol();
                    });
        }

        var filtered = provider
            .Where(static t => t is not null)
            .Select(static (t, _) => t!)
            .Where(selfCopy.Evaluate);

        if (AttributeFullName is null)
            filtered = DedupeByFullName(filtered);

        return new TypePipeline<TypeSymbol>(context, filtered);
    }

    static bool IsTypeDeclaration(SyntaxNode node) =>
        node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax;

    static IncrementalValuesProvider<TypeSymbol> DedupeByFullName(IncrementalValuesProvider<TypeSymbol> provider) {
        return provider
            .Collect()
            .SelectMany(static (arr, _) => {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var list = new List<TypeSymbol>(arr.Length);

                foreach (var info in arr) {
                    if (seen.Add(info.FullName))
                        list.Add(info);
                }

                return list;
            });
    }
}