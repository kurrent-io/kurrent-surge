using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Kurrent.CodeAnalysis;
using Kurrent.CodeAnalysis.Emission;
using Kurrent.CodeAnalysis.Pipeline;
using TypeSymbol = Kurrent.CodeAnalysis.TypeSymbol;
using TypeKind = Kurrent.CodeAnalysis.TypeKind;

namespace Kurrent.Variant.SourceGenerators;

[Generator]
[SuppressMessage("MicrosoftCodeAnalysisReleaseTracking", "RS2008:Enable analyzer release tracking")]
public sealed class VariantGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        context
            .Types(t => t
                .OfKind(
                    TypeKind.Class, TypeKind.Record, TypeKind.RecordStruct,
                    TypeKind.Struct)
                .Implementing(static i =>
                    i.Namespace == "Kurrent.Variant" &&
                    i.Name == "IVariant" &&
                    i.TypeArguments.Length > 0))
            .Select(static t => ExtractVariantInfo(t))
            .Where(static info => info is not null)
            .ReportWhenEmpty(NoTargetTypesFound)
            .RegisterSources(static (spc, info) => {
                if (info is null)
                    return;

                var source = GenerateVariantImplementation(info);

                var hintName = SourceGeneratorFileNaming.GetHintName(
                    typeName: info.ClassName,
                    typeNamespace: info.Namespace ?? "",
                    typeFullName: info.FullName,
                    typeArgumentFullNames: null,
                    options: new FileNamingOptions {
                        Prefix                 = "Variants",
                        UseFoldersForPrefix    = true,
                        UseFoldersForNamespace = true,
                        LowercasePath          = false
                    },
                    explicitHashTypeFullNames: info.Cases.Select(c => c.FullyQualifiedNameForHash).ToArray());

                spc.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
                spc.ReportDiagnostic(Diagnostic.Create(SwitchMethodsGenerated, Location.None, info.FullName));
                spc.ReportDiagnostic(Diagnostic.Create(MatchMethodsGenerated, Location.None, info.FullName));

                spc.ReportDiagnostic(Diagnostic.Create(VariantTypeGenerated, Location.None, info.FullName));

                spc.ReportDiagnostic(Diagnostic.Create(SourceAdded, Location.None, hintName));
            });
    }

    static readonly DiagnosticDescriptor SwitchMethodsGenerated = new(
        id: "VARIANT002",
        title: nameof(VariantGenerator),
        messageFormat: $"[{nameof(VariantGenerator)}] switch methods generated for: {{0}}",
        category: "CodeGeneration",
        defaultSeverity: DiagnosticSeverity.Hidden,
        isEnabledByDefault: false);

    static readonly DiagnosticDescriptor MatchMethodsGenerated = new(
        id: "VARIANT003",
        title: nameof(VariantGenerator),
        messageFormat: $"[{nameof(VariantGenerator)}] match methods generated for: {{0}}",
        category: "CodeGeneration",
        defaultSeverity: DiagnosticSeverity.Hidden,
        isEnabledByDefault: false);

    static readonly DiagnosticDescriptor VariantTypeGenerated = new(
        id: "VARIANT201",
        title: nameof(VariantGenerator),
        messageFormat: $"[{nameof(VariantGenerator)}] completed 'IVariant' code generation for: {{0}}",
        category: "CodeGeneration",
        defaultSeverity: DiagnosticSeverity.Hidden,
        isEnabledByDefault: false);

    static readonly DiagnosticDescriptor SourceAdded = new(
        id: "VARIANT201SRC",
        title: nameof(VariantGenerator),
        messageFormat: $"[{nameof(VariantGenerator)}] source added: {{0}}",
        category: "CodeGeneration",
        defaultSeverity: DiagnosticSeverity.Hidden,
        isEnabledByDefault: false);

    static readonly DiagnosticDescriptor NoTargetTypesFound = new(
        id: "VARIANT404",
        title: nameof(VariantGenerator),
        messageFormat: $"[{nameof(VariantGenerator)}] no target types found for code generation",
        category: "CodeGeneration",
        defaultSeverity: DiagnosticSeverity.Hidden,
        isEnabledByDefault: false);

    static VariantInfo? ExtractVariantInfo(TypeSymbol t) {
        TypeSymbol? iVariant = null;

        foreach (var iface in t.Interfaces) {
            if (iface.Namespace == "Kurrent.Variant" && iface.Name == "IVariant" && iface.TypeArguments.Length > 0) {
                iVariant = iface;
                break;
            }
        }

        if (iVariant is null)
            return null;

        var cases = new VariantCaseInfo[iVariant.TypeArguments.Length];

        for (var i = 0; i < iVariant.TypeArguments.Length; i++) {
            var arg = iVariant.TypeArguments[i];

            cases[i] = new VariantCaseInfo(
                FullyQualifiedName: FullyQualified(arg),
                FullyQualifiedNameForHash: arg.FullName,
                CleanTypeName: GetCleanTypeName(arg),
                ParameterName: GetParameterName(arg),
                IsValueType: arg.IsValueType,
                IsReferenceType: arg.IsReferenceType,
                IsNullableOfT: arg.IsNullableOfT);
        }

        var typeParameterNames = new string[t.TypeArguments.Length];

        for (var i = 0; i < t.TypeArguments.Length; i++)
            typeParameterNames[i] = t.TypeArguments[i].Name;

        var containerChain = new string[t.ContainingTypes.Length];

        for (var i = 0; i < t.ContainingTypes.Length; i++)
            containerChain[i] = t.ContainingTypes[i].Name;

        // Match the user's declared accessibility on the partial type and most members so
        // partial declarations agree (CS0262) and non-public type arguments don't trip
        // accessibility-consistency checks (CS0051 / CS0057). Exceptions stay `public`:
        //   * user-defined operators — required by C# (CS0558 / CS9308)
        //   * IVariant.Value / IVariant.Index — interface members (CS0737)
        //   * ToString — overrides object.ToString (must match base accessibility)
        var accessibility = t.Accessibility switch {
            Accessibility.Public               => "public",
            Accessibility.Internal             => "internal",
            Accessibility.Protected            => "protected",
            Accessibility.Private              => "private",
            Accessibility.ProtectedOrInternal  => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _                                  => "internal"
        };

        return new VariantInfo(
            ClassName: t.Name,
            FullName: t.FullName,
            Namespace: string.IsNullOrEmpty(t.Namespace) ? null : t.Namespace,
            Kind: t.Kind,
            Accessibility: accessibility,
            TypeParameterNames: typeParameterNames,
            ContainingTypeNames: containerChain,
            Cases: cases);
    }

    #region ->> String helpers (TypeSymbol-based replacements for the old ISymbol helpers) <<-

    static string FullyQualified(TypeSymbol t) {
        if (t.Kind == TypeKind.Array && t.ArrayElementType is not null)
            return $"{FullyQualified(t.ArrayElementType)}[]";

        if (t.IsNullableOfT && t.TypeArguments.Length == 1)
            return $"{FullyQualified(t.TypeArguments[0])}?";

        var baseName = $"global::{t.FullName}";

        if (t.TypeArguments.Length > 0 && !t.IsNullableOfT) {
            var args = new string[t.TypeArguments.Length];

            for (var i = 0; i < t.TypeArguments.Length; i++)
                args[i] = FullyQualified(t.TypeArguments[i]);

            baseName += $"<{string.Join(", ", args)}>";
        }

        return baseName;
    }

    static string GetCleanTypeName(TypeSymbol t) {
        switch (t.Special) {
            case SpecialType.System_Boolean: return "Bool";

            case SpecialType.System_Byte: return "Byte";

            case SpecialType.System_SByte: return "SByte";

            case SpecialType.System_Char: return "Char";

            case SpecialType.System_Decimal: return "Decimal";

            case SpecialType.System_Double: return "Double";

            case SpecialType.System_Single: return "Float";

            case SpecialType.System_Int32: return "Int";

            case SpecialType.System_UInt32: return "UInt";

            case SpecialType.System_Int64: return "Long";

            case SpecialType.System_UInt64: return "ULong";

            case SpecialType.System_Int16: return "Short";

            case SpecialType.System_UInt16: return "UShort";

            case SpecialType.System_Object: return "Object";

            case SpecialType.System_String: return "String";
        }

        if (t is { IsNullableOfT: true, TypeArguments.Length: 1 })
            return $"{GetCleanTypeName(t.TypeArguments[0])}Nullable";

        var name = t.Name;

        if (t.TypeArguments.Length > 0 && !t.IsNullableOfT) {
            var sb = new StringBuilder(t.Name);

            foreach (var arg in t.TypeArguments)
                sb.Append(GetCleanTypeName(arg));

            name = sb.ToString();
        }
        else if (t.Kind == TypeKind.Array && t.ArrayElementType is not null) {
            name = $"{GetCleanTypeName(t.ArrayElementType)}Array";
        }

        name = name.Replace(".", "").Replace("<", "").Replace(">", "").Replace(",", "").Replace(" ", "");

        if (string.IsNullOrEmpty(name) || name == "Nullable`1")
            return "Type";

        if (name.Length > 0 && char.IsDigit(name[0]))
            name = $"_{name}";

        return SanitizeIdentifier(name);
    }

    static string SanitizeIdentifier(string name) {
        if (string.IsNullOrEmpty(name))
            return "_";

        var sb = new StringBuilder();

        if (!char.IsLetter(name[0]) && name[0] != '_')
            sb.Append('_');

        foreach (var c in name) {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            else
                sb.Append('_');
        }

        var result = sb.ToString();
        return string.IsNullOrEmpty(result) ? "GeneratedType" : result;
    }

    static string GetParameterName(TypeSymbol t) {
        string paramNameBase;

        if (t is { IsNullableOfT: true, TypeArguments.Length: 1 })
            paramNameBase = $"{GetParameterName(t.TypeArguments[0])}Opt";
        else
            switch (t.Special) {
                case SpecialType.System_Boolean: paramNameBase = "bool"; break;

                case SpecialType.System_Byte: paramNameBase = "byte"; break;

                case SpecialType.System_SByte: paramNameBase = "sbyte"; break;

                case SpecialType.System_Char: paramNameBase = "char"; break;

                case SpecialType.System_Decimal: paramNameBase = "decimal"; break;

                case SpecialType.System_Double: paramNameBase = "double"; break;

                case SpecialType.System_Single: paramNameBase = "float"; break;

                case SpecialType.System_Int32: paramNameBase = "int"; break;

                case SpecialType.System_UInt32: paramNameBase = "uint"; break;

                case SpecialType.System_Int64: paramNameBase = "long"; break;

                case SpecialType.System_UInt64: paramNameBase = "ulong"; break;

                case SpecialType.System_Int16: paramNameBase = "short"; break;

                case SpecialType.System_UInt16: paramNameBase = "ushort"; break;

                case SpecialType.System_Object: paramNameBase = "object"; break;

                case SpecialType.System_String: paramNameBase = "string"; break;

                default: {
                    var clean = GetCleanTypeName(t);

                    if (string.IsNullOrEmpty(clean) || clean == "_" || clean.EndsWith("Nullable", StringComparison.Ordinal)) {
                        if (clean.EndsWith("Nullable", StringComparison.Ordinal)) {
                            var baseName = clean.Substring(0, clean.Length - "Nullable".Length);

                            if (baseName.Length > 0 && char.IsUpper(baseName[0]))
                                baseName = char.ToLowerInvariant(baseName[0]) + baseName.Substring(1);
                            else if (string.IsNullOrEmpty(baseName))
                                baseName = "value";

                            paramNameBase = $"{baseName}Opt";
                        }
                        else {
                            paramNameBase = "arg";
                        }
                    }
                    else if (char.IsUpper(clean[0])) {
                        if (clean.Length == 1 || (clean.Length > 1 && char.IsLower(clean[1]))) {
                            paramNameBase = char.ToLowerInvariant(clean[0]) + clean.Substring(1);
                        }
                        else {
                            var allUpper = true;

                            foreach (var c in clean) {
                                if (char.IsLower(c)) {
                                    allUpper = false;
                                    break;
                                }
                            }

                            paramNameBase = allUpper
                                ? clean.ToLowerInvariant()
                                : char.ToLowerInvariant(clean[0]) + clean.Substring(1);
                        }
                    }
                    else {
                        paramNameBase = clean;
                    }

                    break;
                }
            }

        if (SyntaxFacts.IsKeywordKind(SyntaxFacts.GetKeywordKind(paramNameBase)) || paramNameBase == "value")
            return $"@{paramNameBase}";

        return paramNameBase;
    }

    #endregion

    #region ->> Emission <<-

    static string GenerateVariantImplementation(VariantInfo info) {
        var className = info.ClassName;
        var access    = info.Accessibility;

        var minimallyQualifiedClassName = info.TypeParameterNames.Length > 0
            ? $"{className}<{string.Join(", ", info.TypeParameterNames)}>"
            : className;

        var sb = new StringBuilder();
        sb.AppendLine($"// <auto-generated by VariantGenerator for {minimallyQualifiedClassName} />");

        sb.AppendLine(
            "#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.");

        sb.AppendLine("// ReSharper disable SwitchStatementHandlesSomeKnownEnumValuesWithDefault");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.CodeDom.Compiler;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Kurrent.Variant;");

        sb.AppendLine();

        if (!string.IsNullOrEmpty(info.Namespace)) {
            sb.AppendLine($"namespace {info.Namespace};");
            sb.AppendLine();
        }

        foreach (var containingName in info.ContainingTypeNames)
            sb.AppendLine($"partial class {containingName} {{");

        var baseIndent = new string(' ', 4 * info.ContainingTypeNames.Length);

        sb.AppendLine("[GeneratedCode(\"VariantGenerator\", \"1.0.0\")]");
        sb.AppendLine("[CompilerGenerated]");

        sb.AppendLine($"{baseIndent}{access} readonly partial record struct {className} {{");

        var memberIndent = $"{baseIndent}    ";

        sb.AppendLine($"{memberIndent}readonly object _value;");
        sb.AppendLine($"{memberIndent}readonly byte _index;");
        sb.AppendLine();

        for (var i = 0; i < info.Cases.Length; i++) {
            var c = info.Cases[i];

            sb.AppendLine($"{memberIndent}{access} {className}({c.FullyQualifiedName} {c.ParameterName}) {{");

            if (!c.IsValueType && !c.IsNullableOfT && c.IsReferenceType)
                sb.AppendLine($"{memberIndent}    ArgumentNullException.ThrowIfNull({c.ParameterName});");

            sb.AppendLine($"{memberIndent}    _value = {c.ParameterName};");
            sb.AppendLine($"{memberIndent}    _index = {i};");
            sb.AppendLine();
            sb.AppendLine($"{memberIndent}}}");
        }

        sb.AppendLine();

        GenerateCaseEnum(
            sb, memberIndent, className, access,
            info.Cases);

        // Value and Index implement IVariant (a public interface) — interface members
        // must be declared public on the implementor regardless of containing accessibility (CS0737).
        sb.AppendLine($"{memberIndent}public object Value => _value;");
        sb.AppendLine($"{memberIndent}public int    Index => _index;");
        sb.AppendLine();
        sb.AppendLine($"{memberIndent}{access} {className}Case Case => ({className}Case)_index;");
        sb.AppendLine();

        for (var i = 0; i < info.Cases.Length; i++) {
            var c = info.Cases[i];
            sb.AppendLine($"{memberIndent}{access} bool Is{c.CleanTypeName} => Case == {className}Case.{c.CleanTypeName};");

            sb.AppendLine(
                $"{memberIndent}{access} {c.FullyQualifiedName} As{c.CleanTypeName} => Is{c.CleanTypeName} ? ({c.FullyQualifiedName})Value! : throw new InvalidOperationException($\"Cannot return as {c.CleanTypeName} as current type is {{Value?.GetType().Name ?? \"unknown\"}} (Case {{Case}})\");");

            sb.AppendLine();
        }

        sb.AppendLine("#region . Implicit Operators .");
        sb.AppendLine();

        // User-defined operators must be `public` per the C# language spec (CS0558 / CS9308),
        // regardless of the containing type's accessibility. Effective accessibility is still
        // bounded by the containing type, so non-public variants remain correctly hidden.
        for (var i = 0; i < info.Cases.Length; i++) {
            var c = info.Cases[i];
            sb.AppendLine($"{memberIndent}public static implicit operator {minimallyQualifiedClassName}({c.FullyQualifiedName} _) => new(_);");
            sb.AppendLine($"{memberIndent}public static implicit operator {c.FullyQualifiedName}({minimallyQualifiedClassName} _) => _.As{c.CleanTypeName};");
        }

        sb.AppendLine();
        sb.AppendLine("#endregion");
        sb.AppendLine();

        GenerateSwitchMethods(
            sb, memberIndent, className, access,
            info.Cases);

        GenerateMatchMethods(
            sb, memberIndent, className, access,
            info.Cases);

        if (info.Kind == TypeKind.Struct || info.Kind == TypeKind.RecordStruct)
            GenerateToStringOverride(sb, memberIndent, access);

        sb.AppendLine($"{baseIndent}}}");

        for (var i = info.ContainingTypeNames.Length - 1; i >= 0; i--)
            sb.AppendLine($"{new string(' ', 4 * i)}}}");

        return sb.ToString();
    }

    static void GenerateCaseEnum(StringBuilder sb, string indent, string className, string access, VariantCaseInfo[] cases) {
        sb.AppendLine($"{indent}{access} enum {className}Case : byte {{");

        for (var i = 0; i < cases.Length; i++) {
            var c = cases[i];
            sb.AppendLine($"{indent}    {c.CleanTypeName} = {i}{(i < cases.Length - 1 ? "," : "")}");
        }

        sb.AppendLine($"{indent}}}");
        sb.AppendLine();
    }

    static void GenerateToStringOverride(StringBuilder sb, string indent, string access) {
        sb.AppendLine();
        sb.AppendLine($"{indent}/// <summary>");
        sb.AppendLine($"{indent}/// Returns a string representation of the current variant value.");
        sb.AppendLine($"{indent}/// </summary>");
        // ToString() overrides object.ToString() which is public — override accessibility
        // must match the base, so this always emits as `public` regardless of {access}.
        sb.AppendLine($"{indent}public override string? ToString() => _value?.ToString();");
    }

    #endregion

    #region ->> Switch emission <<-

    static void GenerateSwitchMethods(StringBuilder sb, string indent, string className, string access, VariantCaseInfo[] cases) {
        sb.AppendLine();
        sb.AppendLine($"{indent}#region . Switch .");
        sb.AppendLine();

        sb.Append($"{indent}{access} void Switch(");

        AppendSwitchParameterList(
            sb, cases, async: false,
            withState: false);

        sb.AppendLine(") {");
        sb.AppendLine($"{indent}    switch (Case) {{");

        for (var i = 0; i < cases.Length; i++) {
            var c = cases[i];
            sb.AppendLine($"{indent}        case {className}Case.{c.CleanTypeName}: {BuildParamName(c)}(As{c.CleanTypeName}); break;");
        }

        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine();

        GenerateSwitchWithState(
            sb, indent, className, access,
            cases);

        GenerateSwitchAsync(
            sb, indent, className, access,
            cases);

        sb.AppendLine($"{indent}#endregion");
    }

    static void GenerateSwitchWithState(StringBuilder sb, string indent, string className, string access, VariantCaseInfo[] cases) {
        sb.Append($"{indent}{access} void Switch<TState>(");

        AppendSwitchParameterList(
            sb, cases, async: false,
            withState: true);

        sb.AppendLine(", TState state) {");
        sb.AppendLine($"{indent}    switch (Case) {{");

        for (var i = 0; i < cases.Length; i++) {
            var c = cases[i];
            sb.AppendLine($"{indent}        case {className}Case.{c.CleanTypeName}: {BuildParamName(c)}(As{c.CleanTypeName}, state); break;");
        }

        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine();
    }

    static void GenerateSwitchAsync(StringBuilder sb, string indent, string className, string access, VariantCaseInfo[] cases) {
        sb.Append($"{indent}{access} ValueTask SwitchAsync(");

        AppendSwitchParameterList(
            sb, cases, async: true,
            withState: false);

        sb.AppendLine(") => Case switch {");

        for (var i = 0; i < cases.Length; i++) {
            var c = cases[i];
            sb.AppendLine($"{indent}    {className}Case.{c.CleanTypeName} => {BuildParamName(c)}(As{c.CleanTypeName}),");
        }

        sb.AppendLine($"{indent}}};");
        sb.AppendLine();

        sb.Append($"{indent}{access} ValueTask SwitchAsync<TState>(");

        AppendSwitchParameterList(
            sb, cases, async: true,
            withState: true);

        sb.AppendLine(", TState state) => Case switch {");

        for (var i = 0; i < cases.Length; i++) {
            var c = cases[i];
            sb.AppendLine($"{indent}    {className}Case.{c.CleanTypeName} => {BuildParamName(c)}(As{c.CleanTypeName}, state),");
        }

        sb.AppendLine($"{indent}}};");
        sb.AppendLine();
    }

    static void AppendSwitchParameterList(StringBuilder sb, VariantCaseInfo[] cases, bool async, bool withState) {
        for (var i = 0; i < cases.Length; i++) {
            var c         = cases[i];
            var paramName = BuildParamName(c);

            string signature;

            if (async)
                signature = withState
                    ? $"Func<{c.FullyQualifiedName}, TState, ValueTask> {paramName}"
                    : $"Func<{c.FullyQualifiedName}, ValueTask> {paramName}";
            else
                signature = withState
                    ? $"Action<{c.FullyQualifiedName}, TState> {paramName}"
                    : $"Action<{c.FullyQualifiedName}> {paramName}";

            sb.Append(signature);

            if (i < cases.Length - 1)
                sb.Append(", ");
        }
    }

    #endregion

    #region ->> Match emission <<-

    static void GenerateMatchMethods(StringBuilder sb, string indent, string className, string access, VariantCaseInfo[] cases) {
        sb.AppendLine();
        sb.AppendLine($"{indent}#region . Match .");
        sb.AppendLine();

        sb.Append($"{indent}{access} TResult Match<TResult>(");

        AppendMatchParameterList(
            sb, cases, async: false,
            withState: false);

        sb.AppendLine(") => Case switch {");

        for (var i = 0; i < cases.Length; i++) {
            var c = cases[i];
            sb.AppendLine($"{indent}    {className}Case.{c.CleanTypeName} => {BuildParamName(c)}(As{c.CleanTypeName}),");
        }

        sb.AppendLine($"{indent}}};");
        sb.AppendLine();

        GenerateMatchWithState(
            sb, indent, className, access,
            cases);

        GenerateMatchAsync(
            sb, indent, className, access,
            cases);

        sb.AppendLine($"{indent}#endregion");
    }

    static void GenerateMatchWithState(StringBuilder sb, string indent, string className, string access, VariantCaseInfo[] cases) {
        sb.Append($"{indent}{access} TResult Match<TResult, TState>(");

        AppendMatchParameterList(
            sb, cases, async: false,
            withState: true);

        sb.AppendLine(", TState state) => Case switch {");

        for (var i = 0; i < cases.Length; i++) {
            var c = cases[i];
            sb.AppendLine($"{indent}    {className}Case.{c.CleanTypeName} => {BuildParamName(c)}(As{c.CleanTypeName}, state),");
        }

        sb.AppendLine($"{indent}}};");
        sb.AppendLine();
    }

    static void GenerateMatchAsync(StringBuilder sb, string indent, string className, string access, VariantCaseInfo[] cases) {
        sb.Append($"{indent}{access} ValueTask<TResult> MatchAsync<TResult>(");

        AppendMatchParameterList(
            sb, cases, async: true,
            withState: false);

        sb.AppendLine(") => Case switch {");

        for (var i = 0; i < cases.Length; i++) {
            var c = cases[i];
            sb.AppendLine($"{indent}    {className}Case.{c.CleanTypeName} => {BuildParamName(c)}(As{c.CleanTypeName}),");
        }

        sb.AppendLine($"{indent}}};");
        sb.AppendLine();

        sb.Append($"{indent}{access} ValueTask<TResult> MatchAsync<TResult, TState>(");

        AppendMatchParameterList(
            sb, cases, async: true,
            withState: true);

        sb.AppendLine(", TState state) => Case switch {");

        for (var i = 0; i < cases.Length; i++) {
            var c = cases[i];
            sb.AppendLine($"{indent}    {className}Case.{c.CleanTypeName} => {BuildParamName(c)}(As{c.CleanTypeName}, state),");
        }

        sb.AppendLine($"{indent}}};");
        sb.AppendLine();
    }

    static void AppendMatchParameterList(StringBuilder sb, VariantCaseInfo[] cases, bool async, bool withState) {
        for (var i = 0; i < cases.Length; i++) {
            var c         = cases[i];
            var paramName = BuildParamName(c);

            string signature;

            if (async)
                signature = withState
                    ? $"Func<{c.FullyQualifiedName}, TState, ValueTask<TResult>> {paramName}"
                    : $"Func<{c.FullyQualifiedName}, ValueTask<TResult>> {paramName}";
            else
                signature = withState
                    ? $"Func<{c.FullyQualifiedName}, TState, TResult> {paramName}"
                    : $"Func<{c.FullyQualifiedName}, TResult> {paramName}";

            sb.Append(signature);

            if (i < cases.Length - 1)
                sb.Append(", ");
        }
    }

    #endregion

    static string BuildParamName(VariantCaseInfo c) {
        var raw       = $"on{c.CleanTypeName}";
        var sanitized = SanitizeIdentifier(raw);

        return SyntaxFacts.IsKeywordKind(SyntaxFacts.GetKeywordKind(sanitized))
            ? $"@{sanitized}"
            : sanitized;
    }

    sealed record VariantInfo(
        string ClassName,
        string FullName,
        string? Namespace,
        TypeKind Kind,
        string Accessibility,
        string[] TypeParameterNames,
        string[] ContainingTypeNames,
        VariantCaseInfo[] Cases
    ) {
        public bool Equals(VariantInfo? other) =>
            other is not null
         && ClassName == other.ClassName
         && FullName == other.FullName
         && Namespace == other.Namespace
         && Kind == other.Kind
         && Accessibility == other.Accessibility
         && TypeParameterNames.AsSpan().SequenceEqual(other.TypeParameterNames.AsSpan())
         && ContainingTypeNames.AsSpan().SequenceEqual(other.ContainingTypeNames.AsSpan())
         && Cases.AsSpan().SequenceEqual(other.Cases.AsSpan());

        public override int GetHashCode() =>
            HashCode.New
                .Combine(ClassName)
                .Combine(FullName)
                .Combine(Namespace)
                .Combine(Kind)
                .Combine(Accessibility)
                .Combine(TypeParameterNames)
                .Combine(ContainingTypeNames)
                .Combine(Cases);
    }

    sealed record VariantCaseInfo(
        string FullyQualifiedName,
        string FullyQualifiedNameForHash,
        string CleanTypeName,
        string ParameterName,
        bool IsValueType,
        bool IsReferenceType,
        bool IsNullableOfT
    );
}