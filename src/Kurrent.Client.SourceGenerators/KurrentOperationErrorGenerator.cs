
// ReSharper disable UnusedParameter.Local
// ReSharper disable UnusedMember.Local

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Kurrent.Client.SourceGenerators;

[Generator]
[SuppressMessage("MicrosoftCodeAnalysisReleaseTracking", "RS2008:Enable analyzer release tracking")]
public class KurrentOperationErrorGenerator : ISourceGenerator {
    const string KurrentOperationErrorAttributeKey = "Kurrent.Client.KurrentOperationErrorAttribute";

    class KurrentOperationErrorSyntaxReceiver : ISyntaxReceiver {
        public List<TypeDeclarationSyntax> CandidateTypes { get; } = [];

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode) {
            // Look for partial classes with attributes
            if (syntaxNode is not TypeDeclarationSyntax typeDeclaration ||
                !typeDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)) ||
                typeDeclaration.AttributeLists.Count <= 0) return;

            // Only classes
            if (typeDeclaration is ClassDeclarationSyntax)
                CandidateTypes.Add(typeDeclaration);
        }
    }

    public void Initialize(GeneratorInitializationContext context) {
        #if DEBUG
        if (!Debugger.IsAttached) Debugger.Launch();
        #endif

        context.RegisterForSyntaxNotifications(() => new KurrentOperationErrorSyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context) {
        try {
            var compilation = context.Compilation;

            // Get the syntax receiver
            if (context.SyntaxReceiver is not KurrentOperationErrorSyntaxReceiver receiver)
                return;

            // Debug: Report that we're starting execution
            ReportDiagnostic(context, Starting);

            // Process candidate types from the syntax receiver
            foreach (var typeDeclaration in receiver.CandidateTypes) {
                var semanticModel = compilation.GetSemanticModel(typeDeclaration.SyntaxTree);

                if (semanticModel.GetDeclaredSymbol(typeDeclaration) is not { } typeSymbol)
                    continue;

                if (!HasKurrentOperationErrorAttribute(typeSymbol, context))
                    continue;

                ReportDiagnostic(context, FoundTarget, typeSymbol);

                GenerateAndAddSource(typeSymbol);

                ReportDiagnostic(context, ImplementationGenerated, typeSymbol);
            }
        }
        catch (Exception ex) {
            ReportDiagnostic(context, Failed, ex.ToString());
        }

        return;

        void GenerateAndAddSource(INamedTypeSymbol typeSymbol) {
            var source   = GenerateImplementation(typeSymbol, context);
            var hintName = GenerateHintName(typeSymbol);
            context.AddSource(hintName, source);
        }
    }

    #region ->> Diagnostic Helpers <<-

    static readonly DiagnosticDescriptor Starting = new(
        "KURRENT000",
        nameof(KurrentOperationErrorGenerator),
        $"[{nameof(KurrentOperationErrorGenerator)}] starting",
        "CodeGeneration",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor FoundAttribute = new(
        "KURRENT001",
        nameof(KurrentOperationErrorGenerator),
        $"[{nameof(KurrentOperationErrorGenerator)}] found attribute: {{0}}",
        "CodeGeneration",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor FoundErrorEnumCode = new(
        "KURRENT002",
        nameof(KurrentOperationErrorGenerator),
        $"[{nameof(KurrentOperationErrorGenerator)}] found error enum code: {{0}}",
        "CodeGeneration",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor FoundTarget = new(
        "KURRENT202",
        nameof(KurrentOperationErrorGenerator),
        $"[{nameof(KurrentOperationErrorGenerator)}] found type: {{0}}",
        "CodeGeneration",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor Failed = new(
        "KURRENT500",
        nameof(KurrentOperationErrorGenerator),
        $"[{nameof(KurrentOperationErrorGenerator)}] failed: {{0}}",
        "Error",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor ImplementationGenerated = new(
        "KURRENT200",
        nameof(KurrentOperationErrorGenerator),
        $"[{nameof(KurrentOperationErrorGenerator)}] completed code generation for: {{0}}",
        "CodeGeneration",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor NoTargetTypesFound = new(
        "KURRENT404",
        nameof(KurrentOperationErrorGenerator),
        $"[{nameof(KurrentOperationErrorGenerator)}] no target types found for code generation",
        "CodeGeneration",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    static void ReportDiagnostic(GeneratorExecutionContext context, DiagnosticDescriptor descriptor, INamedTypeSymbol classSymbol) {
        var diagnostic = Diagnostic.Create(descriptor, Location.None, classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""));
        context.ReportDiagnostic(diagnostic);
    }

    static void ReportDiagnostic(GeneratorExecutionContext context, DiagnosticDescriptor descriptor, params object?[]? messageArgs) {
        var diagnostic = Diagnostic.Create(descriptor, Location.None, messageArgs);
        context.ReportDiagnostic(diagnostic);
    }

    #endregion

    static IEnumerable<INamedTypeSymbol> ExtractKurrentOperationErrorTypes(Compilation compilation, GeneratorExecutionContext context) {
        foreach (var syntaxTree in compilation.SyntaxTrees) {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);

            var root = syntaxTree.GetRoot();

            var typeDeclarations = root.DescendantNodes()
                .Where(node => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax);

            // var typeDeclarations = root.DescendantNodes()
            //     .OfType<TypeDeclarationSyntax>()
            //     .Where(td => td.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)) &&
            //                  td.Modifiers.Any(m => m.IsKind(SyntaxKind.RecordKeyword) || m.IsKind(SyntaxKind.StructKeyword)));;

            foreach (var typeDeclaration in typeDeclarations) {
                if (semanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol typeSymbol)
                    continue;

                // if has no attributes, skip
                if (typeSymbol.GetAttributes().Length == 0)
                    continue;

                // Skip if the type is not a partial struct or record
                if (typeSymbol is { IsRecord: false, IsValueType: false })
                    continue;

                if (!HasKurrentOperationErrorAttribute(typeSymbol, context))
                    continue;

                // Only generate for partial structs with the attribute
                // We don't check for interface implementation since we'll generate it
                yield return typeSymbol;
            }
        }
    }

    static bool HasKurrentOperationErrorAttribute(INamedTypeSymbol typeSymbol, GeneratorExecutionContext context) {
        var attr = typeSymbol.GetAttributes().Where(attr => {
            if (attr.AttributeClass is not { } classInfo || !classInfo.ToDisplayString().StartsWith(KurrentOperationErrorAttributeKey))
                return false;

            ReportDiagnostic(context, FoundAttribute, classInfo.ToDisplayString());

            // var enumErrorTypeArg = classInfo.TypeParameters.LastOrDefault();
            //
            // // var errorCode = enumErrorTypeArg.GetAttributes().First().ConstructorArguments.FirstOrDefault();
            //
            // ReportDiagnostic(context, FoundErrorEnumCode, enumErrorTypeArg?.ToDisplayString() ?? "<null>");

            return true;

        }).FirstOrDefault();

        return attr is not null;

        // static bool Predicate(AttributeData attr) =>
        //     attr.AttributeClass is { IsGenericType: true } classInfo &&
        //     classInfo.ToDisplayString().StartsWith(KurrentOperationErrorAttributeKey);
    }

    static SourceText GenerateImplementation(INamedTypeSymbol typeSymbol, GeneratorExecutionContext context) {
        var sourceBuilder = new StringBuilder(
             """
            // <auto-generated />
            // This file was generated by the KurrentOperationExceptionGenerator
            // Changes to this file will be lost when the file is regenerated.
            
            #nullable enable
            
            {USING_STATEMENTS}
            
            {NAMESPACE_DECLARATION}
            
            """);

        var (name, ns) = GetInstanceNameAndNamespace(typeSymbol);

        sourceBuilder
            .Replace("{USING_STATEMENTS}", GenerateUsingStatements(ns))
            .Replace("{NAMESPACE_DECLARATION}", GenerateNamespaceDeclaration(ns));

        const string tab = "    ";

        // Generate type declarations for all containing types
        var containingTypeSymbols = new List<INamedTypeSymbol>();
        var currentContainingType = typeSymbol.ContainingType;
        while (currentContainingType is not null) {
            containingTypeSymbols.Add(currentContainingType);
            currentContainingType = currentContainingType.ContainingType;
        }

        containingTypeSymbols.Reverse();

        var declarationIndent = "";
        foreach (var declaration in containingTypeSymbols.Select(GenerateTypeDeclaration)) {
            declarationIndent += tab;
            sourceBuilder.AppendLine($"{declarationIndent}{declaration} {{");

        }

        // Generate the instance type
        var memberIndent = declarationIndent;

        // const string codeBlock = """
        //         [PublicAPI, CompilerGenerated, GeneratedCode("KurrentOperationErrorGenerator", "1.0.0")]
        //         public partial class {INSTANCE_NAME} : KurrentException {
        //             static readonly ErrorCode ErrorCode = typeof({INSTANCE_NAME}).GetCustomAttribute<KurrentOperationErrorAttribute>()!.ErrorCode;
        //
        //             static Action<Status, {INSTANCE_NAME}>? OnInstanceCreated { get; }
        //
        //             {INSTANCE_NAME}(Status status) : base(status.Message, status.GetDetail<DebugInfo>().ToException()) {
        //                 var details = new KurrentErrorDetails { ErrorCode = ErrorCode };
        //
        //                 if (status.GetDetail<Google.Rpc.ResourceInfo>() is { } resourceInfo)
        //                     details.ResourceInfo = Kurrent.Client.Errors.ResourceInfo.From(resourceInfo.ResourceType, resourceInfo.ResourceName);
        //
        //                 if (status.GetDetail<Google.Rpc.RetryInfo>() is { } retryInfo)
        //                     details.RetryInfo = Kurrent.Client.Errors.RetryInfo.From(retryInfo.RetryDelay.ToTimeSpan());
        //
        //                 OnInstanceCreated?.Invoke(status, this);
        //             }
        //
        //             protected {INSTANCE_NAME}(string message) : base(ErrorCode, message, innerException: null) { }
        //
        //             public static bool TryParseRpcStatus(Status status, ErrorInfo? errorInfo, [MaybeNullWhen(false)] out StreamNotFoundException ex) {
        //                 if (ErrorCode.MatchesRpcStatusReason(status, errorInfo)) {
        //                     ex = new(status);
        //                     return true;
        //                 }
        //
        //                 ex = null;
        //                 return false;
        //             }
        //         }
        //     """;

        const string codeBlock = """
                [PublicAPI, CompilerGenerated, GeneratedCode("KurrentOperationExceptionGenerator", "1.0.0")]
                public partial class {INSTANCE_NAME} : KurrentException, IKurrentOperationException<{INSTANCE_NAME}> {
                    public static ErrorCode Code { get; } = typeof({INSTANCE_NAME}).GetCustomAttribute<KurrentOperationErrorAttribute>()!.ErrorCode;
                
                    public {INSTANCE_NAME}(string message) : base(Code, message, innerException: null) { }
                
                    public {INSTANCE_NAME}(Status status) : base(Code, status) {
                        try {
                            ((IKurrentOperationException<{INSTANCE_NAME}>)this).AfterParseRpcStatus(status, this);
                        }
                        catch (Exception ex) {
                            throw new UnreachableException("EVERYTHING IS BROKEN! The {INSTANCE_NAME}::AfterParseRpcStatus hook threw an exception.", ex);
                        }
                    }
                
                    public static bool TryParseRpcStatus(Status status, ErrorInfo? errorInfo, [MaybeNullWhen(false)] out {INSTANCE_NAME} ex) =>
                        (ex = Code.MatchesRpcStatusReason(status, errorInfo) ? new(status) : null) is not null;
                
                    public static {INSTANCE_NAME}? From(Status status, ErrorInfo? errorInfo = null) => TryParseRpcStatus(status, errorInfo, out var ex) ? ex : null;
                }
            """;


        // const string codeBlock = """
        //         [PublicAPI, CompilerGenerated, GeneratedCode("KurrentOperationErrorGenerator", "1.0.0")]
        //         public partial class {INSTANCE_NAME} : KurrentOperationException {
        //             public static readonly string ErrorReason = typeof({INSTANCE_NAME})
        //                 .GetCustomAttribute<KurrentOperationErrorAttribute>()!.ErrorReason;
        //
        //             public {INSTANCE_NAME}(Status status, ErrorInfo? errorInfo) : base(status, errorInfo) { }
        //
        //             public {INSTANCE_NAME}(string errorMessage, Exception? innerException = null) : base(ErrorReason, errorMessage, innerException) { }
        //
        //             public {INSTANCE_NAME}(Exception? innerException = null) : base(ErrorReason, ErrorReason, innerException) { }
        //         }
        //     """;

        var indentedCode = Regex.Replace(codeBlock, @"(?<=\n)", memberIndent);

        sourceBuilder
            .Append(memberIndent + indentedCode)
            .Replace("{INSTANCE_NAME}", name);

        // Close all containing types declarations
        sourceBuilder.AppendLine();
        for (var i = containingTypeSymbols.Count - 1; i >= 0; i--) {
            declarationIndent = declarationIndent.Substring(0, declarationIndent.Length - tab.Length);
            sourceBuilder.AppendLine($"{memberIndent + declarationIndent}}}");
        }

        // Close the error namespace declaration
        sourceBuilder
            .Append('}')
            .AppendLine();

        // const string extensionsCodeBlock = """
        //     public static partial class StatusExtensions {
        //         [PublicAPI, CompilerGenerated, GeneratedCode("KurrentOperationErrorGenerator", "1.0.0")]
        //         public static {INSTANCE_NAME}? To{INSTANCE_NAME}(this Google.Rpc.Status status, ErrorInfo? errorInfo) =>
        //             {INSTANCE_NAME}.ErrorReason == errorInfo?.Reason || status.MatchesErrorReason({INSTANCE_NAME}.ErrorReason)
        //                 ? new {INSTANCE_NAME}(status, errorInfo) : null;
        //     }
        //     """;

        sourceBuilder
            .AppendLine()
            //.Append(extensionsCodeBlock)
            .Replace("{INSTANCE_NAME}", name);

        return SourceText.From(sourceBuilder.ToString(), Encoding.UTF8);

        static string GenerateUsingStatements(string ns) {
            var stringBuilder = new StringBuilder();

            stringBuilder.AppendLine("using System;");
            stringBuilder.AppendLine("using System.CodeDom.Compiler;");
            stringBuilder.AppendLine("using System.Diagnostics;");
            stringBuilder.AppendLine("using System.Diagnostics.CodeAnalysis;");
            stringBuilder.AppendLine("using System.Runtime.CompilerServices;");
            stringBuilder.AppendLine("using System.Reflection;");
            stringBuilder.AppendLine("using Humanizer;");
            stringBuilder.AppendLine("using Google.Rpc;");
            stringBuilder.AppendLine("using Google.Rpc.Exceptions;");
            stringBuilder.AppendLine("using Kurrent;");
            stringBuilder.AppendLine("using Kurrent.Client;");
            stringBuilder.AppendLine("using Kurrent.Client.Errors;");
            stringBuilder.AppendLine("using Kurrent.Client.Exceptions;");
            stringBuilder.AppendLine("using Kurrent.GoogleRpc;");
            stringBuilder.AppendLine("using Kurrent.Rpc;");

            // if (ns != KurrentClientModelNamespace)
            //     stringBuilder.AppendLine("using Kurrent.Client.Model;");

            return stringBuilder.ToString().TrimEnd();
        }

        static string GenerateNamespaceDeclaration(string ns) =>
            ns != string.Empty ? $"namespace {ns} {{" : "// Global namespace";
    }

    static string GenerateTypeDeclaration(INamedTypeSymbol containingTypeSymbol) {
        var typeDeclarationBuilder = new StringBuilder();

        var accessibility = containingTypeSymbol.DeclaredAccessibility switch {
            Accessibility.Public    => "public ",
            Accessibility.Internal  => "internal ",
            Accessibility.Protected => "protected ",
            _                       => string.Empty
        };

        typeDeclarationBuilder.Append(accessibility);

        if (containingTypeSymbol.IsStatic)
            typeDeclarationBuilder.Append("static ");

        if (containingTypeSymbol.IsReadOnly)
            typeDeclarationBuilder.Append("readonly ");
        else if (containingTypeSymbol is { IsAbstract: true, IsSealed: false })
            typeDeclarationBuilder.Append("abstract ");
        else if (containingTypeSymbol is { IsAbstract: false, IsSealed: true })
            typeDeclarationBuilder.Append("sealed ");

        if (IsPartial(containingTypeSymbol))
            typeDeclarationBuilder.Append("partial ");

        var typeKind = containingTypeSymbol switch {
            { IsRecord   : true, IsValueType: true } => "record struct ",
            { IsRecord   : true }                    => "record ",
            { IsValueType: true }                    => "struct ",
            { TypeKind   : TypeKind.Interface }      => "interface ",
            { TypeKind   : TypeKind.Enum }           => "enum ",
            { TypeKind   : TypeKind.Class }          => "class ",
            _                                        => throw new InvalidOperationException($"Unsupported type kind: {containingTypeSymbol.TypeKind}")
        };

        typeDeclarationBuilder.Append(typeKind);
        typeDeclarationBuilder.Append(containingTypeSymbol.Name);

        return typeDeclarationBuilder.ToString();

        static bool IsPartial(INamedTypeSymbol typeSymbol) =>
            typeSymbol.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax())
                .OfType<TypeDeclarationSyntax>()
                .Any(syntax => syntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));
    }

    static (string Name, string ContainingNamespace) GetInstanceNameAndNamespace(INamedTypeSymbol typeSymbol) {
        var instanceName = typeSymbol.Name;
        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();

        return (Name: instanceName, ContainingNamespace: ns);
    }

    static string GenerateHintName(INamedTypeSymbol typeSymbol) {
        var typeNames          = GetInstanceNameAndNamespace(typeSymbol);
        var sanitizedTypeName  = SanitizeName(typeNames.Name);
        var sanitizedNamespace = SanitizeName(typeNames.ContainingNamespace);

        return $"{sanitizedNamespace}_{sanitizedTypeName}.g.cs";

        // Replace spaces, dashes, and pluses with underscores to ensure a valid file name
        // do not replace dots
        string SanitizeName(string input) {
            return input
                .Replace(' ', '_')
                .Replace('-', '_')
                .Replace('+', '_')
                .Replace('<', '_')
                .Replace('>', '_');
        }
    }
}
