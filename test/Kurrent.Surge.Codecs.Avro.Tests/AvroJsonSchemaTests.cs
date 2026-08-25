using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kurrent.Surge.Codecs.Avro.Generators.Model;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Validation;
using AvroJsonKind = Kurrent.Surge.Codecs.Avro.Generators.Json.JsonKind;
using AvroJsonValue = Kurrent.Surge.Codecs.Avro.Generators.Json.JsonValue;

namespace Kurrent.Surge.Codecs.Avro.Tests;

[Category("Codecs")]
[Timeout(30_000)]
public class AvroJsonSchemaTests {
    static readonly AvroJsonSchemaOptions Canonical   = new() { Canonical = true };
    static readonly AvroJsonSchemaOptions Draft202012 = new() { Dialect = JsonSchemaDialect.Draft202012 };

    static readonly FrozenSet<string> Draft04Keywords = new[] {
        "$schema", "$ref", "id", "definitions", "title", "description", "default",
        "type", "enum", "allOf", "anyOf", "oneOf", "not",
        "properties", "additionalProperties", "patternProperties", "required", "dependencies",
        "maxProperties", "minProperties",
        "items", "additionalItems", "maxItems", "minItems", "uniqueItems",
        "maximum", "minimum", "exclusiveMaximum", "exclusiveMinimum", "multipleOf",
        "maxLength", "minLength", "pattern", "format"
    }.ToFrozenSet(StringComparer.Ordinal);

    #region ->> Round trip <<-

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

    [Test]
    [RepositorySchemas]
    public async ValueTask no_declared_attribute_is_lost_on_the_way_out(string path) {
        // Arrange
        var original = await File.ReadAllTextAsync(path);
        var expected = Attributes(original);

        // Act
        var actual = Attributes(AvroJsonSchema.Import(AvroJsonSchema.Export(original)));

        // Assert
        foreach (var fact in expected)
            await Assert.That(actual).Contains(fact).Because($"'{fact}' was declared and did not come back");
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
        // nothing. RFC 3986 does not, and neither does the only shape this exporter emits. A schema whose
        // root is not a named type has no $defs and no references at all, which is legal.
        if (exported["definitions"] is JsonObject) await Assert.That(references).IsNotEmpty();

        foreach (var reference in references)
            await Assert.That(IsResolvablePointer(reference))
                .IsTrue()
                .Because($"'{reference}' must be '#' or '#/definitions/' followed by an Avro fullname");

        static bool IsResolvablePointer(string reference) {
            const string Prefix = "#/definitions/";

            if (reference == "#") return true;

            if (!reference.StartsWith(Prefix, StringComparison.Ordinal)) return false;

            var name = reference[Prefix.Length..];

            return name.Length > 0
                && !name.StartsWith('.')
                && !name.EndsWith('.')
                && !name.Contains("..", StringComparison.Ordinal)
                && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.');
        }
    }

