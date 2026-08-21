using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Kurrent.Surge.Codecs.Avro.Generators.Model;
using AvroJsonKind = Kurrent.Surge.Codecs.Avro.Generators.Json.JsonKind;
using AvroJsonValue = Kurrent.Surge.Codecs.Avro.Generators.Json.JsonValue;

namespace Kurrent.Surge.Codecs.Avro.Tests;

[Category("Codecs")]
[Timeout(30_000)]
public class AvroJsonSchemaTests {
    static readonly AvroJsonSchemaOptions Canonical = new() { Canonical = true };

    #region ->> Round trip <<-

    /// <summary>
    /// The round trip is the acceptance property, and it is asserted over parsed models rather than over
    /// schema text. Hoisting every named type into <c>$defs</c> legitimately changes the text, and always
    /// will.
    /// </summary>
    [Test]
    [RepositorySchemas]
    public async ValueTask round_trips_every_repository_schema(string path) {
        // Arrange
        var original = await File.ReadAllTextAsync(path);
        var expected = Render(original);

        // Act
        var reimported = AvroJsonSchema.Import(AvroJsonSchema.Export(original));

        // Assert
        await Assert.That(Render(reimported)).IsEqualTo(expected);
    }

    /// <summary>
    /// The model erases what the code generator does not emit — precision, aliases, order, custom
    /// attributes, unrecognised logical types — so a model comparison alone cannot see those facts leave.
    /// Comparing the two exported documents does, because the exporter reads them from the schema text.
    /// </summary>
    [Test]
    [RepositorySchemas]
    public async ValueTask exporting_a_reimported_schema_reproduces_the_document(string path) {
        // Arrange
        var original = await File.ReadAllTextAsync(path);
        var expected = AvroJsonSchema.Export(original, Canonical);

        // Act
        var actual = AvroJsonSchema.Export(AvroJsonSchema.Import(expected), Canonical);

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    #endregion // Round trip

    #region ->> Compliance <<-

    [Test]
    [RepositorySchemas]
    public async ValueTask exported_refs_are_legal_uri_references(string path) {
        // Arrange
        var exported = AvroJsonSchema.ExportAsJsonNode(await File.ReadAllTextAsync(path));

        // Act
        var references = Collect(exported, "$ref").ToList();

        // Assert — Uri.TryCreate with RelativeOrAbsolute accepts a space and a bare '!!!', so it asserts
        // nothing. RFC 3986 does not, and neither does the only shape this exporter emits.
        await Assert.That(references).IsNotEmpty();

        foreach (var reference in references)
            await Assert.That(IsDefsPointer(reference))
                .IsTrue()
                .Because($"'{reference}' must be '#/$defs/' followed by an Avro fullname");
    }

    /// <summary>A fragment pointer into <c>$defs</c> whose target is a legal Avro fullname.</summary>
    /// <remarks>
    /// Avro names match <c>[A-Za-z_][A-Za-z0-9_]*</c> and join with dots, so every character is legal in a
    /// URI fragment unescaped. Anything outside that set means the pointer needs escaping and does not.
    /// </remarks>
    static bool IsDefsPointer(string reference) {
        const string Prefix = "#/$defs/";

        if (!reference.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var name = reference[Prefix.Length..];

        return name.Length > 0
            && !name.StartsWith('.')
            && !name.EndsWith('.')
            && !name.Contains("..", StringComparison.Ordinal)
            && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.');
    }

    [Test]
    [RepositorySchemas]
    public async ValueTask exported_refs_all_resolve(string path) {
        // Arrange
        var exported = AvroJsonSchema.ExportAsJsonNode(await File.ReadAllTextAsync(path));
        var defs     = exported["$defs"] as JsonObject ?? new JsonObject();

        // Act
        var targets = Collect(exported, "$ref").Select(reference => reference["#/$defs/".Length..]).ToList();

        // Assert
        foreach (var target in targets)
            await Assert.That(defs.ContainsKey(target)).IsTrue().Because($"$defs has no entry '{target}'");
    }

    [Test]
    [RepositorySchemas]
    public async ValueTask disabling_annotations_removes_nothing_but_the_annotations(string path) {
        // Arrange
        var original = await File.ReadAllTextAsync(path);
        var stripped = AvroJsonSchema.ExportAsJsonNode(original, Canonical);

        Strip(stripped, key: null);

        // Act
        var portable = AvroJsonSchema.Export(original, new AvroJsonSchemaOptions { Canonical = true, Annotations = false });

        // Assert
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(portable), stripped))
            .IsTrue()
            .Because("turning annotations off must remove the annotations and nothing else");
    }

