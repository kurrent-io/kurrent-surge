using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Kurrent.Surge.Generators.Validation;

[Generator(LanguageNames.CSharp)]
public sealed class SelfValidatingGenerator : IIncrementalGenerator {
    const string AttributeMetadataName = "Kurrent.Surge.Validation.SelfValidatingAttribute";

    // The attribute is a real type in Kurrent.Surge.Core rather than emitted per compilation:
    // emitting it gave every downstream assembly its own copy, and CS0436 fired once per annotated
    // type where the two definitions met.

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        // Widened to TypeDeclarationSyntax so that a record or struct reaches the transform and is
        // refused with a diagnostic, rather than falling through the filter and leaving the user's
        // partial properties unimplemented.
        var provider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeMetadataName,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, token) => Model.From(ctx, token))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(provider, static (ctx, model) => {
            foreach (var diagnostic in model!.Diagnostics)
                ctx.ReportDiagnostic(diagnostic.ToDiagnostic());

            if (model.Diagnostics.Any(d => d.IsError))
                return;

            ctx.AddSource($"{model.HintName}.g.cs", SourceText.From(Emitter.Emit(model), Encoding.UTF8));
        });
    }
}

#region ->> Model <<-

enum Shape { Scalar, Collection, Child }

static class Formats {
    /// <summary>
    /// Fully-qualified, and carrying the nullable-reference annotation. The default
    /// <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/> drops the <c>?</c>, which makes the
    /// generated half disagree with the defining declaration on nullability.
    /// </summary>
    public static readonly SymbolDisplayFormat NullableAware = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
}

readonly record struct PropertyModel(string Name, string Type, Shape Shape, string ElementType, string Accessibility);

