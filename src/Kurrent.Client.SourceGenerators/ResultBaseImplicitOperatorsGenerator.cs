using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Kurrent.Client.SourceGenerators;

[Generator]
public class ResultBaseImplicitOperatorsGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        #if DEBUG
        if (!Debugger.IsAttached) {
            // Debugger.Launch();
        }
        #endif

        // Filter for record types that might inherit from ResultBase
        var candidateTypes = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsRecordDeclaration(node),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(candidateTypes, static (spc, typeSymbol) => {
            if (typeSymbol is null) return;

            try {
                GenerateImplicitOperators(spc, typeSymbol);
            }
            catch (Exception ex) {
                ReportDiagnostic(spc, Failed, ex.ToString());
            }
        });
    }

    static bool IsRecordDeclaration(SyntaxNode node) {
        if (node is not RecordDeclarationSyntax)
            return false;

        return true;
    }

    static INamedTypeSymbol? GetSemanticTargetForGeneration(GeneratorSyntaxContext context) {
        var recordDecl = (RecordDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(recordDecl);

        if (symbol is not { } typeSymbol)
            return null;

        // Check if it implements IResultBase<TValue, TError>
        if (!ImplementsIResultBase(typeSymbol))
            return null;

        return typeSymbol;
    }

    static bool ImplementsIResultBase(INamedTypeSymbol typeSymbol) {
        foreach (var iface in typeSymbol.AllInterfaces) {
            var ifaceName = iface.OriginalDefinition.ToDisplayString();

            if (ifaceName == "Kurrent.IResultBase<TValue, TError>")
                return true;
        }

        return false;
    }

    static void GenerateImplicitOperators(SourceProductionContext context, INamedTypeSymbol typeSymbol) {
        // Extract TValue and TError from IResultBase<TValue, TError>
        var interfaceType = typeSymbol.AllInterfaces.Where( x => {
            var ifaceName = x.OriginalDefinition.ToDisplayString();
            return ifaceName == "Kurrent.IResultBase<TValue, TError>";
        }).First();

        var tValue = interfaceType.TypeArguments[0];
        var tError = interfaceType.TypeArguments[1];

        // Extract exception variants from TError if it implements IVariantException<...>
        var exceptionVariants = ExtractVariantExceptionTypes(tError);

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();

        var source = GenerateSource(typeSymbol, tValue, tError, exceptionVariants, ns);
        var hintName = $"{typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "").Replace(".", "_").Replace("<", "_").Replace(">", "_")}.ImplicitOperators.g.cs";

        context.AddSource(hintName, SourceText.From(source, Encoding.UTF8));

        ReportDiagnostic(context, ImplementationGenerated, typeSymbol.ToDisplayString());
    }

    static List<ITypeSymbol> ExtractVariantExceptionTypes(ITypeSymbol tError) {
        var variants = new List<ITypeSymbol>();

        // Look for IVariantException<T1, T2, ...> interface
        foreach (var iface in tError.AllInterfaces) {
            var ifaceName = iface.OriginalDefinition.ToDisplayString();

            // Check if it's IVariantException with generic arguments
            if (ifaceName.StartsWith("Kurrent.IVariantException<") || ifaceName.StartsWith("Kurrent.Variant.IVariantException<")) {
                // Extract all type arguments (the exception types)
                variants.AddRange(iface.TypeArguments);
                break;
            }
        }

        return variants;
    }

    static string GenerateSource(INamedTypeSymbol typeSymbol, ITypeSymbol tValue, ITypeSymbol tError, List<ITypeSymbol> exceptionVariants, string ns) {
        var typeName = typeSymbol.Name;
        var tValueName = tValue.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var tErrorName = tError.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Generate exception operators
        var exceptionOperatorsBuilder = new StringBuilder();
        foreach (var exceptionType in exceptionVariants) {
            var exceptionTypeName = exceptionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var exceptionSimpleName = exceptionType.Name;

            const string exceptionOperatorTemplate = """
                    /// <summary>
                    /// Implicitly converts {EXCEPTION_SIMPLE_NAME} to {TYPE_NAME}.
                    /// </summary>
                    public static implicit operator {TYPE_NAME}({EXCEPTION_TYPE_NAME} ex) => new(ex);

                    /// <summary>
                    /// Implicitly converts {TYPE_NAME} to {EXCEPTION_SIMPLE_NAME}.
                    /// </summary>
                    public static implicit operator {EXCEPTION_TYPE_NAME}({TYPE_NAME} result) => result.Error.As{EXCEPTION_SIMPLE_NAME};

                """;

            var operatorCode = exceptionOperatorTemplate
                .Replace("{TYPE_NAME}", typeName)
                .Replace("{EXCEPTION_TYPE_NAME}", exceptionTypeName)
                .Replace("{EXCEPTION_SIMPLE_NAME}", exceptionSimpleName)
                .Replace("{ERROR_TYPE_NAME}", tErrorName);

            exceptionOperatorsBuilder.Append(operatorCode);
        }

        const string template = """
            // <auto-generated />
            // This file was generated by ResultBaseImplicitOperatorsGenerator
            // Changes to this file may be lost when the file is regenerated.

            #nullable enable
            
            {NAMESPACE_DECLARATION}

            [PublicAPI]
            partial record {TYPE_NAME} : Result<{VALUE_TYPE_NAME}, {ERROR_TYPE_NAME}> {
                /// <summary>
                /// Initializes a new instance with a success value.
                /// </summary>
                {TYPE_NAME}({VALUE_TYPE_NAME} value) : base(true, value, default) { }

                /// <summary>
                /// Initializes a new instance with an error.
                /// </summary>
                {TYPE_NAME}({ERROR_TYPE_NAME} error) : base(false, default, error) { }
                
                /// <summary>
                /// Creates a new success result.
                /// </summary>
                public static new {TYPE_NAME} Success({VALUE_TYPE_NAME} value) => new(value);
            
                /// <summary>
                /// Creates a new failure result.
                /// </summary>
                public static new {TYPE_NAME} Failure({ERROR_TYPE_NAME} error) => new(error);

                /// <summary>
                /// Implicitly converts a success value to {TYPE_NAME}.
                /// </summary>
                public static implicit operator {TYPE_NAME}({VALUE_TYPE_NAME} value) => new(value);

                /// <summary>
                /// Implicitly converts an error to {TYPE_NAME}.
                /// </summary>
                public static implicit operator {TYPE_NAME}({ERROR_TYPE_NAME} error) => new(error);

                {EXCEPTION_OPERATORS}
            }
            """;

        var namespaceDecl = !string.IsNullOrEmpty(ns) ? $"namespace {ns};" : "// Global namespace";

        return template
            .Replace("{NAMESPACE_DECLARATION}", namespaceDecl)
            .Replace("{TYPE_NAME}", typeName)
            .Replace("{VALUE_TYPE_NAME}", tValueName)
            .Replace("{ERROR_TYPE_NAME}", tErrorName)
            .Replace("{EXCEPTION_OPERATORS}", exceptionOperatorsBuilder.ToString());
    }

    #region . Diagnostics .

    static readonly DiagnosticDescriptor ImplementationGenerated = new(
        "RESULT001",
        "ResultBase Implicit Operators",
        "Generated implicit operators for: {0}",
        "CodeGeneration",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor Failed = new(
        "RESULT500",
        "ResultBase Implicit Operators",
        "Failed to generate implicit operators: {0}",
        "Error",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    static void ReportDiagnostic(SourceProductionContext context, DiagnosticDescriptor descriptor, params object?[]? messageArgs) {
        var diagnostic = Diagnostic.Create(descriptor, Location.None, messageArgs);
        context.ReportDiagnostic(diagnostic);
    }

    #endregion
}