    [Test]
    [RepositorySchemas]
    public async ValueTask declares_the_2020_12_dialect(string path) {
        // Arrange
        var original = await File.ReadAllTextAsync(path);

        // Act
        var exported = AvroJsonSchema.ExportAsJsonNode(original);

        // Assert
        await Assert.That(exported["$schema"]?.GetValue<string>()).IsEqualTo(AvroJsonSchema.Dialect);
    }

    #endregion // Compliance

    #region ->> Annotation minimality <<-

    /// <summary>
    /// Gate 4. Each key is dropped from every exported schema in turn and the round trip re-run: a key
    /// whose removal still round-trips carries nothing the standard keywords do not already carry.
    /// </summary>
    /// <remarks>
    /// Run canonically, which sorts object keys. <c>properties</c> is a JSON object and JSON Schema treats
    /// it as unordered, so sorting is a legal edit that destroys field order — which is what makes
    /// <c>ordinal</c> a genuine loss rather than one that happens to survive. <c>oneOf</c> is an array and
    /// keeps its order under the same edit, which is why <c>branch</c> is the declared exemption: it guards
    /// a third-party reordering that JSON Schema considers a no-op and Avro does not.
    /// </remarks>
    [Test]
    [AnnotationKeys]
    public async ValueTask dropping_any_annotation_key_except_branch_breaks_the_round_trip(string key) {
        // Arrange
        var exempt    = key is "branch";
        var schemas   = Corpus().ToList();
        var surviving = new List<string>();

        // Act
        foreach (var path in schemas)
            if (RoundTripsWithout(key, await File.ReadAllTextAsync(path)))
                surviving.Add(Path.GetRelativePath(AppContext.BaseDirectory, path));

        var redundant = surviving.Count == schemas.Count;

        // Assert
        await Assert.That(redundant)
            .IsEqualTo(exempt)
            .Because(exempt
                ? $"'{key}' is the declared exemption and must survive; it broke on {schemas.Count - surviving.Count} schema(s)"
                : $"'{key}' carries a fact no keyword does, so at least one schema must break; all {schemas.Count} still round-tripped");
    }

    static bool RoundTripsWithout(string key, string original) {
        var expected = AvroJsonSchema.Export(original, Canonical);
        var stripped = AvroJsonSchema.ExportAsJsonNode(original, Canonical);

        Strip(stripped, key);

        try {
            return AvroJsonSchema.Export(AvroJsonSchema.Import(stripped), Canonical) == expected
                && Render(AvroJsonSchema.Import(stripped)) == Render(original);
        }
        // A key whose absence makes the document unimportable is necessary by the strongest evidence there
        // is, and this is the one failure that means exactly that. Nothing wider is caught: Export throws
        // ArgumentException when it re-parses Import's output, so catching that would let a bug in Import
        // read as proof that the key was carrying its weight.
        catch (AvroJsonSchemaException) {
            return false;
        }
    }

    #endregion // Annotation minimality

    #region ->> The facts the keys exist for <<-

    [Test]
    public async ValueTask fixed_size_survives_because_base64_length_is_not_invertible() {
        // Arrange
        var schema = Coverage();

        // Act
        var exported = AvroJsonSchema.ExportAsJsonNode(schema);
        var checksum = exported["$defs"]!["kurrent.surge.codecs.avro.tests.corpus.Checksum"]!;

        // Assert — 16, 17 and 18 bytes all encode to 24 characters, so the emitted length pins the encoded
        // size and nothing more. The literal is written down: comparing the three calls to each other would
        // hold just as well for a Base64Length that always returned zero.
        await Assert.That(AvroJsonSchema.Base64Length(16)).IsEqualTo(24);
        await Assert.That(AvroJsonSchema.Base64Length(17)).IsEqualTo(24);
        await Assert.That(AvroJsonSchema.Base64Length(18)).IsEqualTo(24);

        await Assert.That(checksum["minLength"]!.GetValue<int>()).IsEqualTo(24);
        await Assert.That(checksum["maxLength"]!.GetValue<int>()).IsEqualTo(24);
        await Assert.That(checksum["x-avro"]!["size"]!.GetValue<int>()).IsEqualTo(17);
    }

