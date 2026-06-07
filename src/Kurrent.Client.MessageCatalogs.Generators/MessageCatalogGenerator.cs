using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Kurrent.Client.MessageCatalogs.Generators;

/// <summary>
/// Generates message catalogs in CONSUMER assemblies (this generator ships inside the
/// Kurrent.Client package). From [assembly: MessageCatalog("Prefix", Key = ...)] scans and
/// the MessageType attribute family it emits:
///   1. {RootNamespace}.MessageCatalogs — one MessageCatalog property per declared key,
///      All, and a pattern-matchable Get(key) returning null on miss;
///   2. a derived MessageCatalogProviderAttribute applied at assembly level — the export
///      manifest that makes catalogs auto-discoverable (and AOT-rooted) with zero user input;
///   3. Use*Catalog() extension methods in the Kurrent.Client namespace, ASP.NET-style, so
///      IntelliSense surfaces every catalog on the message options without extra usings.
/// </summary>
[Generator]
[SuppressMessage("MicrosoftCodeAnalysisReleaseTracking", "RS2008:Enable analyzer release tracking")]
public sealed class MessageCatalogGenerator : IIncrementalGenerator {
    const string SchemaNamespace             = "Kurrent.Client.Schema";
    const string MessageCatalogAttributeName = "MessageCatalogAttribute";
    const string MessageTypeAttributeName    = "MessageTypeAttribute";
    const string DefaultCatalogKey           = "Default";

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        // Assembly-level attributes + whole-assembly scans require the full compilation, so
        // this pipeline cannot use ForAttributeWithMetadataName. The scan re-runs on every
        // compilation change, but the extracted model is equatable: when the catalog set is
        // unchanged, the output step is cached and nothing is regenerated.
        var model = context.CompilationProvider.Select(static (compilation, ct) => Extract(compilation, ct));