readonly record struct DiagnosticModel(
    string Id, string Title, string Message, bool IsError,
    string FilePath, int Start, int Length, LinePositionSpan Lines
) {
    public Diagnostic ToDiagnostic() {
        var descriptor = new DiagnosticDescriptor(
            Id, Title, Message, "Kurrent.Validation",
            IsError ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        // The line span must be carried alongside the character span: Location.Create builds no
        // line map of its own, so a default LinePositionSpan puts every squiggle on line 1.
        var location = FilePath.Length == 0
            ? Location.None
            : Location.Create(FilePath, new TextSpan(Start, Length), Lines);

        return Diagnostic.Create(descriptor, location);
    }
}

sealed record Model(
    string Namespace,
    string TypeName,
    bool IsSealed,
    ImmutableArray<string> ContainingTypes,
    ImmutableArray<PropertyModel> Properties,
    bool HasConfigureRules,
    ImmutableArray<DiagnosticModel> Diagnostics
) {
    public string HintName {
        get {
            var parts = ContainingTypes.Append(TypeName);
            var path  = string.Join(".", parts);

            return Namespace.Length == 0 ? path : $"{Namespace}.{path}";
        }
    }

    public bool Equals(Model? other) =>
        other is not null
        && Namespace == other.Namespace
        && TypeName == other.TypeName
        && IsSealed == other.IsSealed
        && HasConfigureRules == other.HasConfigureRules
        && ContainingTypes.SequenceEqual(other.ContainingTypes)
        && Properties.SequenceEqual(other.Properties)
        && Diagnostics.SequenceEqual(other.Diagnostics);

    // Every field the model carries participates, so two shapes that differ only in a property
    // never collide and never share a cache entry.
    public override int GetHashCode() {
        var hash = Namespace.GetHashCode() ^ TypeName.GetHashCode() ^ IsSealed.GetHashCode();

        foreach (var containing in ContainingTypes)
            hash = (hash * 397) ^ containing.GetHashCode();

        foreach (var property in Properties)
            hash = (hash * 397) ^ property.GetHashCode();

        foreach (var diagnostic in Diagnostics)
            hash = (hash * 397) ^ diagnostic.GetHashCode();

        return hash;
    }

    public static Model? From(GeneratorAttributeSyntaxContext context, System.Threading.CancellationToken token) {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticModel>();
        var anchor      = (TypeDeclarationSyntax)context.TargetNode;

        // The generated half declares `partial class`, and the transplanted plumbing puts a mutable
        // cache field on the type — neither is sound for a record or a struct.
        if (anchor is not ClassDeclarationSyntax) {
            diagnostics.Add(Diag("KVAL005", "Only classes are supported",
                $"'{symbol.Name}' is a {DescribeKind(anchor)}; [SelfValidating] supports classes only.",
                true, anchor.Identifier));

            return Empty(symbol, diagnostics);
        }

        // The generated accessors use the `field` keyword, which is C# 14. The pinned Roslyn has no
        // LanguageVersion.CSharp14 constant yet, so compare the underlying value (C# 13 is 1300).
        const int CSharp14 = 1400;

        if (context.SemanticModel.Compilation is CSharpCompilation csharp
            && (int)csharp.LanguageVersion.MapSpecifiedToEffectiveVersion() < CSharp14)
            diagnostics.Add(Diag("KVAL006", "C# 14 required",
                $"[SelfValidating] on '{symbol.Name}' requires C# 14 or later.", true, anchor.Identifier));

        // A user-declared PropertyChanged would collide with the transplanted one.
        if (symbol.Interfaces.Any(i => i.ToDisplayString() == "System.ComponentModel.INotifyPropertyChanged"))
            diagnostics.Add(Diag("KVAL003", "Duplicate INotifyPropertyChanged",
                $"'{symbol.Name}' already implements INotifyPropertyChanged; [SelfValidating] provides it.",
                true, anchor.Identifier));

        // A nested type must be re-opened through its containing types, each of which has to be
        // partial for the generated half to attach to it.
        var containing = ImmutableArray.CreateBuilder<string>();

        for (var outer = symbol.ContainingType; outer is not null; outer = outer.ContainingType) {
            containing.Insert(0, outer.Name);

            var outerIsPartial = outer.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(token))
                .OfType<TypeDeclarationSyntax>()
                .Any(d => d.Modifiers.Any(SyntaxKind.PartialKeyword));

            if (!outerIsPartial)
                diagnostics.Add(Diag("KVAL007", "Containing type must be partial",
                    $"'{symbol.Name}' is nested in '{outer.Name}', which must also be declared partial.",
                    true, anchor.Identifier));
        }

        // Every partial declaration, not just the one carrying the attribute: options types are
        // routinely split across files, and properties or rules can live in any of them.
        var declarations = symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(token))
            .OfType<ClassDeclarationSyntax>()
            .ToArray();

        foreach (var declaration in declarations) {
            if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                diagnostics.Add(Diag("KVAL001", "Type must be partial",
                    $"'{symbol.Name}' is [SelfValidating] and must be declared partial.", true, declaration.Identifier));
        }

        var properties = ImmutableArray.CreateBuilder<PropertyModel>();

        // The rule bodies as text, so a property can be asked whether anything validates it.
        var ruleText = string.Concat(declarations
            .SelectMany(d => d.Members.OfType<MethodDeclarationSyntax>())
            .Where(m => m.Identifier.ValueText == "ConfigureRules")
            .Select(m => m.ToString()));

        foreach (var member in declarations.SelectMany(d => d.Members.OfType<PropertyDeclarationSyntax>())) {
            token.ThrowIfCancellationRequested();

            if (!member.Modifiers.Any(SyntaxKind.PartialKeyword)) {
                // A settable property the generator does not own cannot invalidate on its own, so a
                // rule that reads it can go stale. Only worth reporting when a rule actually does —
                // a property nothing validates is free to change without discarding anything. A
                // hand-written setter can opt back in by calling OnPropertyChanged itself.
                var setter = member.AccessorList?.Accessors
                    .FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));

                if (setter is not null
                    && !setter.ToString().Contains("OnPropertyChanged")
                    && ruleText.Contains($".{member.Identifier.ValueText}"))
                    diagnostics.Add(Diag("KVAL008", "Validated property does not invalidate",
                        $"'{member.Identifier.ValueText}' has a validation rule but is settable and not partial, "
                        + "so changing it will not discard the cached verdict. Declare it partial, or call "
                        + "OnPropertyChanged from its setter.",
                        false, member.Identifier));

                continue;
            }

            if (context.SemanticModel.Compilation
                    .GetSemanticModel(member.SyntaxTree)
                    .GetDeclaredSymbol(member, token) is not IPropertySymbol property)
                continue;

            // A partial property the generator implements must be the *defining* half.
            if (property.PartialImplementationPart is not null)
                continue;

            var (shape, element) = Classify(property.Type);

            if (shape == Shape.Collection && IsMutableReference(((INamedTypeSymbol)property.Type).TypeArguments[0]))
                diagnostics.Add(Diag("KVAL004", "Mutable collection element",
                    $"Element type '{element}' of '{property.Name}' is mutable; changes made to an element in place will not invalidate validation.",
                    false, member.Identifier));

            properties.Add(new PropertyModel(
                property.Name,
                property.Type.ToDisplayString(Formats.NullableAware),
                shape,
                element,
                // Both halves of a partial member must agree on accessibility, so it is carried
                // from the declaration rather than assumed public.
                SyntaxFacts.GetText(property.DeclaredAccessibility)));
        }

        var hasRules = declarations
            .SelectMany(d => d.Members.OfType<MethodDeclarationSyntax>())
            .Any(m => m.Identifier.ValueText == "ConfigureRules");

        if (!hasRules)
            diagnostics.Add(Diag("KVAL002", "No rules configured",
                $"'{symbol.Name}' declares no ConfigureRules method; Validate() will always succeed.",
                false, anchor.Identifier));

        return new Model(
            symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString(),
            symbol.Name,
            symbol.IsSealed,
            containing.ToImmutable(),
            properties.ToImmutable(),
            hasRules,
            diagnostics.ToImmutable());

        static Model Empty(INamedTypeSymbol symbol, ImmutableArray<DiagnosticModel>.Builder diagnostics) =>
            new(symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString(),
                symbol.Name, symbol.IsSealed, [], [], false, diagnostics.ToImmutable());

        static string DescribeKind(TypeDeclarationSyntax declaration) => declaration switch {
            RecordDeclarationSyntax => "record",
            StructDeclarationSyntax => "struct",
            _                       => "type"
        };

        static DiagnosticModel Diag(string id, string title, string message, bool isError, SyntaxToken token) {
            var location = token.GetLocation();
            var span     = location.SourceSpan;

            return new DiagnosticModel(
                id, title, message, isError,
                token.SyntaxTree?.FilePath ?? "",
                span.Start, span.Length,
                location.GetLineSpan().Span);
        }
    }

    static (Shape, string) Classify(ITypeSymbol type) {
        if (type is INamedTypeSymbol { IsGenericType: true } named) {
            var definition = named.ConstructedFrom.ToDisplayString();

            if (definition is "System.Collections.Generic.IList<T>"
                           or "System.Collections.Generic.ICollection<T>"
                           or "System.Collections.Generic.IReadOnlyList<T>")
                return (Shape.Collection, named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        if (HasSelfValidatingAttribute(type))
            return (Shape.Child, "");

        return (Shape.Scalar, "");
    }

    static bool HasSelfValidatingAttribute(ITypeSymbol type) =>
        type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Kurrent.Surge.Validation.SelfValidatingAttribute");

    // A reference type whose state can change in place: it has a settable public property.
    static bool IsMutableReference(ITypeSymbol type) =>
        type.IsReferenceType
        && type.SpecialType != SpecialType.System_String
        && type.GetMembers().OfType<IPropertySymbol>().Any(p =>
            p.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false });
}

