using Microsoft.CodeAnalysis;

namespace Kurrent.CodeAnalysis.Pipeline;

public static class KurrentGenerationExtensions {
    /// <summary>
    /// Starts a type-selection pipeline. The configure lambda picks which types to extract;
    /// the library picks the optimal Roslyn primitive under the hood
    /// (<c>ForAttributeWithMetadataName</c> when <see cref="TypeSelector.WithAttribute"/> is
    /// called, otherwise <c>CreateSyntaxProvider</c>).
    /// </summary>
    public static TypePipeline<TypeSymbol> Types(this IncrementalGeneratorInitializationContext context, Action<TypeSelector> configure) {
        var selector = new TypeSelector();
        configure(selector);
        return selector.Build(context);
    }
}