    [Test]
    [RepositorySchemas]
    public async ValueTask exported_refs_all_resolve(string path) {
        // Arrange
        var exported = AvroJsonSchema.ExportAsJsonNode(await File.ReadAllTextAsync(path));
        var defs     = exported["definitions"] as JsonObject ?? new JsonObject();

        // Act — '#' is the document root and resolves by definition, so only the container pointers are
        // checked against the container.
        var targets = Collect(exported, "$ref")
            .Where(reference => reference != "#")
            .Select(reference => reference["#/definitions/".Length..])
            .ToList();

        // Assert
        foreach (var target in targets)
            await Assert.That(defs.ContainsKey(target)).IsTrue().Because($"definitions has no entry '{target}'");
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
    public async ValueTask declares_draft_04_by_default(string path) {
        // Arrange
        var original = await File.ReadAllTextAsync(path);

        // Act
        var exported = AvroJsonSchema.ExportAsJsonNode(original);

        // Assert
        await Assert.That(exported["$schema"]?.GetValue<string>()).IsEqualTo(AvroJsonSchema.Draft04Dialect);
    }

    [Test]
    [RepositorySchemas]
    public async ValueTask the_registry_can_resolve_every_exported_schema(string path) {
        // Arrange
        var exported = AvroJsonSchema.Export(await File.ReadAllTextAsync(path));

        // Act — reference resolution is lazy, so an unresolvable schema throws here rather than returning
        // errors. Validation errors are fine; an exception means the registry cannot read the document.
        var thrown = null as Exception;

        try {
            NJsonSchemaAgent.Instance.ParseSchema(exported).Validate("{}");
        }
        catch (Exception exception) {
            thrown = exception;
        }

        // Assert
        await Assert.That(thrown).IsNull().Because(thrown?.Message ?? "");
    }

    [Test]
    [RepositorySchemas]
    public async ValueTask emits_only_draft_04_keywords(string path) {
        // Arrange
        var exported = AvroJsonSchema.ExportAsJsonNode(await File.ReadAllTextAsync(path));

        // Act
        var stray = Keywords(exported).Where(keyword => !Draft04Keywords.Contains(keyword)).ToList();

        // Assert
        await Assert.That(stray).IsEmpty().Because($"draft 4 does not define {string.Join(", ", stray)}");
    }

    [Test]
    [RepositorySchemas]
    public async ValueTask the_2020_12_dialect_hoists_into_defs_and_round_trips(string path) {
        // Arrange
        var original = await File.ReadAllTextAsync(path);

        // Act
        var exported = AvroJsonSchema.ExportAsJsonNode(original, Draft202012);

        // Assert — the two dialects differ in the declaration and the container, and in nothing else.
        await Assert.That(exported["$schema"]?.GetValue<string>()).IsEqualTo(AvroJsonSchema.Draft202012Dialect);
        await Assert.That(exported["definitions"]).IsNull();
        await Assert.That(Render(AvroJsonSchema.Import(exported))).IsEqualTo(Render(original));
    }

    static IEnumerable<string> Keywords(JsonNode? node) {
        switch (node) {
            case JsonObject o:
                foreach (var (key, value) in o) {
                    if (key.StartsWith("x-", StringComparison.Ordinal)) continue;

                    yield return key;

                    // The keys under these are names the schema author chose, not keywords.
                    var named = key is "properties" or "definitions" or "$defs";

                    foreach (var child in named ? (value as JsonObject)?.Select(p => p.Value) ?? [] : [value])
                        foreach (var found in Keywords(child))
                            yield return found;
                }

                break;

            case JsonArray a:
                foreach (var item in a)
                    foreach (var found in Keywords(item))
                        yield return found;

                break;
        }
    }

    [Test]
    public async ValueTask omitting_the_dialect_leaves_a_schema_that_still_converts() {
        // Arrange
        var original = Coverage();

        // Act
        var embedded = AvroJsonSchema.ExportAsJsonNode(original, new AvroJsonSchemaOptions { DeclareDialect = false });

        // Assert — the option exists for embedding the result in a larger document, which already declares
        // the dialect. Nothing else about the schema may change.
        await Assert.That(embedded["$schema"]).IsNull();
        await Assert.That(Render(AvroJsonSchema.Import(embedded))).IsEqualTo(Render(original));
    }

    [Test]
    [RepositorySchemas]
    public async ValueTask canonical_sorts_every_object_key(string path) {
        // Arrange
        var exported = AvroJsonSchema.ExportAsJsonNode(await File.ReadAllTextAsync(path), Canonical);

        // Act
        var unsorted = Unsorted(exported).ToList();

        // Assert — two structurally identical schemas must export byte for byte alike, which only holds
        // if every object key is ordered. Array items are never sorted; oneOf order is the union tag and
        // enum order is the symbol ordinal, so the round trip above is what proves those survive.
        await Assert.That(unsorted).IsEmpty();
    }

    static IEnumerable<string> Unsorted(JsonNode? node) {
        switch (node) {
            case JsonObject o:
                var keys = o.Select(property => property.Key).ToList();

                if (!keys.SequenceEqual(keys.OrderBy(key => key, StringComparer.Ordinal)))
                    yield return string.Join(",", keys);

                foreach (var (_, value) in o)
                    foreach (var found in Unsorted(value))
                        yield return found;

                break;

            case JsonArray a:
                foreach (var item in a)
                    foreach (var found in Unsorted(item))
                        yield return found;

                break;
        }
    }

    [Test]
    public async ValueTask the_byte_string_pattern_asserts_what_it_claims() {
        // Arrange
        var pattern = AvroJsonSchema.ByteStringPattern;

        // Assert — every byte maps to one code point in U+0000..U+00FF, and nothing above it may appear.
        await Assert.That(Regex.IsMatch("ÿþABC", pattern)).IsTrue();
        await Assert.That(Regex.IsMatch("", pattern)).IsTrue();
        await Assert.That(Regex.IsMatch("Ā", pattern)).IsFalse();
        await Assert.That(Regex.IsMatch("€", pattern)).IsFalse();
    }

    #endregion // Compliance

    #region ->> Annotation minimality <<-

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

        // Canonical on purpose: sorting object keys is a legal JSON Schema edit that destroys `properties`
        // order and leaves `oneOf` order intact, which is what makes `ordinal` a real loss and `branch` exempt.
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
    }