#endregion

#region ->> Emitter <<-

static class Emitter {
    /// <summary>
    /// Members of the embedded plumbing that the generator supplies per type instead.
    /// </summary>
    static readonly string[] SkippedMembers = ["__RunValidation"];

    /// <summary>
    /// The plumbing is identical for every annotated type, so it is read and parsed once per
    /// compiler host rather than once per type per keystroke.
    /// </summary>
    static readonly Lazy<ClassDeclarationSyntax> Core = new(static () => {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SelfValidatingCore.cs");
        using var reader = new System.IO.StreamReader(stream!);

        return CSharpSyntaxTree
            .ParseText(reader.ReadToEnd())
            .GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First();
    }, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    public static string Emit(Model model) {
        var writer = new StringBuilder();

        writer.AppendLine("// <auto-generated/>");
        writer.AppendLine("#nullable enable");
        writer.AppendLine();

        if (model.Namespace.Length > 0) {
            writer.Append("namespace ").Append(model.Namespace).AppendLine(";");
            writer.AppendLine();
        }

        // A nested type is re-opened through its containing types; each wrapper adds a level.
        foreach (var containing in model.ContainingTypes)
            writer.Append("partial class ").Append(containing).AppendLine(" {");

        writer
            .Append("partial class ").Append(model.TypeName)
            .AppendLine(" : global::Kurrent.Surge.Validation.ISelfValidating, global::System.ComponentModel.INotifyPropertyChanged {");

        EmitTransplantedCore(writer, model.IsSealed);
        EmitValidator(writer, model);

        foreach (var property in model.Properties)
            EmitProperty(writer, property);

        writer.AppendLine("}");

        foreach (var _ in model.ContainingTypes)
            writer.AppendLine("}");

        return writer.ToString();
    }

    /// <summary>
    /// Copies the members of the embedded plumbing into the type. Following the transitive-members
    /// approach: no base class, so the type keeps its inheritance slot.
    /// </summary>
    static void EmitTransplantedCore(StringBuilder writer, bool isSealed) {
        foreach (var member in Core.Value.Members) {
            if (Name(member) is { } name && SkippedMembers.Contains(name))
                continue;

            var emitted = member;

            // A sealed type cannot have protected or virtual members.
            if (isSealed) {
                var modifiers = emitted.Modifiers;

                for (var i = 0; i < modifiers.Count; i++) {
                    if (modifiers[i].IsKind(SyntaxKind.ProtectedKeyword))
                        modifiers = modifiers.Replace(modifiers[i], SyntaxFactory.Token(SyntaxKind.PrivateKeyword));
                }

                modifiers = SyntaxFactory.TokenList(modifiers.Where(m =>
                    !m.IsKind(SyntaxKind.VirtualKeyword) && !m.IsKind(SyntaxKind.AbstractKeyword)));

                emitted = emitted.WithModifiers(modifiers);
            }

            // NormalizeWhitespace has no idea the member is being nested one level, so indent here.
            foreach (var line in emitted.NormalizeWhitespace().ToFullString().Split('\n'))
                writer.Append("    ").AppendLine(line.TrimEnd('\r'));

            writer.AppendLine();
        }

        static string? Name(MemberDeclarationSyntax member) => member switch {
            MethodDeclarationSyntax m   => m.Identifier.ValueText,
            PropertyDeclarationSyntax p => p.Identifier.ValueText,
            _                           => null
        };
    }

    static void EmitValidator(StringBuilder writer, Model model) {
        writer
            .Append("    public sealed class Validator : global::Kurrent.Surge.Validation.Validator<Validator, ")
            .Append(model.TypeName).AppendLine("> {");

        writer.AppendLine("        public Validator() => ConfigureRules(this);");
        writer.AppendLine("    }");
        writer.AppendLine();

        writer.AppendLine("    static partial void ConfigureRules(Validator rules);");
        writer.AppendLine("    static readonly Validator __rules = new();");
        writer.AppendLine();
        writer.AppendLine("    private global::Kurrent.Surge.Validation.ValidationResult __RunValidation() => __rules.Validate(this);");
        writer.AppendLine();
    }

    static void EmitProperty(StringBuilder writer, PropertyModel property) {
        writer
            .Append("    ").Append(property.Accessibility)
            .Append(" partial ").Append(property.Type)
            .Append(' ').Append(property.Name).AppendLine(" {");

        switch (property.Shape) {
            case Shape.Collection:
                // Wrapped on first read rather than only on set: a property initializer assigns the
                // backing field directly, so `= []` would otherwise install a plain list whose
                // mutations are invisible — a silent wrong answer rather than a compile error.
                writer
                    .Append("        get => field is global::System.Collections.ObjectModel.ObservableCollection<")
                    .Append(property.ElementType)
                    .AppendLine("> ? field : (field = __Observe(field));");
                writer.AppendLine("        set {");
                writer.AppendLine("            if (global::System.Object.ReferenceEquals(field, value)) return;");
                writer.AppendLine("            __Detach(field);");
                writer.AppendLine("            field = __Observe(value);");
                writer.Append("            OnPropertyChanged(nameof(").Append(property.Name).AppendLine("));");
                writer.AppendLine("        }");
                break;

            case Shape.Child:
                // Attached on every read, not only on assignment: an initializer such as
                // `= Foo.Default` writes the backing field directly, so a subscription made only in
                // the setter would never happen. __Attach is idempotent, so repeating it is safe.
                writer.Append("        get => __Attach(field ??= new ").Append(property.Type).AppendLine("());");
                writer.AppendLine("        set {");
                writer.AppendLine("            if (global::System.Object.ReferenceEquals(field, value)) return;");
                writer.AppendLine("            __DetachChild(field);");
                writer.AppendLine("            field = __Attach(value);");
                writer.Append("            OnPropertyChanged(nameof(").Append(property.Name).AppendLine("));");
                writer.AppendLine("        }");
                break;

            default:
                writer.AppendLine("        get => field;");
                writer.AppendLine("        set {");
                writer.Append("            if (global::System.Collections.Generic.EqualityComparer<").Append(property.Type)
                      .AppendLine(">.Default.Equals(field, value)) return;");
                writer.AppendLine("            field = value;");
                writer.Append("            OnPropertyChanged(nameof(").Append(property.Name).AppendLine("));");
                writer.AppendLine("        }");
                break;
        }

        writer.AppendLine("    }");
        writer.AppendLine();
    }
}

#endregion
