using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Kurrent.Testkit.TUnit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
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

    /// <summary>The MSBuild properties the generator reads, supplied without an MSBuild build.</summary>
    sealed class FakeOptions(Dictionary<string, string> values) : AnalyzerConfigOptions {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }

    sealed class FakeOptionsProvider(Dictionary<string, string> values) : AnalyzerConfigOptionsProvider {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new FakeOptions(values);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)          => GlobalOptions;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)  => GlobalOptions;
    }

    static GeneratorDriverRunResult Run(params (string Path, string Content)[] schemas) =>
        RunWith([], schemas);

    static GeneratorDriverRunResult RunWith(
        Dictionary<string, string> properties, params (string Path, string Content)[] schemas) {
        var compilation = CSharpCompilation.Create(
            "AvroCompilation",
            [],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = schemas
            .Select(s => (AdditionalText)new InMemorySchema(s.Path, s.Content))
            .ToImmutableArray();

        return CSharpGeneratorDriver
            .Create(
                [new AvroCodecGenerator().AsSourceGenerator()],
                texts,
                optionsProvider: new FakeOptionsProvider(properties))
            .RunGenerators(compilation)
            .GetRunResult();
    }

    /// <summary>An MSBuild property as the generator sees it.</summary>
    static Dictionary<string, string> Property(string name, string value) =>
        new() { [$"build_property.{name}"] = value };

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

    /// <summary>
    /// One illegal name per declaring surface, paired with the fragment of the message that identifies it.
    /// Asserting the fragment is what proves the offending surface was named, rather than some other name
    /// in the same schema tripping the check first.
    /// </summary>
    public sealed class IllegalNameCases : TestCaseDataGenerator<(string Schema, string Expected)> {
        protected override IEnumerable<TestCase<(string Schema, string Expected)>> Data() {
            yield return new((
                """{ "type": "record", "name": "Order-Line", "fields": [] }""",
                "'Order-Line' is not a valid Avro name (record)"
            ), DisplayName: "record name with a dash");

            yield return new((
                """{ "type": "record", "name": "Order", "namespace": "Acme.9Messages", "fields": [] }""",
                "'9Messages' is not a valid Avro name (namespace part)"
            ), DisplayName: "namespace part starting with a digit");

            yield return new((
                """
                {
                  "type": "record",
                  "name": "Order",
                  "namespace": "Acme.Messages",
                  "fields": [ { "name": "Order Id", "type": "string" } ]
                }
                """,
                "'Order Id' is not a valid Avro name (field of 'Acme.Messages.Order')"
            ), DisplayName: "field name with a space");

            yield return new((
                """
                {
                  "type": "record",
                  "name": "Order",
                  "fields": [
                    { "name": "State", "type": { "type": "enum", "name": "Status", "symbols": ["In-Transit"] } }
                  ]
                }
                """,
                "'In-Transit' is not a valid Avro name (symbol of enum 'Status')"
            ), DisplayName: "enum symbol with a dash");

            // The Avro rule is ASCII. C# would accept this identifier, so nothing downstream catches it.
            yield return new((
                """{ "type": "record", "name": "Ordré", "fields": [] }""",
                "'Ordré' is not a valid Avro name (record)"
            ), DisplayName: "record name with a non-ASCII letter");
        }
    }

    [Test]
    [IllegalNameCases]
    public async ValueTask reports_KAVRO001_for_a_name_the_avro_specification_forbids(string schema, string expected) {
        // Arrange & Act — an illegal name otherwise reaches the compiler as a malformed C# identifier,
        // inside generated code the consumer cannot edit.
        var result = Run(("/schemas/Illegal.avsc", schema));

        // Assert
        var diagnostic = result.Diagnostics.Single();

        await Assert.That(diagnostic.Id).IsEqualTo("KAVRO001");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.GetMessage()).Contains(expected);
        await Assert.That(SourceOf(result)).DoesNotContain("AvroCodec");
    }

    [Test]
    public async ValueTask accepts_a_name_that_is_legal_avro_but_unusual_in_csharp() {
        // Arrange — a leading underscore and embedded digits are legal Avro and must survive the check.
        // Rejecting them would be the same defect in the opposite direction.
        var schema =
            """
            {
              "type": "record",
              "name": "_Order2",
              "namespace": "_acme.m9",
              "fields": [ { "name": "_id2", "type": "string" } ]
            }
            """;

        // Act
        var result = Run(("/schemas/Underscore.avsc", schema));

        // Assert
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(SourceOf(result)).Contains("typeof(global::_acme.m9._Order2)");
    }

    #region ->> Generator options <<-

    const string DocumentedSchema =
        """
        {
          "type": "record",
          "name": "Shipment",
          "namespace": "Acme.Messages",
          "doc": "A shipment leaving the depot.",
          "fields": [ { "name": "Reference", "type": "string", "doc": "The carrier's tracking code." } ]
        }
        """;

    const string NanosSchema =
        """
        {
          "type": "record",
          "name": "Reading",
          "namespace": "Acme.Messages",
          "fields": [ { "name": "Taken", "type": { "type": "long", "logicalType": "timestamp-nanos" } } ]
        }
        """;

    [Test]
    public async ValueTask emits_public_types_by_default() {
        // Arrange & Act
        var source = SourceOf(Run(("/schemas/Order.avsc", ValidSchema)));

        // Assert
        await Assert.That(source).Contains("public sealed partial record Order");
    }

    [Test]
    public async ValueTask emits_internal_types_when_the_access_modifier_says_internal() {
        // Arrange & Act
        var source = SourceOf(RunWith(
            Property("KurrentAvroAccessModifier", "internal"),
            ("/schemas/Order.avsc", ValidSchema)));

        // Assert
        await Assert.That(source).Contains("internal sealed partial record Order");
        await Assert.That(source).DoesNotContain("public sealed partial record Order");
    }

    [Test]
    public async ValueTask omits_documentation_by_default() {
        // Arrange & Act — the schema declares doc on both the record and the field.
        var source = SourceOf(Run(("/schemas/Shipment.avsc", DocumentedSchema)));

        // Assert — asserted on the comment form rather than the bare text, because the same words appear
        // in SchemaJson wherever that is emitted.
        await Assert.That(source).DoesNotContain("/// A shipment leaving the depot.");
        await Assert.That(source).DoesNotContain("/// The carrier's tracking code.");

        // Assert — the schema text is verbatim and the documentation setting must not touch it. With
        // interop on the doc is still in SchemaJson even though no doc comment was emitted.
        var interop = SourceOf(RunWith(
            Property("KurrentAvroApacheInterop", "On"),
            ("/schemas/Shipment.avsc", DocumentedSchema)));

        await Assert.That(interop).DoesNotContain("/// A shipment leaving the depot.");
        await Assert.That(interop).Contains("A shipment leaving the depot.");
    }

    [Test]
    public async ValueTask emits_documentation_when_switched_on() {
        // Arrange & Act
        var source = SourceOf(RunWith(
            Property("KurrentAvroXmlDocs", "On"),
            ("/schemas/Shipment.avsc", DocumentedSchema)));

        // Assert
        await Assert.That(source).Contains("/// A shipment leaving the depot.");
        await Assert.That(source).Contains("/// The carrier's tracking code.");
    }

    [Test]
    public async ValueTask erases_nanosecond_timestamps_to_long_by_default() {
        // Arrange & Act — this is what Apache does with an unknown logical type over a long.
        var source = SourceOf(Run(("/schemas/Reading.avsc", NanosSchema)));

        // Assert
        await Assert.That(source).Contains("public required long Taken { get; set; }");
        await Assert.That(source).DoesNotContain("NanoInstant");
    }

    [Test]
    public async ValueTask emits_nano_instant_when_switched_on() {
        // Arrange & Act
        var source = SourceOf(RunWith(
            Property("KurrentAvroNanosecondTimestamps", "NanoInstant"),
            ("/schemas/Reading.avsc", NanosSchema)));

        // Assert — the richer type on the property, converted back where Apache reads it.
        await Assert.That(source).Contains("global::Kurrent.Surge.Codecs.Avro.NanoInstant Taken");
        await Assert.That(source).Contains("Taken.TotalNanoseconds");
    }

    const string TemporalSchema =
        """
        {
          "type": "record",
          "name": "Moment",
          "namespace": "Acme.Messages",
          "fields": [
            { "name": "Day",     "type": { "type": "int",  "logicalType": "date" } },
            { "name": "Clock",   "type": { "type": "int",  "logicalType": "time-millis" } },
            { "name": "Stamp",   "type": { "type": "long", "logicalType": "timestamp-millis" } },
            { "name": "Wall",    "type": { "type": "long", "logicalType": "local-timestamp-millis" } },
            { "name": "Precise", "type": { "type": "long", "logicalType": "timestamp-nanos" } }
          ]
        }
        """;

    const string DurationSchema =
        """
        {
          "type": "record",
          "name": "Window",
          "namespace": "Acme.Messages",
          "fields": [
            { "name": "Span", "type": { "type": "fixed", "name": "Span12", "size": 12, "logicalType": "duration" } }
          ]
        }
        """;

    [Test]
    public async ValueTask maps_temporal_types_to_the_bcl_by_default() {
        // Arrange & Act — the type distinguishes an instant from a zone-free reading, which is the
        // distinction the Avro specification itself draws.
        var source = SourceOf(RunWith(
            Property("KurrentAvroNanosecondTimestamps", "NanoInstant"),
            ("/schemas/Moment.avsc", TemporalSchema)));

        // Assert
        await Assert.That(source).Contains("global::System.DateOnly Day");
        await Assert.That(source).Contains("global::System.TimeOnly Clock");
        await Assert.That(source).Contains("global::System.DateTimeOffset Stamp");
        await Assert.That(source).Contains("global::System.DateTime Wall");
        await Assert.That(source).Contains("global::Kurrent.Surge.Codecs.Avro.NanoInstant Precise");
    }

    [Test]
    public async ValueTask maps_temporal_types_to_nodatime_when_selected() {
        // Arrange & Act — the same distinction, spelled in NodaTime's vocabulary.
        var properties = Property("KurrentAvroTimeModel", "NodaTime");

        properties["build_property.KurrentAvroNanosecondTimestamps"] = "NanoInstant";

        var source = SourceOf(RunWith(properties, ("/schemas/Moment.avsc", TemporalSchema)));

        // Assert
        await Assert.That(source).Contains("global::NodaTime.LocalDate Day");
        await Assert.That(source).Contains("global::NodaTime.LocalTime Clock");
        await Assert.That(source).Contains("global::NodaTime.Instant Stamp");
        await Assert.That(source).Contains("global::NodaTime.LocalDateTime Wall");
        await Assert.That(source).Contains("global::NodaTime.Instant Precise");
    }

    [Test]
    public async ValueTask runs_the_same_wire_arithmetic_under_both_time_models() {
        // Arrange — the NodaTime model converts to the BCL type and then runs the identical conversion,
        // so the two cannot drift apart against Apache. This pins that, since a second implementation is
        // exactly what would rot.
        var properties = Property("KurrentAvroTimeModel", "NodaTime");

        // Act
        var source = SourceOf(RunWith(properties, ("/schemas/Moment.avsc", TemporalSchema)));

        // Assert — the NodaTime value is converted, then the same expression the BCL model emits.
        await Assert.That(source).Contains(".ToDateOnly().DayNumber - UnixEpochDay");
        await Assert.That(source).Contains(".ToTimeOnly().ToTimeSpan().TotalMilliseconds");
        await Assert.That(source).Contains(".ToDateTimeOffset().UtcDateTime - UnixEpoch");
    }

    [Test]
    public async ValueTask erases_duration_without_the_nodatime_model() {
        // Arrange & Act — no BCL type carries months, days and milliseconds together, so the annotation
        // is dropped and the field stays the fixed, which is what the specification asks for.
        var source = SourceOf(Run(("/schemas/Window.avsc", DurationSchema)));

        // Assert
        await Assert.That(source).Contains("Span12 Span");
        await Assert.That(source).DoesNotContain("Period");
        await Assert.That(source).DoesNotContain("WriteDuration");
    }

    [Test]
    public async ValueTask maps_duration_to_period_under_the_nodatime_model() {
        // Arrange & Act
        var source = SourceOf(RunWith(
            Property("KurrentAvroTimeModel", "NodaTime"),
            ("/schemas/Window.avsc", DurationSchema)));

        // Assert
        await Assert.That(source).Contains("global::NodaTime.Period Span");
        await Assert.That(source).Contains("WriteDuration");
        await Assert.That(source).Contains("ReadDuration");
    }

    [Test]
    public async ValueTask reports_KAVRO004_for_an_unrecognised_option_value() {
        // Arrange & Act — a typo would otherwise leave the generated types public, which is the opposite
        // of what was asked for and invisible until someone reads the generated code.
        var result = RunWith(
            Property("KurrentAvroAccessModifier", "internl"),
            ("/schemas/Order.avsc", ValidSchema));

        // Assert
        var diagnostic = result.Diagnostics.Single();

        await Assert.That(diagnostic.Id).IsEqualTo("KAVRO004");
        await Assert.That(diagnostic.GetMessage()).Contains("KurrentAvroAccessModifier=internl");
        await Assert.That(diagnostic.GetMessage()).Contains("Internal");
        await Assert.That(SourceOf(result)).Contains("public sealed partial record Order");
    }

    #endregion // Generator options

    [Test]
    public async ValueTask reports_KAVRO001_when_one_file_declares_a_name_twice() {
        // Arrange — copy-and-paste instead of declaring once and referring to the name afterwards. Avro
        // allows one definition per fullname; unchecked, the type emitter takes the last declaration and
        // the codec emitter the first, so the codec reads a member the emitted type does not have.
        var schema =
            """
            {
              "type": "record",
              "name": "Order",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "A", "type": { "type": "record", "name": "Dup",
                    "fields": [ { "name": "X", "type": "string" } ] } },
                { "name": "B", "type": { "type": "record", "name": "Dup",
                    "fields": [ { "name": "Y", "type": "int" } ] } }
              ]
            }
            """;

        // Act
        var result = Run(("/schemas/Duplicate.avsc", schema));

        // Assert
        var diagnostic = result.Diagnostics.Single();

        await Assert.That(diagnostic.Id).IsEqualTo("KAVRO001");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.GetMessage()).Contains("Acme.Messages.Dup");
        await Assert.That(SourceOf(result)).DoesNotContain("AvroCodec");
    }

    [Test]
    public async ValueTask accepts_a_name_referred_to_after_it_is_declared() {
        // Arrange — the correct form of the schema above: declare once, then name it. This is the only
        // way to share a type within a file, so rejecting it would break ordinary schemas.
        var schema =
            """
            {
              "type": "record",
              "name": "Order",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "A", "type": { "type": "record", "name": "Dup",
                    "fields": [ { "name": "X", "type": "string" } ] } },
                { "name": "B", "type": "Dup" }
              ]
            }
            """;

        // Act
        var result = Run(("/schemas/Shared.avsc", schema));

        // Assert
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(Regex.Matches(SourceOf(result), "partial record Dup").Count).IsEqualTo(1);
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
