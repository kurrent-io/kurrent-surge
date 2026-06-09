using System.Diagnostics.CodeAnalysis;
using System.Text;
using Kurrent.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using TypeKind = Microsoft.CodeAnalysis.TypeKind;

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
    const string ClientNamespace             = "Kurrent.Client";
    const string MessageCatalogAttributeName = "MessageCatalogAttribute";
    const string MessageTypeAttributeName    = "MessageTypeAttribute";
    const string MessageCatalogTypeName      = "MessageCatalog";
    const string DefaultCatalogKey           = "Default";

    const string CustomMessageCatalogAttributeMetadataName = "Kurrent.Client.Schema.CustomMessageCatalogAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        // Assembly-level attributes + whole-assembly scans require the full compilation, so
        // this pipeline cannot use ForAttributeWithMetadataName. The scan re-runs on every
        // compilation change, but the extracted model is equatable: when the catalog set is
        // unchanged, the output step is cached and nothing is regenerated.
        var model = context.CompilationProvider.Select(static (compilation, ct) => Extract(compilation, ct));

        // Hand-authored catalogs: [CustomMessageCatalog] on a MessageCatalog subclass. The fast
        // attribute path hands us the target symbol (and its location, for diagnostics) directly.
        var customCatalogs = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CustomMessageCatalogAttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => ExtractCustom(ctx))
            .Collect();

        context.RegisterSourceOutput(model.Combine(customCatalogs), static (spc, pair) => {
            var (info, customs) = pair;

            // Fail loud: a [CustomMessageCatalog] that isn't a usable catalog is almost always an
            // authoring mistake — surface it at compile time rather than silently dropping it.
            foreach (var custom in customs) {
                if (!custom.IsValid)
                    spc.ReportDiagnostic(Diagnostic.Create(CustomCatalogInvalid, custom.Location, custom.TypeName));
            }

            var validCustoms = customs
                .Where(c => c.IsValid)
                .OrderBy(c => c.TypeName, StringComparer.Ordinal)
                .ToArray();

            if (!info.HasCatalogs && validCustoms.Length == 0)
                return;

            try {
                foreach (var file in GenerateFiles(info, validCustoms))
                    spc.AddSource(file.HintName, SourceText.From(file.Source, Encoding.UTF8));
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

    static readonly DiagnosticDescriptor CustomCatalogInvalid = new(
        id: "KURRENT521",
        title: "Invalid custom message catalog",
        messageFormat: "[CustomMessageCatalog] type '{0}' must be a non-abstract class deriving from MessageCatalog with an accessible parameterless constructor",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    static CatalogsInfo Extract(Compilation compilation, CancellationToken ct) {
        // key → (registration identity → registration); same key declared twice merges.
        var catalogs    = new Dictionary<string, Dictionary<(string, string?, string?), RegistrationInfo>>(StringComparer.Ordinal);
        var prefixScans = new List<PrefixScan>();

        // Keys whose [assembly: MessageCatalog(..., AlwaysActive = true)] marks the catalog
        // always-active. Per-key: any always-active declaration wins.
        var alwaysActiveKeys = new HashSet<string>(StringComparer.Ordinal);

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

                if (attribute.NamedArguments.FirstOrDefault(a => a.Key == "AlwaysActive").Value.Value is true)
                    alwaysActiveKeys.Add(key);

                // The constraint can come from the generic MessageCatalogAttribute<T> (type arg) or
                // the non-generic MustImplement property; the generic form wins when both are given.
                // A generic attribute cannot push its type parameter into the property the generator
                // reads, so the two are distinct metadata sources unified here.
                var mustImplement = (attrClass.Arity == 1 ? attrClass.TypeArguments[0] as INamedTypeSymbol : null)
                                 ?? attribute.NamedArguments.FirstOrDefault(a => a.Key == "MustImplement").Value.Value as INamedTypeSymbol;

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

        var rootNamespace = compilation.AssemblyName ?? "Kurrent.Generated";
        var assemblyId    = Sanitize(compilation.AssemblyName ?? "Assembly");

        if (catalogs.Count == 0)
            // No attribute-declared catalogs — still return the assembly identity so any
            // [CustomMessageCatalog] subclasses (combined downstream) can be emitted under
            // this assembly's provider.
            return new CatalogsInfo(HasCatalogs: false, rootNamespace, assemblyId, []);

        // Sorted for deterministic output — keeps the incremental cache and the emitted
        // source stable across builds regardless of symbol enumeration order.
        var ordered = catalogs
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => new CatalogInfo(
                c.Key,
                c.Value.Values
                    .OrderBy(r => r.TypeName, StringComparer.Ordinal)
                    .ThenBy(r => r.SchemaName, StringComparer.Ordinal)
                    .ToArray(),
                alwaysActiveKeys.Contains(c.Key)))
            .ToArray();

        return new CatalogsInfo(HasCatalogs: true, rootNamespace, assemblyId, ordered);

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

    static CustomCatalogInfo ExtractCustom(GeneratorAttributeSyntaxContext ctx) {
        var type     = (INamedTypeSymbol)ctx.TargetSymbol;
        var location = type.Locations.FirstOrDefault() ?? Location.None;

        var valid = type is { TypeKind: TypeKind.Class, IsAbstract: false, IsStatic: false }
                 && DerivesFromMessageCatalog(type)
                 && HasAccessibleParameterlessConstructor(type);

        return new CustomCatalogInfo(valid, type.ToDisplayString(), location);
    }

    static bool DerivesFromMessageCatalog(INamedTypeSymbol type) {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType) {
            if (baseType.Name == MessageCatalogTypeName && baseType.ContainingNamespace?.ToDisplayString() == ClientNamespace)
                return true;
        }

        return false;
    }

    // Generated code calls `new T()`; an internal ctor is reachable because the generated provider
    // lives in the same assembly as the catalog.
    static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type) =>
        type.InstanceConstructors.Any(c =>
            c.Parameters.Length == 0 &&
            c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal);

    // Each declared catalog lands in its own file via partial classes; a single shared file holds
    // the cross-catalog aggregates (All, Get, the provider attribute, the assembly-wide extension).
    static IEnumerable<(string HintName, string Source)> GenerateFiles(CatalogsInfo info, CustomCatalogInfo[] customCatalogs) {
        yield return ("MessageCatalogs.g.cs", GenerateSharedFile(info, customCatalogs));

        foreach (var catalog in info.Catalogs)
            yield return ($"{CatalogFileStem(catalog.Key)}.g.cs", GenerateCatalogFile(info, catalog));
    }

    // "scanned" → "ScannedMessageCatalog"; a key that already ends in "MessageCatalog" is not
    // doubled (e.g. "StreamingMessageCatalog" stays "StreamingMessageCatalog").
    static string CatalogFileStem(string key) {
        var name = PropertyName(key);

        if (name.EndsWith("MessageCatalog", StringComparison.Ordinal))
            name = name[..^"MessageCatalog".Length];

        return $"{name}MessageCatalog";
    }

    static string GenerateSharedFile(CatalogsInfo info, CustomCatalogInfo[] customCatalogs) {
        var sb = new StringBuilder();

        AppendHeader(sb);

        sb.AppendLine($"[assembly: global::{info.RootNamespace}.{info.AssemblyId}MessageCatalogProviderAttribute]");
        sb.AppendLine();
        sb.AppendLine($"namespace {info.RootNamespace} {{");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Message catalogs declared by this assembly's attributes.</summary>");
        sb.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"MessageCatalogGenerator\", \"3.0.0\")]");
        sb.AppendLine("    public static partial class MessageCatalogs {");

        // All = attribute-declared catalog properties (declared in the per-catalog files), then a
        // fresh instance of each discovered [CustomMessageCatalog] subclass. Built lazily: the
        // per-catalog properties live in other partial files, so a field initializer here could run
        // before theirs — `??=` defers until first access, after every static initializer has run.
        var allItems = info.Catalogs.Select(c => PropertyName(c.Key))
            .Concat(customCatalogs.Select(c => $"new global::{c.TypeName}()"))
            .ToArray();

        sb.AppendLine("        static global::System.Collections.Generic.IReadOnlyList<global::Kurrent.Client.MessageCatalog>? _all;");
        sb.AppendLine("        public static global::System.Collections.Generic.IReadOnlyList<global::Kurrent.Client.MessageCatalog> All =>");
        sb.AppendLine($"            _all ??= [{string.Join(", ", allItems)}];");
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
        sb.AppendLine($"    public static partial class {info.AssemblyId}MessageCatalogExtensions {{");
        sb.AppendLine($"        public static global::Kurrent.Client.KurrentClientMessageOptions Use{info.AssemblyId}Catalogs(this global::Kurrent.Client.KurrentClientMessageOptions options) =>");
        sb.AppendLine($"            options.UseCatalog(global::{info.RootNamespace}.MessageCatalogs.All);");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    static string GenerateCatalogFile(CatalogsInfo info, CatalogInfo catalog) {
        var sb = new StringBuilder();

        AppendHeader(sb);

        // Partial parts of the shared types — the [GeneratedCode] attribute and XML docs live only
        // on the shared file's declarations to avoid duplicate-attribute / duplicate-doc on partials.
        sb.AppendLine($"namespace {info.RootNamespace} {{");
        sb.AppendLine();
        sb.AppendLine("    public static partial class MessageCatalogs {");
        sb.AppendLine($"        public static global::Kurrent.Client.MessageCatalog {PropertyName(catalog.Key)} {{ get; }} = new global::Kurrent.Client.MessageCatalog(\"{catalog.Key}\", {(catalog.AlwaysActive ? "true" : "false")}, [");

        foreach (var r in catalog.Registrations) {
            var name     = r.SchemaName is null ? "null" : $"\"{r.SchemaName}\"";
            var strategy = r.StrategyTypeName is null ? "null" : $"new global::{r.StrategyTypeName}()";

            sb.AppendLine($"            new(typeof(global::{r.TypeName}), {name}, {strategy}, \"{catalog.Key}\"),");
        }

        sb.AppendLine("        ]);");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("namespace Kurrent.Client {");
        sb.AppendLine();
        sb.AppendLine($"    public static partial class {info.AssemblyId}MessageCatalogExtensions {{");
        sb.AppendLine($"        public static global::Kurrent.Client.KurrentClientMessageOptions Use{PropertyName(catalog.Key)}Catalog(this global::Kurrent.Client.KurrentClientMessageOptions options) =>");
        sb.AppendLine($"            options.UseCatalog(global::{info.RootNamespace}.MessageCatalogs.{PropertyName(catalog.Key)});");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    static void AppendHeader(StringBuilder sb) {
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// This file was generated by the MessageCatalogGenerator");
        sb.AppendLine("// Changes to this file will be lost when the file is regenerated.");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
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

    // A discovered [CustomMessageCatalog] subclass. Location feeds the misuse diagnostic. The
    // subclass carries its own always-active flag via its constructor, so it isn't tracked here.
    sealed record CustomCatalogInfo(bool IsValid, string TypeName, Location Location);

    sealed record CatalogInfo(string Key, RegistrationInfo[] Registrations, bool AlwaysActive) {
        public bool Equals(CatalogInfo? other) =>
            other is not null
         && Key == other.Key
         && AlwaysActive == other.AlwaysActive
         && Registrations.AsSpan().SequenceEqual(other.Registrations.AsSpan());

        public override int GetHashCode() =>
            Kurrent.CodeAnalysis.HashCode.New.Combine(Key).Combine(AlwaysActive).Combine(Registrations);
    }

    sealed record CatalogsInfo(bool HasCatalogs, string RootNamespace, string AssemblyId, CatalogInfo[] Catalogs) {
        public bool Equals(CatalogsInfo? other) =>
            other is not null
         && HasCatalogs == other.HasCatalogs
         && RootNamespace == other.RootNamespace
         && AssemblyId == other.AssemblyId
         && Catalogs.AsSpan().SequenceEqual(other.Catalogs.AsSpan());

        public override int GetHashCode() =>
            HashCode.New
                .Combine(HasCatalogs)
                .Combine(RootNamespace)
                .Combine(AssemblyId)
                .Combine(Catalogs);
    }
}