    #endregion // Annotation minimality

    #region ->> The facts the keys exist for <<-

    [Test]
    public async ValueTask a_fixed_asserts_its_exact_byte_count() {
        // Arrange
        var schema = Coverage();

        // Act
        var exported = AvroJsonSchema.ExportAsJsonNode(schema);
        var checksum = exported["definitions"]!["kurrent.surge.codecs.avro.tests.corpus.Checksum"]!;

        // Assert — and no x-avro.size beside it: the length is the size, so stating it twice was what the
        // minimality gate deleted.
        await Assert.That(checksum["minLength"]!.GetValue<int>()).IsEqualTo(17);
        await Assert.That(checksum["maxLength"]!.GetValue<int>()).IsEqualTo(17);
        await Assert.That(checksum["pattern"]!.GetValue<string>()).IsEqualTo(AvroJsonSchema.ByteStringPattern);
        await Assert.That(checksum["x-avro"]!["size"]).IsNull();
    }

    [Test]
    public async ValueTask preserves_bytes_default_as_avro_encodes_it() {
        // Arrange
        var schema   = Coverage();
        var expected = "ÿþABCDEFGHIJKLMNO";

        // Act
        var exported   = AvroJsonSchema.ExportAsJsonNode(schema);
        var checksum   = exported["properties"]!["checksum"]!;
        var reimported = AvroJsonSchema.Import(exported);

        var restored = JsonNode.Parse(reimported)!["fields"]!.AsArray()
            .First(field => field!["name"]!.GetValue<string>() == "checksum")!["default"]!.GetValue<string>();

        // Assert — Avro writes a bytes or fixed default as one code point per byte, and the exported schema
        // describes that same encoding, so the value crosses unchanged. Reading it as base64 is the bug
        // this codec already shipped once, and base64 would not survive this comparison.
        await Assert.That(checksum["default"]!.GetValue<string>()).IsEqualTo(expected);
        await Assert.That(restored).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask preserves_custom_schema_attributes() {
        // Arrange
        var schema = Coverage();

        // Act
        var exported   = AvroJsonSchema.ExportAsJsonNode(schema);
        var record     = exported;
        var reimported = JsonNode.Parse(AvroJsonSchema.Import(exported))!;
        var tag        = reimported["fields"]!.AsArray().First(field => field!["name"]!.GetValue<string>() == "tag")!;

        // Assert — the value and the node it landed on, not a substring anywhere in the document. An
        // attribute hoisted onto the wrong type would pass a Contains check.
        await Assert.That(record["x-avro"]!["props"]!["sourceSystem"]!.GetValue<string>()).IsEqualTo("muthur");
        await Assert.That(reimported["sourceSystem"]!.GetValue<string>()).IsEqualTo("muthur");
        await Assert.That(tag["type"]!["maxLength"]!.GetValue<int>()).IsEqualTo(32);
        await Assert.That(tag["type"]!["logicalType"]!.GetValue<string>()).IsEqualTo("varchar");
    }

    [Test]
    public async ValueTask preserves_an_enum_default() {
        // Arrange
        var schema = Coverage();

        // Act
        var exported   = AvroJsonSchema.ExportAsJsonNode(schema);
        var status     = exported["definitions"]!["kurrent.surge.codecs.avro.tests.corpus.Status"]!;
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
        var tag      = exported["properties"]!["tag"]!;

        // Assert — the specification says an unrecognised logical type is ignored, not dropped.
        await Assert.That(tag["x-avro"]!["logicalType"]!.GetValue<string>()).IsEqualTo("varchar");
    }

    [Test]
    public async ValueTask a_logical_type_is_written_as_its_base_type() {
        // Arrange
        var schema = Coverage();

        // Act
        var exported   = AvroJsonSchema.ExportAsJsonNode(schema);
        var properties = exported["properties"]!;

        // Assert
        foreach (var name in (string[])["observedAt", "recordedAt"]) {
            await Assert.That(properties[name]!["type"]!.GetValue<string>()).IsEqualTo("integer");
            await Assert.That(properties[name]!["format"]).IsNull();
        }

        await Assert.That(properties["observedAt"]!["x-avro"]!["logicalType"]!.GetValue<string>()).IsEqualTo("timestamp-micros");
        await Assert.That(properties["recordedAt"]!["x-avro"]!["logicalType"]!.GetValue<string>()).IsEqualTo("local-timestamp-micros");
    }

    [Test]
    public async ValueTask a_transcoded_payload_validates_against_the_exported_schema() {
        // Arrange
        var order    = AvroJsonWriterTests.NewComplexOrder("handle with care");
        var text     = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Schemas", "ComplexOrder.avsc"));
        var payload  = new AvroSchemaCodec().Encode(order);
        var exported = AvroJsonSchema.Export(text);

        // Act
        var transcoded = new AvroJsonWriter(text).Write(payload);
        var result     = NJsonSchemaAgent.Instance.ParseSchema(exported).Validate(transcoded);

        // Assert
        await Assert.That(result.IsValid)
            .IsTrue()
            .Because(string.Join("; ", result.Errors.Select(error => error.ToString())));
    }

    [Test]
    public async ValueTask the_exported_schema_rejects_an_untagged_union_branch() {
        // Arrange
        var order      = AvroJsonWriterTests.NewComplexOrder("handle with care");
        var text       = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Schemas", "ComplexOrder.avsc"));
        var exported   = AvroJsonSchema.Export(text);
        var transcoded = JsonNode.Parse(new AvroJsonWriter(text).Write(new AvroSchemaCodec().Encode(order)))!;

        // Act — strip the branch tag the Avro JSON encoding requires.
        transcoded["Note"] = transcoded["Note"]!["string"]!.DeepClone();

        var result = NJsonSchemaAgent.Instance.ParseSchema(exported).Validate(transcoded.ToJsonString());

        // Assert — a schema that accepted this would not be asserting the wrapper at all.
        await Assert.That(result.IsValid).IsFalse();
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

    public sealed class RepositorySchemas : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() =>
            Corpus().Select(path => new TestCase<string>(path, DisplayName: Path.GetRelativePath(AppContext.BaseDirectory, path)));
    }

    public sealed class AnnotationKeys : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() =>
            new[] { "type", "logicalType", "ordinal", "branch", "precision", "scale", "aliases", "order", "props" }
                .Select(key => new TestCase<string>(key, DisplayName: key));
    }

    static IEnumerable<string> Corpus() =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "*.avsc", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);

    static string Coverage() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Corpus", "AnnotationCoverage.avsc"));

    #endregion // Corpus

    #region ->> Helpers <<-

    static List<string> Attributes(string avroSchema) {
        var facts = new List<string>();

        Collect("$", JsonNode.Parse(avroSchema), facts);

        return facts;

        static void Collect(string key, JsonNode? node, List<string> facts) {
            switch (node) {
                case JsonObject o:
                    foreach (var (name, value) in o)
                        if (name is not ("name" or "type" or "namespace"))
                            Collect(name, value, facts);

                    break;

                case JsonArray a:
                    foreach (var item in a) Collect(key, item, facts);
                    break;

                default:
                    facts.Add($"{key}={node?.ToJsonString() ?? "null"}");
                    break;
            }
        }
    }

    static AvroSchemaDocument Parse(string schema) =>
        AvroSchemaParser.TryParse(schema, out var document, out var error, out var offset)
            ? document
            : throw new InvalidOperationException($"schema does not parse at {offset}: {error}");

    static IReadOnlyList<string> FieldNames(AvroSchemaDocument document) =>
        document.Root is AvroRecord record ? record.Fields.Select(field => field.Name).ToList() : [];

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