        context.RegisterSourceOutput(model, static (spc, info) => {
            if (!info.HasCatalogs)
                return;

            try {
                spc.AddSource("MessageCatalogs.g.cs", SourceText.From(GenerateSource(info), Encoding.UTF8));
            }
            catch (Exception ex) {
                spc.ReportDiagnostic(Diagnostic.Create(Failed, Location.None, ex.ToString()));
            }
        });
    }

    static readonly DiagnosticDescriptor Failed = new(
        id: "KURRENT520",
        title: nameof(MessageCatalogGenerator),
        messageFormat: $"[{nameof(MessageCatalogGenerator)}] failed: {{0}}",
        category: "Error",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    static CatalogsInfo Extract(Compilation compilation, CancellationToken ct) {
        // key → (registration identity → registration); same key declared twice merges.
        var catalogs    = new Dictionary<string, Dictionary<(string, string?, string?), RegistrationInfo>>(StringComparer.Ordinal);
        var prefixScans = new List<PrefixScan>();

        foreach (var attribute in compilation.Assembly.GetAttributes()) {
            ct.ThrowIfCancellationRequested();

            var attrClass = attribute.AttributeClass;
            if (attrClass is null)
                continue;

            if (IsFamily(attrClass, MessageCatalogAttributeName)) {
                var prefix = attribute.ConstructorArguments.FirstOrDefault().Value as string;
                if (string.IsNullOrEmpty(prefix))
                    continue;

                var key      = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Key").Value.Value as string ?? DefaultCatalogKey;
                var strategy = (attribute.NamedArguments.FirstOrDefault(a => a.Key == "Strategy").Value.Value as INamedTypeSymbol)?.ToDisplayString();

                // MessageCatalogAttribute<TMustImplement> constrains the scan via the generic.
                var mustImplement = attrClass.Arity == 1 ? attrClass.TypeArguments[0] as INamedTypeSymbol : null;

                prefixScans.Add(new PrefixScan(prefix!, key, strategy, mustImplement));
            }
            else if (IsFamily(attrClass, MessageTypeAttributeName)) {
                AddMessageType(attribute, attrClass);
            }
        }

        // Single walk over the source assembly's types serves both discovery sources.
        // Scans only the source assembly: referenced assemblies generate their own catalogs.
        var namespaces = new Stack<INamespaceSymbol>();
        namespaces.Push(compilation.Assembly.GlobalNamespace);

        while (namespaces.Count > 0) {
            ct.ThrowIfCancellationRequested();

            var ns = namespaces.Pop();

            foreach (var type in ns.GetTypeMembers()) {
                foreach (var attribute in type.GetAttributes()) {
                    if (attribute.AttributeClass is { } attrClass && IsFamily(attrClass, MessageTypeAttributeName))
                        AddMessageType(attribute, attrClass);
                }

                if (prefixScans.Count == 0 || !IsScanCandidate(type))
                    continue;

                var fullName = type.ToDisplayString();

                foreach (var scan in prefixScans) {
                    if (fullName.StartsWith(scan.Prefix, StringComparison.Ordinal) && Satisfies(type, scan.MustImplement))
                        Add(scan.Key, new RegistrationInfo(fullName, SchemaName: null, scan.StrategyTypeName));
                }
            }

            foreach (var nested in ns.GetNamespaceMembers())
                namespaces.Push(nested);
        }

        if (catalogs.Count == 0)
            return CatalogsInfo.None;

        // Sorted for deterministic output — keeps the incremental cache and the emitted
        // source stable across builds regardless of symbol enumeration order.
        var ordered = catalogs
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => new CatalogInfo(
                c.Key,
                c.Value.Values
                    .OrderBy(r => r.TypeName, StringComparer.Ordinal)
                    .ThenBy(r => r.SchemaName, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();

        return new CatalogsInfo(
            HasCatalogs: true,
            RootNamespace: compilation.AssemblyName ?? "Kurrent.Generated",
            AssemblyId: Sanitize(compilation.AssemblyName ?? "Assembly"),
            ordered);

        void Add(string key, RegistrationInfo registration) {
            if (!catalogs.TryGetValue(key, out var entries))
                catalogs[key] = entries = new Dictionary<(string, string?, string?), RegistrationInfo>();

            var identity = (registration.TypeName, registration.SchemaName, registration.StrategyTypeName);
            if (!entries.ContainsKey(identity))
                entries.Add(identity, registration);
        }

        void AddMessageType(AttributeData attribute, INamedTypeSymbol attrClass) {
            // MessageTypeAttribute<T>("name")      — arity 1, pinned name in ctor arg 0
            // MessageTypeAttribute<T, TStrategy>() — arity 2, strategy in type arg 1
            if (attrClass.Arity is not (1 or 2))
                return;

            var messageType = attrClass.TypeArguments[0].ToDisplayString();
            var schemaName  = attrClass.Arity == 1 ? attribute.ConstructorArguments.FirstOrDefault().Value as string : null;
            var strategy    = attrClass.Arity == 2 ? attrClass.TypeArguments[1].ToDisplayString() : null;
            var catalogKey  = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Catalog").Value.Value as string ?? DefaultCatalogKey;

            Add(catalogKey, new RegistrationInfo(messageType, schemaName, strategy));
        }
    }

    // Family match by namespace + name across the base chain: covers every arity and future
    // derived sugar without hardcoding constructed generic names.
    static bool IsFamily(INamedTypeSymbol attrClass, string familyName) {
        for (var type = attrClass; type is not null; type = type.BaseType) {
            if (type.Name == familyName && type.ContainingNamespace?.ToDisplayString() == SchemaNamespace)
                return true;
        }

        return false;
    }

    static bool IsScanCandidate(INamedTypeSymbol type) =>
        type is {
            TypeKind: TypeKind.Class or TypeKind.Struct,
            IsAbstract: false,
            IsStatic: false,
            IsImplicitlyDeclared: false,
            Arity: 0, // open generics cannot be catalog entries: a schema maps to a closed type
            DeclaredAccessibility: Accessibility.Public or Accessibility.Internal
        };

    static bool Satisfies(INamedTypeSymbol type, INamedTypeSymbol? mustImplement) {
        if (mustImplement is null)
            return true;

        if (type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, mustImplement)))
            return true;

        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType) {
            if (SymbolEqualityComparer.Default.Equals(baseType, mustImplement))
                return true;
        }

        return false;
    }

    static string GenerateSource(CatalogsInfo info) {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// This file was generated by the MessageCatalogGenerator");
        sb.AppendLine("// Changes to this file will be lost when the file is regenerated.");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"[assembly: global::{info.RootNamespace}.{info.AssemblyId}MessageCatalogProviderAttribute]");
        sb.AppendLine();
        sb.AppendLine($"namespace {info.RootNamespace} {{");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Message catalogs declared by this assembly's attributes.</summary>");
        sb.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"MessageCatalogGenerator\", \"3.0.0\")]");
        sb.AppendLine("    public static class MessageCatalogs {");

        foreach (var catalog in info.Catalogs) {
            sb.AppendLine($"        public static global::Kurrent.Client.MessageCatalog {PropertyName(catalog.Key)} {{ get; }} = global::Kurrent.Client.MessageCatalog.From(\"{catalog.Key}\", [");

            foreach (var r in catalog.Registrations) {
                var name     = r.SchemaName is null ? "null" : $"\"{r.SchemaName}\"";
                var strategy = r.StrategyTypeName is null ? "null" : $"new global::{r.StrategyTypeName}()";

                sb.AppendLine($"            new(typeof(global::{r.TypeName}), {name}, {strategy}, \"{catalog.Key}\"),");
            }

            sb.AppendLine("        ]);");
            sb.AppendLine();
        }

        var propertyNames = info.Catalogs.Select(c => PropertyName(c.Key)).ToArray();

        sb.AppendLine($"        public static global::System.Collections.Generic.IReadOnlyList<global::Kurrent.Client.MessageCatalog> All {{ get; }} = [{string.Join(", ", propertyNames)}];");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>Returns the catalog with the given key, or null — pattern-matchable.</summary>");
        sb.AppendLine("        public static global::Kurrent.Client.MessageCatalog? Get(string key) => key switch {");

        foreach (var catalog in info.Catalogs)
            sb.AppendLine($"            \"{catalog.Key}\" => {PropertyName(catalog.Key)},");

        sb.AppendLine("            _ => null");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>This assembly's catalog export manifest — enables auto-discovery and roots the catalogs for AOT.</summary>");
        sb.AppendLine($"    sealed class {info.AssemblyId}MessageCatalogProviderAttribute : global::Kurrent.Client.MessageCatalogProviderAttribute {{");
        sb.AppendLine("        public override global::System.Collections.Generic.IReadOnlyList<global::Kurrent.Client.MessageCatalog> GetCatalogs() => MessageCatalogs.All;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("// ASP.NET-style: extensions live in the SDK namespace so IntelliSense surfaces every");
        sb.AppendLine("// referenced module's catalogs on the message options without extra usings.");
        sb.AppendLine("namespace Kurrent.Client {");
        sb.AppendLine();
        sb.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"MessageCatalogGenerator\", \"3.0.0\")]");
        sb.AppendLine($"    public static class {info.AssemblyId}MessageCatalogExtensions {{");

        foreach (var catalog in info.Catalogs) {
            sb.AppendLine($"        public static global::Kurrent.Client.KurrentClientMessageOptions Use{PropertyName(catalog.Key)}Catalog(this global::Kurrent.Client.KurrentClientMessageOptions options) =>");
            sb.AppendLine($"            options.UseCatalog(global::{info.RootNamespace}.MessageCatalogs.{PropertyName(catalog.Key)});");
            sb.AppendLine();
        }

        sb.AppendLine($"        public static global::Kurrent.Client.KurrentClientMessageOptions Use{info.AssemblyId}Catalogs(this global::Kurrent.Client.KurrentClientMessageOptions options) =>");
        sb.AppendLine($"            options.UseCatalog(global::{info.RootNamespace}.MessageCatalogs.All);");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    // "orders" → "Orders"; "order-events" → "OrderEvents"; leading digits get a prefix.
    static string PropertyName(string key) {
        var parts = key.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        var name  = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
        return name.Length > 0 && char.IsDigit(name[0]) ? "_" + name : name;
    }

    static string Sanitize(string input) =>
        new(input.Where(char.IsLetterOrDigit).ToArray());

    readonly struct PrefixScan(string prefix, string key, string? strategyTypeName, INamedTypeSymbol? mustImplement) {
        public string            Prefix           { get; } = prefix;
        public string            Key              { get; } = key;
        public string?           StrategyTypeName { get; } = strategyTypeName;
        public INamedTypeSymbol? MustImplement    { get; } = mustImplement;
    }

    // Scalar-only records: automatic value equality is correct and cache-safe.
    sealed record RegistrationInfo(string TypeName, string? SchemaName, string? StrategyTypeName);

    sealed record CatalogInfo(string Key, RegistrationInfo[] Registrations) {
        public bool Equals(CatalogInfo? other) =>
            other is not null && Key == other.Key && Registrations.AsSpan().SequenceEqual(other.Registrations.AsSpan());

        public override int GetHashCode() =>
            Kurrent.CodeAnalysis.HashCode.New.Combine(Key).Combine(Registrations);
    }

    sealed record CatalogsInfo(bool HasCatalogs, string RootNamespace, string AssemblyId, CatalogInfo[] Catalogs) {
        public static readonly CatalogsInfo None = new(HasCatalogs: false, "", "", []);

        public bool Equals(CatalogsInfo? other) =>
            other is not null
         && HasCatalogs == other.HasCatalogs
         && RootNamespace == other.RootNamespace
         && AssemblyId == other.AssemblyId
         && Catalogs.AsSpan().SequenceEqual(other.Catalogs.AsSpan());

        public override int GetHashCode() =>
            Kurrent.CodeAnalysis.HashCode.New
                .Combine(HasCatalogs)
                .Combine(RootNamespace)
                .Combine(AssemblyId)
                .Combine(Catalogs);
    }

    sealed record CatalogsInfoNone;
}
