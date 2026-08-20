using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Kurrent.Surge.Codecs.Avro.Generators.Tests.Unit;

/// <summary>
/// Drives <see cref="AvroCodecGenerator"/> over hand-built compilations and in-memory .avsc files.
/// </summary>
/// <remarks>
/// The codec round-trip tests in Kurrent.Surge.Codecs.Avro.Tests prove the emitted code is correct.
/// These cover what those cannot reach: the diagnostics, their source locations, what is deliberately
/// NOT emitted, and the incremental cache.
/// </remarks>
[Category("Codecs")]
public class AvroCodecGeneratorTests {
    const string ValidSchema =
        """
        {
          "type": "record",
          "name": "Order",
          "namespace": "Acme.Messages",
          "fields": [
            { "name": "OrderId", "type": "string" },
            { "name": "Lines", "type": { "type": "array", "items": "int" } }
          ]
        }
        """;

    const string DecimalSchema =
        """
        {
          "type": "record",
          "name": "Payment",
          "namespace": "Acme.Messages",
          "fields": [
            { "name": "Amount", "type": { "type": "bytes", "logicalType": "decimal", "precision": 12, "scale": 2 } }
          ]
        }
        """;

    /// <summary>An .avsc supplied to the generator without touching disk.</summary>
    sealed class InMemorySchema(string path, string content) : AdditionalText {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(content);
    }

    static GeneratorDriverRunResult Run(params (string Path, string Content)[] schemas) {
        var compilation = CSharpCompilation.Create(
            "AvroCompilation",
            [],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = schemas
            .Select(s => (AdditionalText)new InMemorySchema(s.Path, s.Content))
            .ToImmutableArray();

        return CSharpGeneratorDriver
            .Create([new AvroCodecGenerator().AsSourceGenerator()], texts)
            .RunGenerators(compilation)
            .GetRunResult();
    }

    static string SourceOf(GeneratorDriverRunResult result) =>
        string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));

    [Test]
    public async ValueTask emits_a_codec_and_a_registration_for_a_valid_schema() {
        // Arrange & Act
        var result = Run(("/schemas/Order.avsc", ValidSchema));

        // Assert — the codec class, its entry points, and the module initializer that registers it.
        var source = SourceOf(result);

        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(source).Contains("Acme_Messages_OrderAvroCodec");
        await Assert.That(source).Contains("static void Encode_Acme_Messages_Order");
        await Assert.That(source).Contains("ModuleInitializer");
        await Assert.That(source).Contains("AvroCodecRegistry.Register");
    }

    [Test]
    public async ValueTask reports_KAVRO001_with_a_source_location_for_malformed_json() {
        // Arrange — a truncated document. The offset must survive into the diagnostic so the error is
        // navigable in an IDE rather than a bare message.
        var result = Run(("/schemas/Broken.avsc", "{ \"type\": \"record\", "));

        // Act
        var diagnostic = result.Diagnostics.Single();

        // Assert
        await Assert.That(diagnostic.Id).IsEqualTo("KAVRO001");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location).IsNotEqualTo(Location.None);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo("/schemas/Broken.avsc");
    }

    [Test]
    public async ValueTask reports_KAVRO002_when_the_root_schema_is_not_a_record() {
        // Arrange — a bare enum is a legal Avro schema but produces no codec.
        var enumSchema = """{ "type": "enum", "name": "Colour", "symbols": ["Red"] }""";

        // Act
        var result = Run(("/schemas/Colour.avsc", enumSchema));

        // Assert
        var diagnostic = result.Diagnostics.Single();

        await Assert.That(diagnostic.Id).IsEqualTo("KAVRO002");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(SourceOf(result)).DoesNotContain("AvroCodec");
    }

    [Test]
    public async ValueTask ignores_files_that_are_not_avsc() {
        // Arrange & Act
        var result = Run(("/schemas/notes.txt", "not a schema"));

        // Assert — no diagnostics and no output; a stray AdditionalFile is not this generator's business.
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(result.GeneratedTrees).IsEmpty();
    }

    [Test]
    public async ValueTask omits_decimal_helpers_from_a_schema_that_has_no_decimal() {
        // Arrange & Act — helper members are emitted per schema, so a schema without decimals must not
        // carry the decimal converters. Emitting them unconditionally and relying on the trimmer would
        // leave dead code in every generated codec.
        var source = SourceOf(Run(("/schemas/Order.avsc", ValidSchema)));

        // Assert
        await Assert.That(source).DoesNotContain("WriteDecimal");
        await Assert.That(source).DoesNotContain("ReadDecimal");
        await Assert.That(source).DoesNotContain("UnixEpoch");
    }

    [Test]
    public async ValueTask emits_decimal_helpers_only_for_a_schema_that_uses_decimal() {
        // Arrange & Act
        var source = SourceOf(Run(("/schemas/Payment.avsc", DecimalSchema)));

        // Assert — the decimal converters appear, and the unrelated epoch constant does not.
        await Assert.That(source).Contains("WriteDecimal");
        await Assert.That(source).Contains("ReadDecimal");
        await Assert.That(source).DoesNotContain("UnixEpoch");
    }

    [Test]
    public async ValueTask sizes_decoded_collections_from_the_block_count() {
        // Arrange & Act — Avro sends an element count per block; the decoder must use it instead of
        // growing the list from zero.
        var source = SourceOf(Run(("/schemas/Order.avsc", ValidSchema)));

        // Assert
        await Assert.That(source).Contains("EnsureCapacity");
    }

    [Test]
    public async ValueTask reuses_the_cached_emission_when_the_schemas_are_unchanged() {
        // Arrange — the pipeline value is emitted text precisely so this holds. Putting the parsed model
        // there instead compiles and passes every other test, then silently re-emits on every keystroke,
        // because the model is a class and compares by reference.
        var compilation = CSharpCompilation.Create(
            "AvroCompilation",
            [],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            [new AvroCodecGenerator().AsSourceGenerator()],
            [new InMemorySchema("/schemas/Order.avsc", ValidSchema)],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        // Act — a second compilation with the same schemas. A fresh syntax tree is what makes it a new
        // compilation; reusing the first one would short-circuit before the cache is consulted.
        var second = driver
            .RunGenerators(compilation)
            .RunGenerators(compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Trigger;")))
            .GetRunResult();

        // Assert
        var steps = second.Results[0].TrackedSteps["AvroSchemas"];

        var reasons = steps
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason)
            .ToList();

        await Assert.That(reasons).IsNotEmpty();
        await Assert.That(reasons.All(reason => reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)).IsTrue();
    }

    [Test]
    public async ValueTask registers_every_schema_in_one_registration_class() {
        // Arrange & Act
        var source = SourceOf(Run(
            ("/schemas/Order.avsc", ValidSchema),
            ("/schemas/Payment.avsc", DecimalSchema)));

        // Assert — one module initializer covering both types, not one class per schema.
        await Assert.That(source).Contains("typeof(global::Acme.Messages.Order)");
        await Assert.That(source).Contains("typeof(global::Acme.Messages.Payment)");
        await Assert.That(Regex.Matches(source, "class AvroGeneratedCodecRegistration").Count).IsEqualTo(1);
    }
}