    [Test]
    public async ValueTask preserves_bytes_default_as_avro_encodes_it() {
        // Arrange
        var schema   = Coverage();
        var expected = "ÿþABCDEFGHIJKLMNO";

        // Act
        var exported   = AvroJsonSchema.ExportAsJsonNode(schema);
        var checksum   = exported["$defs"]!["kurrent.surge.codecs.avro.tests.corpus.AnnotationCoverage"]!["properties"]!["checksum"]!;
        var reimported = AvroJsonSchema.Import(exported);

        var restored = JsonNode.Parse(reimported)!["fields"]!.AsArray()
            .First(field => field!["name"]!.GetValue<string>() == "checksum")!["default"]!.GetValue<string>();

        // Assert — Avro writes the default as one code point per byte, JSON Schema writes it as base64, and
        // reading the Avro form as base64 is the bug this codec already shipped once. The base64 is written
        // down rather than computed with ToBase64: deriving it from the same function the exporter calls
        // would hold whatever that function did.
        await Assert.That(checksum["default"]!.GetValue<string>()).IsEqualTo("//5BQkNERUZHSElKS0xNTk8=");
        await Assert.That(restored).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask preserves_custom_schema_attributes() {
        // Arrange
        var schema = Coverage();

        // Act
        var exported   = AvroJsonSchema.ExportAsJsonNode(schema);
        var record     = exported["$defs"]!["kurrent.surge.codecs.avro.tests.corpus.AnnotationCoverage"]!;
        var reimported = JsonNode.Parse(AvroJsonSchema.Import(exported))!;
        var tag        = reimported["fields"]!.AsArray().First(field => field!["name"]!.GetValue<string>() == "tag")!;

        // Assert — the value and the node it landed on, not a substring anywhere in the document. An
        // attribute hoisted onto the wrong type would pass a Contains check.
        await Assert.That(record["x-avro"]!["props"]!["sourceSystem"]!.GetValue<string>()).IsEqualTo("muthur");
        await Assert.That(reimported["sourceSystem"]!.GetValue<string>()).IsEqualTo("muthur");
        await Assert.That(tag["type"]!["maxLength"]!.GetValue<int>()).IsEqualTo(32);
        await Assert.That(tag["type"]!["logicalType"]!.GetValue<string>()).IsEqualTo("varchar");
    }

    /// <remarks>
    /// The enum's own default rides the standard <c>default</c> keyword, so no <c>x-avro</c> key carries it
    /// and the minimality gate never sees it. The model has no <c>AvroEnum.Default</c> either, so the
    /// round-trip comparison cannot see it. This is the only assertion that holds it.
    /// </remarks>
    [Test]
    public async ValueTask preserves_an_enum_default() {
        // Arrange
        var schema = Coverage();

        // Act
        var exported   = AvroJsonSchema.ExportAsJsonNode(schema);
        var status     = exported["$defs"]!["kurrent.surge.codecs.avro.tests.corpus.Status"]!;
        var reimported = JsonNode.Parse(AvroJsonSchema.Import(exported))!;

        var restored = reimported["fields"]!.AsArray()
            .First(field => field!["name"]!.GetValue<string>() == "status")!["type"]!;

        // Assert
        await Assert.That(status["default"]!.GetValue<string>()).IsEqualTo("Unknown");
        await Assert.That(restored["default"]!.GetValue<string>()).IsEqualTo("Unknown");
    }

    [Test]
    public async ValueTask carries_an_unrecognised_logical_type_rather_than_dropping_it() {
        // Arrange
        var schema = Coverage();

        // Act
        var exported = AvroJsonSchema.ExportAsJsonNode(schema);
        var tag      = exported["$defs"]!["kurrent.surge.codecs.avro.tests.corpus.AnnotationCoverage"]!["properties"]!["tag"]!;

        // Assert — the specification says an unrecognised logical type is ignored, not dropped.
        await Assert.That(tag["x-avro"]!["logicalType"]!.GetValue<string>()).IsEqualTo("varchar");
    }

    [Test]
    public async ValueTask separates_the_logical_types_that_share_one_format() {
        // Arrange
        var schema = Coverage();

        // Act
        var exported   = AvroJsonSchema.ExportAsJsonNode(schema);
        var properties = exported["$defs"]!["kurrent.surge.codecs.avro.tests.corpus.AnnotationCoverage"]!["properties"]!;

        // Assert — one format, two meanings; only the annotation tells them apart.
        await Assert.That(properties["observedAt"]!["format"]!.GetValue<string>())
            .IsEqualTo(properties["recordedAt"]!["format"]!.GetValue<string>());

        await Assert.That(properties["observedAt"]!["x-avro"]!["logicalType"]!.GetValue<string>()).IsEqualTo("timestamp-micros");
        await Assert.That(properties["recordedAt"]!["x-avro"]!["logicalType"]!.GetValue<string>()).IsEqualTo("local-timestamp-micros");
    }

    #endregion // The facts the keys exist for

    #region ->> Import <<-

    [Test]
    public async ValueTask imports_a_schema_with_no_annotations() {
        // Arrange
        var original = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Schemas", "TestOrder.avsc"));
        var portable = AvroJsonSchema.Export(original, new AvroJsonSchemaOptions { Annotations = false });

        // Act
        var imported = AvroJsonSchema.Import(portable);

        // Assert — inference reproduces the structure; only the annotated form reproduces every fact.
        // Field order is asserted, not just membership: position is the whole reason x-avro.ordinal
        // exists, and this is the one test of the unannotated path.
        await Assert.That(AvroSchemaParser.TryParse(imported, out var document, out _, out _)).IsTrue();
        await Assert.That(FieldNames(document!)).IsDeepEquivalentTo(FieldNames(Parse(original)));
    }

    [Test]
    public async ValueTask import_throws_when_annotation_contradicts_type() {
        // Arrange
        var contradiction = """{"type":"string","x-avro":{"type":"float"}}""";

        // Act
        var act = () => AvroJsonSchema.Import(contradiction);

        // Assert
        var thrown = await Assert.That(act).Throws<AvroJsonSchemaException>();

        await Assert.That(thrown!.Pointer).IsEqualTo("#");
    }

    [Test]
    public async ValueTask import_throws_when_a_ref_does_not_resolve() {
        // Arrange
        var dangling = """{"$ref":"#/$defs/acme.Missing","$defs":{}}""";

        // Act
        var act = () => AvroJsonSchema.Import(dangling);

        // Assert
        await Assert.That(act).Throws<AvroJsonSchemaException>();
    }

    [Test]
    public async ValueTask import_throws_when_union_branch_indices_are_not_a_permutation() {
        // Arrange
        var corrupt = """
                      {"oneOf":[{"type":"null","x-avro":{"branch":0}},{"type":"string","x-avro":{"branch":2}}]}
                      """;

        // Act
        var act = () => AvroJsonSchema.Import(corrupt);

        // Assert — the index is the wire tag, so a contradiction throws instead of decoding wrongly.
        await Assert.That(act).Throws<AvroJsonSchemaException>();
    }

    #endregion // Import

    #region ->> Corpus <<-

    /// <summary>Every <c>.avsc</c> in the repository, copied beside the test binary by the project file.</summary>
    public sealed class RepositorySchemas : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() =>
            Corpus().Select(path => new TestCase<string>(path, DisplayName: Path.GetRelativePath(AppContext.BaseDirectory, path)));
    }

