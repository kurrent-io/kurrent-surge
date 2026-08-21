using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Kurrent.Surge.Codecs.Avro.Generators.Tests.Unit;

/// <summary>
/// Drives <see cref="AvroCodecGenerator"/> over in-memory schemas and MSBuild properties.
/// </summary>
/// <remarks>
/// Shared by the codec and type generator test classes. They assert different halves of the output and
/// need the same harness to get at it.
/// </remarks>
static class AvroGeneratorHarness {
    /// <summary>An .avsc supplied to the generator without touching disk.</summary>
    sealed class InMemorySchema(string path, string content) : AdditionalText {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(content);
    }

    /// <summary>The MSBuild properties the generator reads, supplied without an MSBuild build.</summary>
    sealed class FakeOptions(Dictionary<string, string> values) : AnalyzerConfigOptions {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }

    sealed class FakeOptionsProvider(Dictionary<string, string> values) : AnalyzerConfigOptionsProvider {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new FakeOptions(values);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)         => GlobalOptions;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
    }

    public static GeneratorDriverRunResult Run(params (string Path, string Content)[] schemas) =>
        RunWith([], schemas);

    /// <remarks>
    /// Every assembly loaded into this test process, so generated code can be compiled for real rather
    /// than only searched for substrings. Without the references, the compilation cannot resolve
    /// <c>AvroBinaryEncoder</c> or <c>NodaTime.Instant</c> and every generated file looks broken.
    /// </remarks>
    static readonly ImmutableArray<MetadataReference> References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToImmutableArray();

    static CSharpCompilation Compilation() =>
        CSharpCompilation.Create(
            "AvroCompilation",
            [],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    public static GeneratorDriverRunResult RunWith(
        Dictionary<string, string> properties, params (string Path, string Content)[] schemas) =>
        DriveWith(properties, schemas).GetRunResult();

    /// <summary>The driver itself, which is what Verify serialises into a snapshot.</summary>
    public static GeneratorDriver DriveWith(
        Dictionary<string, string> properties, params (string Path, string Content)[] schemas) {
        var texts = schemas
            .Select(s => (AdditionalText)new InMemorySchema(s.Path, s.Content))
            .ToImmutableArray();

        return CSharpGeneratorDriver
            .Create(
                [new AvroCodecGenerator().AsSourceGenerator()],
                texts,
                optionsProvider: new FakeOptionsProvider(properties))
            .RunGenerators(Compilation());
    }

    /// <summary>
    /// Compiles what the generator produced and returns the compiler's errors.
    /// </summary>
    /// <remarks>
    /// The black-box assertion a source generator actually owes: this schema, with these options,
    /// produces code that builds. A substring search cannot see a type that is referenced and never
    /// emitted — that exact bug shipped once and was caught by a real compilation, not by a string.
    /// </remarks>
    public static IReadOnlyList<Diagnostic> CompileErrors(GeneratorDriverRunResult result) =>
        Compilation()
            .AddSyntaxTrees(result.GeneratedTrees)
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

    /// <summary>Runs with Apache interop switched on, which most declaration-shape assertions need.</summary>
    public static GeneratorDriverRunResult RunWithApacheInterop(params (string Path, string Content)[] schemas) =>
        RunWith(Property("KurrentAvroApacheInterop", "On"), schemas);

    /// <summary>An MSBuild property as the generator sees it.</summary>
    public static Dictionary<string, string> Property(string name, string value) =>
        new() { [$"build_property.{name}"] = value };

    public static string SourceOf(GeneratorDriverRunResult result) =>
        string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
}
