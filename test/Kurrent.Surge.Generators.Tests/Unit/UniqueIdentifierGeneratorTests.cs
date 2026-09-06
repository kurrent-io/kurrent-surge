using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kurrent.Surge.Generators.Tests.Unit;

/// <summary>
/// Drives <see cref="UniqueIdentifierGenerator"/> directly over hand-built compilations — the
/// generated-output behavior (minting, parsing, formatting) is covered end to end by RecordId in
/// Kurrent.Surge.Tests; these tests cover the generator itself: which types it fires for, how it
/// handles namespaces, and what it silently skips.
/// </summary>
public class UniqueIdentifierGeneratorTests {
    const string SingleTypeSource =
        """
        using Kurrent.Surge;

        namespace Acme.Widgets;

        [UniqueIdentifier]
        public readonly partial record struct WidgetId;
        """;

    [Test]
    public async ValueTask emits_full_surface_for_a_single_type() {
        // Act
        var generated = RunGenerator(SingleTypeSource);

        // Assert
        await Assert.That(generated).Contains("namespace Acme.Widgets;");
        await Assert.That(generated).Contains("readonly partial record struct WidgetId");
        await Assert.That(generated).Contains("global::Kurrent.Surge.IUniqueIdentifier");
        await Assert.That(generated).Contains("public static WidgetId New()");
        await Assert.That(generated).Contains("UniqueIdentifierJsonConverter<WidgetId>");
    }

    [Test]
    public async ValueTask emits_independent_surfaces_for_multiple_types_in_one_compilation() {
        // Arrange
        const string source =
            """
            using Kurrent.Surge;

            namespace Acme.Widgets;

            [UniqueIdentifier]
            public readonly partial record struct WidgetId;

            [UniqueIdentifier]
            public readonly partial record struct GadgetId;
            """;

        // Act
        var generated = RunGenerator(source);

        // Assert
        await Assert.That(generated).Contains("readonly partial record struct WidgetId");
        await Assert.That(generated).Contains("readonly partial record struct GadgetId");
        await Assert.That(generated).Contains("public static WidgetId New()");
        await Assert.That(generated).Contains("public static GadgetId New()");
    }

    [Test]
    public async ValueTask omits_the_namespace_declaration_for_a_global_namespace_type() {
        // Arrange
        const string source =
            """
            using Kurrent.Surge;

            [UniqueIdentifier]
            public readonly partial record struct RootId;
            """;

        // Act
        var generated = RunGenerator(source);

        // Assert
        await Assert.That(generated).Contains("readonly partial record struct RootId");
        await Assert.That(generated).DoesNotContain("namespace ");
    }

    [Test]
    public async ValueTask ignores_types_without_the_attribute() {
        // Arrange
        const string source =
            """
            namespace Acme.Widgets;

            public readonly partial record struct PlainId;
            """;

        // Act
        var generated = RunGenerator(source);

        // Assert
        await Assert.That(generated).IsEmpty();
    }

    [Test]
    public async ValueTask ignores_a_plain_struct_even_with_the_attribute() {
        // Arrange
        // [UniqueIdentifier] is [AttributeUsage(AttributeTargets.Struct)], so it compiles on a
        // plain struct — but the generation pipeline only matches TypeKind.RecordStruct, so
        // nothing is emitted. The validation pipeline catches this independently — see
        // reports_when_the_kind_is_not_a_record_struct.
        const string source =
            """
            using Kurrent.Surge;

            namespace Acme.Widgets;

            [UniqueIdentifier]
            public readonly partial struct NotARecordId { }
            """;

        // Act
        var generated = RunGenerator(source);

        // Assert
        await Assert.That(generated).IsEmpty();
    }

