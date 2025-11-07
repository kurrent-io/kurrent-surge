using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Kurrent.Client.SourceGenerators;

[Generator]
public class ValueObjectGenerator : ISourceGenerator {
    const string ValueObjectAttributeKey = "Kurrent.ValueObjectAttribute";

    class ValueObjectSyntaxReceiver : ISyntaxReceiver {
        public List<TypeDeclarationSyntax> CandidateTypes { get; } = [];

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode) {
            // Look for partial classes with attributes
            if (syntaxNode is not TypeDeclarationSyntax typeDeclaration ||
                !typeDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)) ||
                typeDeclaration.AttributeLists.Count <= 0) return;

            // Only classes
            if (typeDeclaration is ClassDeclarationSyntax or RecordDeclarationSyntax)
                CandidateTypes.Add(typeDeclaration);

            // // Look for partial record structs with attributes
            // if (syntaxNode is not TypeDeclarationSyntax { } typeDeclaration ||
            //     !typeDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)) ||
            //     !typeDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.RecordKeyword)) ||
            //     typeDeclaration.AttributeLists.Count <= 0) return;
            //
            // // Only record structs
            // if (typeDeclaration is RecordDeclarationSyntax)
            //     CandidateTypes.Add(typeDeclaration);
        }
    }

    public void Initialize(GeneratorInitializationContext context) {
        #if DEBUG
        if (!Debugger.IsAttached) Debugger.Launch();
        #endif

        context.RegisterForSyntaxNotifications(() => new ValueObjectSyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context) {
        try {
            var compilation = context.Compilation;

            // Get the syntax receiver
            if (context.SyntaxReceiver is not ValueObjectSyntaxReceiver receiver)
                return;

            // Debug: Report that we're starting execution
            ReportDiagnostic(context, Starting);

            // Process candidate types from the syntax receiver
            foreach (var typeDeclaration in receiver.CandidateTypes) {
                var semanticModel = compilation.GetSemanticModel(typeDeclaration.SyntaxTree);

                if (semanticModel.GetDeclaredSymbol(typeDeclaration) is not { } typeSymbol)
                    continue;

                var (hasAttribute, valueType) = HasValueObjectAttribute(typeSymbol, context);
                if (!hasAttribute || valueType is null)
                    continue;

                ReportDiagnostic(context, FoundTarget, typeSymbol, valueType);

                GenerateAndAddSource(typeSymbol, valueType);

                ReportDiagnostic(context, ImplementationGenerated, typeSymbol);
            }
        }
        catch (Exception ex) {
            ReportDiagnostic(context, Failed, ex.ToString());
        }

        return;

        void GenerateAndAddSource(INamedTypeSymbol typeSymbol, ITypeSymbol valueType) {
            var source = GenerateImplementation(typeSymbol, valueType, context);

            var hintName = SourceGeneratorFileNaming.GetHintName(
                typeSymbol,
                new FileNamingOptions {
                    Prefix                 = "ValueObjects",
                    UseFoldersForPrefix    = true,
                    UseFoldersForNamespace = true,
                    LowercasePath          = false
                },
                [valueType]
            );

            context.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
        }
    }

    #region . Diagnostic Helpers .

    static readonly DiagnosticDescriptor Starting = new(
        "KURRENT_VO_000",
        nameof(ValueObjectGenerator),
        $"[{nameof(ValueObjectGenerator)}] starting",
        "CodeGeneration",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor FoundTarget = new(
        "KURRENT_VO_202",
        nameof(ValueObjectGenerator),
        $"[{nameof(ValueObjectGenerator)}] found type: {{0}} with value type: {{1}}",
        "CodeGeneration",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor Failed = new(
        "KURRENT_VO_500",
        nameof(ValueObjectGenerator),
        $"[{nameof(ValueObjectGenerator)}] failed: {{0}}",
        "Error",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor ImplementationGenerated = new(
        "KURRENT_VO_200",
        nameof(ValueObjectGenerator),
        $"[{nameof(ValueObjectGenerator)}] completed code generation for: {{0}}",
        "CodeGeneration",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static void ReportDiagnostic(GeneratorExecutionContext context, DiagnosticDescriptor descriptor, params object?[]? messageArgs) {
        var diagnostic = Diagnostic.Create(descriptor, Location.None, messageArgs);
        context.ReportDiagnostic(diagnostic);
    }

    #endregion

    static (bool hasAttribute, ITypeSymbol? valueType) HasValueObjectAttribute(INamedTypeSymbol typeSymbol, GeneratorExecutionContext context) {
        var attr = typeSymbol.GetAttributes().Where(attr => {
            if (attr.AttributeClass is not { } classInfo || !classInfo.ToDisplayString().StartsWith(ValueObjectAttributeKey))
                return false;

            ReportDiagnostic(context, FoundTarget, classInfo.ToDisplayString());
            return true;
        }).FirstOrDefault();

        if (attr is null)
            return (false, null);

        // Extract TValue from ValueObjectAttribute<TValue>
        var valueType = attr.AttributeClass?.TypeArguments.FirstOrDefault();
        return (true, valueType);
    }

    // static (bool hasAttribute, ITypeSymbol? valueType) HasValueObjectAttribute(INamedTypeSymbol typeSymbol, Compilation compilation) {
    //     var valueObjectAttributeSymbol = compilation.GetTypeByMetadataName($"{ValueObjectAttributeKey}`1");
    //     if (valueObjectAttributeSymbol is null)
    //         return (false, null);
    //
    //     var attribute = typeSymbol.GetAttributes()
    //         .FirstOrDefault(a =>
    //             a.AttributeClass is { IsGenericType: true } attrClass &&
    //             attrClass.ConstructedFrom.Equals(valueObjectAttributeSymbol, SymbolEqualityComparer.Default));
    //
    //     if (attribute is null)
    //         return (false, null);
    //
    //     // Extract TValue from ValueObjectAttribute<TValue>
    //     var valueType = attribute.AttributeClass?.TypeArguments.FirstOrDefault();
    //     return (true, valueType);
    // }

    static string GenerateImplementation(INamedTypeSymbol typeSymbol, ITypeSymbol valueType, GeneratorExecutionContext context) {

        var (typeName, ns) = GetTypeNameAndNamespace(typeSymbol);
        var valueTypeName = valueType.ToDisplayString();

        var sourceBuilder = new StringBuilder(
            """
            // <auto-generated />
            // Generated by ValueObjectGenerator
            // DO NOT MODIFY - Changes will be overwritten

            #nullable enable
            
            using System;
            using System.CodeDom.Compiler;
            using System.Runtime.CompilerServices;
            using JetBrains.Annotations;
            using Kurrent.Validation;

            {NAMESPACE_DECLARATION}

            {TYPE_DECLARATION}
            """
        );

        sourceBuilder
            .Replace("{NAMESPACE_DECLARATION}", GenerateNamespaceDeclaration(ns))
            .Replace("{TYPE_DECLARATION}", GenerateTypeDeclaration(typeName, valueTypeName));

        return sourceBuilder.ToString();

        static string GenerateNamespaceDeclaration(string ns) => ns != string.Empty ? $"namespace {ns};" : "// Global namespace";

        static string GenerateTypeDeclaration(string typeName, string valueTypeName) {
            var template = """
                [System.Diagnostics.DebuggerDisplay("{GetDebugString()}")]
                [PublicAPI, CompilerGenerated, GeneratedCode("ValueObjectGenerator", "1.0.0")]
                readonly partial record struct {TYPE_NAME} {
                    /// <summary>
                    /// Optional validation hook. Assign in static constructor to enable validation.
                    /// </summary>
                    /// <remarks>
                    /// <para>If null, EnsureValid() returns this instance without validation.</para>
                    /// <para>If assigned, EnsureValid() invokes this delegate and unwraps the result.</para>
                    /// </remarks>
                    static System.Func<{VALUE_TYPE}, Kurrent.Validation.ValidationResult>? OnValidate { get; }

                    /// <summary>The wrapped primitive value.</summary>
                    public {VALUE_TYPE} Value { get; private init; }

                    /// <summary>
                    /// Validates the value without throwing exceptions.
                    /// </summary>
                    public Kurrent.Validation.ValidationResult Validate() => OnValidate?.Invoke(Value) ?? Results.Success;

                    /// <summary>
                    /// Ensures the value is valid according to OnValidate delegate (if assigned) or throws an exception.
                    /// </summary>
                    public {TYPE_NAME} EnsureValid() => Validate().Map(static (state, _) => state, this).Unwrap();

                    /// <summary>
                    /// Creates a {TYPE_NAME} that may be in an invalid state.
                    /// </summary>
                    /// <remarks>
                    /// To ensure the created value is valid, call EnsureValid() after creation.
                    /// </remarks>
                    public static {TYPE_NAME} From({VALUE_TYPE} value) => new() { Value = value };

                    /// <summary>
                    /// Creates a copy of another {TYPE_NAME}.
                    /// </summary>
                    public static {TYPE_NAME} From({TYPE_NAME} other) => new() { Value = other.Value };

                    /// <summary>Gets the display string representation.</summary>
                    string GetDisplayString() => Value.ToString() ?? "";

                    /// <summary>Gets the debug display string.</summary>
                    string GetDebugString() => $"{nameof({TYPE_NAME})}: {GetDisplayString()}";

                    /// <inheritdoc />
                    public override string ToString() => GetDisplayString();

                    /// <summary>Implicitly converts to {VALUE_TYPE}.</summary>
                    public static implicit operator {VALUE_TYPE}({TYPE_NAME} _) => _.Value;

                    /// <summary>Implicitly converts from {VALUE_TYPE}.</summary>
                    public static implicit operator {TYPE_NAME}({VALUE_TYPE} _) => From(_);
                }
                """;

            return template
                .Replace("{TYPE_NAME}", typeName)
                .Replace("{VALUE_TYPE}", valueTypeName);
        }

        static (string Name, string ContainingNamespace) GetTypeNameAndNamespace(INamedTypeSymbol typeSymbol) {
            var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : typeSymbol.ContainingNamespace.ToDisplayString();

            return (Name: typeSymbol.Name, ContainingNamespace: ns);
        }
    }
}