    public sealed class AnnotationKeys : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() =>
            new[] { "type", "logicalType", "ordinal", "branch", "size", "precision", "aliases", "order", "props" }
                .Select(key => new TestCase<string>(key, DisplayName: key));
    }

    static IEnumerable<string> Corpus() =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "*.avsc", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);

    static string Coverage() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Corpus", "AnnotationCoverage.avsc"));

    #endregion // Corpus

    #region ->> Helpers <<-

    static AvroSchemaDocument Parse(string schema) =>
        AvroSchemaParser.TryParse(schema, out var document, out var error, out var offset)
            ? document
            : throw new InvalidOperationException($"schema does not parse at {offset}: {error}");

    static IReadOnlyList<string> FieldNames(AvroSchemaDocument document) =>
        document.Root is AvroRecord record ? record.Fields.Select(field => field.Name).ToList() : [];

    /// <summary>Removes one <c>x-avro</c> key everywhere, or every annotation when the key is null.</summary>
    static void Strip(JsonNode? node, string? key) {
        switch (node) {
            case JsonObject o:
                if (o["x-avro"] is JsonObject annotation) {
                    if (key is null)
                        o.Remove("x-avro");
                    else {
                        annotation.Remove(key);

                        if (annotation.Count == 0) o.Remove("x-avro");
                    }
                }

                foreach (var (_, value) in o.ToList()) Strip(value, key);
                break;

            case JsonArray a:
                foreach (var item in a) Strip(item, key);
                break;
        }
    }

    static IEnumerable<string> Collect(JsonNode? node, string key) {
        switch (node) {
            case JsonObject o:
                if (o[key] is JsonValue value && value.TryGetValue<string>(out var text)) yield return text;

                foreach (var (_, child) in o)
                    foreach (var found in Collect(child, key))
                        yield return found;

                break;

            case JsonArray a:
                foreach (var item in a)
                    foreach (var found in Collect(item, key))
                        yield return found;

                break;
        }
    }

    #endregion // Helpers

    #region ->> Model rendering <<-

    /// <summary>
    /// A canonical rendering of a parsed schema, so a round-trip failure reads as a diff rather than as a
    /// boolean. The model's records compare by reference on their list members, so equality is written out
    /// here rather than taken from the synthesised <c>Equals</c>.
    /// </summary>
    static string Render(string schema) {
        var document = Parse(schema);
        var writer   = new StringBuilder();

        Render(document, document.Root, writer);

        return writer.ToString();
    }

    static void Render(AvroSchemaDocument document, AvroSchema schema, StringBuilder writer) {
        switch (schema) {
            case AvroNamedReference reference:
                writer.Append("ref(").Append(reference.FullName).Append(')');
                break;

            case AvroPrimitive primitive:
                writer.Append("prim(").Append(primitive.Kind).Append(',').Append(primitive.Logical).Append(',').Append(primitive.Scale).Append(')');
                break;

            case AvroEnum @enum:
                writer.Append("enum(").Append(@enum.FullName).Append(",doc=").Append(@enum.Doc)
                    .Append(",[").AppendJoin(',', @enum.Symbols).Append("])");
                break;

            case AvroFixed @fixed:
                writer.Append("fixed(").Append(@fixed.FullName).Append(',').Append(@fixed.Size).Append(",doc=").Append(@fixed.Doc)
                    .Append(',').Append(@fixed.Logical).Append(',').Append(@fixed.Scale).Append(')');
                break;

            case AvroRecord record:
                writer.Append("record(").Append(record.FullName).Append(",doc=").Append(record.Doc).Append("){");

                foreach (var field in record.Fields) {
                    writer.Append(field.Name).Append(",doc=").Append(field.Doc).Append(",default=");
                    Render(field.Default, writer);
                    writer.Append('=');
                    Render(document, field.Type, writer);
                    writer.Append(';');
                }

                writer.Append('}');
                break;

            case AvroArray array:
                writer.Append("array(");
                Render(document, array.Items, writer);
                writer.Append(')');
                break;

            case AvroMap map:
                writer.Append("map(");
                Render(document, map.Values, writer);
                writer.Append(')');
                break;

            case AvroUnion union:
                writer.Append("union[");

                foreach (var branch in union.Branches) {
                    Render(document, branch, writer);
                    writer.Append(',');
                }

                writer.Append(']');
                break;
        }
    }

    static void Render(AvroJsonValue? value, StringBuilder writer) {
        switch (value?.Kind) {
            case null:
                writer.Append("(none)");
                break;

            case AvroJsonKind.Object:
                writer.Append('{');

                foreach (var (key, member) in value.Members.OrderBy(member => member.Key, StringComparer.Ordinal)) {
                    writer.Append(key).Append(':');
                    Render(member, writer);
                    writer.Append(',');
                }

                writer.Append('}');
                break;

            case AvroJsonKind.Array:
                writer.Append('[');

                foreach (var item in value.Items) {
                    Render(item, writer);
                    writer.Append(',');
                }

                writer.Append(']');
                break;

            case AvroJsonKind.String:
                writer.Append('"').Append(value.Text).Append('"');
                break;

            case AvroJsonKind.Number:
                writer.Append(value.Number.ToString("R", CultureInfo.InvariantCulture));
                break;

            case AvroJsonKind.Boolean:
                writer.Append(value.Boolean);
                break;

            default:
                writer.Append("null");
                break;
        }
    }

    #endregion // Model rendering
}