    [Test]
    public async ValueTask reports_the_generation_diagnostic() {
        // Arrange
        // UNIQUEID001 is DiagnosticSeverity.Hidden + isEnabledByDefault: false — an opt-in
        // confirmation diagnostic, not something that surfaces on a bare run. Enable it
        // explicitly, the way a consumer would via .editorconfig's dotnet_diagnostic severity.
        var diagnosticOptions = new Dictionary<string, ReportDiagnostic> {
            ["UNIQUEID001"] = ReportDiagnostic.Info
        };

        // Act
        var diagnostics = RunGeneratorDiagnostics(SingleTypeSource, diagnosticOptions);

        // Assert
        await Assert.That(diagnostics).Contains(d => d.Id == "UNIQUEID001");
    }

    [Test]
    public async ValueTask reports_when_the_kind_is_not_a_record_struct() {
        // Arrange
        const string source =
            """
            using Kurrent.Surge;

            namespace Acme.Widgets;

            [UniqueIdentifier]
            public readonly partial struct NotARecordId { }
            """;

        // Act
        var diagnostics = RunGeneratorDiagnostics(source, diagnosticOptions: null);

        // Assert
        await Assert.That(diagnostics).Contains(d =>
            d.Id == "UNIQUEID002" && d.GetMessage().Contains("must be a readonly partial record struct"));
    }

    [Test]
    public async ValueTask reports_when_the_record_struct_is_not_partial() {
        // Arrange
        const string source =
            """
            using Kurrent.Surge;

            namespace Acme.Widgets;

            [UniqueIdentifier]
            public readonly record struct NotPartialId;
            """;

        // Act
        var diagnostics = RunGeneratorDiagnostics(source, diagnosticOptions: null);

        // Assert
        await Assert.That(diagnostics).Contains(d =>
            d.Id == "UNIQUEID002" && d.GetMessage().Contains("must be declared partial"));
    }

    [Test]
    public async ValueTask reports_when_the_record_struct_is_not_readonly() {
        // Arrange
        const string source =
            """
            using Kurrent.Surge;

            namespace Acme.Widgets;

            [UniqueIdentifier]
            public partial record struct NotReadOnlyId;
            """;

        // Act
        var diagnostics = RunGeneratorDiagnostics(source, diagnosticOptions: null);

        // Assert
        await Assert.That(diagnostics).Contains(d =>
            d.Id == "UNIQUEID002" && d.GetMessage().Contains("must be declared readonly"));
    }

    [Test]
    public async ValueTask reports_no_validation_diagnostic_for_a_valid_declaration() {
        // Act
        var diagnostics = RunGeneratorDiagnostics(SingleTypeSource, diagnosticOptions: null);

        // Assert
        await Assert.That(diagnostics).DoesNotContain(d => d.Id == "UNIQUEID002");
    }

    // Runs the generator over the given sources and returns the concatenated generated source text.
    static string RunGenerator(params string[] sources) =>
        string.Join("\n", Run(sources, diagnosticOptions: null).GeneratedTrees.Select(t => t.ToString()));

    static IReadOnlyList<Diagnostic> RunGeneratorDiagnostics(string source, Dictionary<string, ReportDiagnostic>? diagnosticOptions) =>
        Run([source], diagnosticOptions).Diagnostics;

    static GeneratorDriverRunResult Run(string[] sources, Dictionary<string, ReportDiagnostic>? diagnosticOptions) {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();

        // Ensure the package assembly (UniqueIdentifierAttribute, IUniqueIdentifier) is referenced
        // even if it has not been touched (and thus loaded) by the test AppDomain yet.
        references.Add(MetadataReference.CreateFromFile(typeof(UniqueIdentifierAttribute).Assembly.Location));

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        if (diagnosticOptions is not null)
            options = options.WithSpecificDiagnosticOptions(diagnosticOptions);

        var compilation = CSharpCompilation.Create(
            "UniqueIdentifierCompilation",
            sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            references,
            options);

        var driver = CSharpGeneratorDriver
            .Create(new UniqueIdentifierGenerator())
            .RunGenerators(compilation);

        return driver.GetRunResult();
    }
}
