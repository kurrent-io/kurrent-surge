using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Validation;

namespace Kurrent.Surge.Codecs.Avro.Tests;

[Category("Codecs")]
[Timeout(30_000)]
public class AvroJsonSchemaTests {
    static readonly JsonSchemaExportOptions Canonical = new() { Canonical = true };

    /// <remarks>2020-12 vocabulary. A keyword outside this set is one no validator reads as intended.</remarks>
    static readonly FrozenSet<string> Draft202012Keywords = new[] {
        "$schema", "$ref", "$id", "$defs", "$anchor", "$comment", "title", "description", "default",
        "type", "enum", "const", "allOf", "anyOf", "oneOf", "not", "if", "then", "else",
        "properties", "additionalProperties", "patternProperties", "propertyNames", "required",
        "dependentSchemas", "dependentRequired", "maxProperties", "minProperties",
        "items", "prefixItems", "contains", "maxItems", "minItems", "uniqueItems",
        "unevaluatedItems", "unevaluatedProperties",
        "maximum", "minimum", "exclusiveMaximum", "exclusiveMinimum", "multipleOf",
        "maxLength", "minLength", "pattern", "format", "examples", "deprecated"
    }.ToFrozenSet(StringComparer.Ordinal);

    const string CoverageType = "kurrent.surge.codecs.avro.tests.corpus.ExportCoverage";

    #region ->> Compliance <<-

    [Test]
    [RepositorySchemas]
    public async ValueTask declares_draft_2020_12(string path) {
        // Arrange
        var source = await Source(path);

        // Act
        var exported = AvroJsonSchema.Instance.Export(source);

        // Assert
        await Assert.That(exported["$schema"]?.GetValue<string>()).IsEqualTo(AvroJsonSchema.Dialect);
    }

    [Test]
    [RepositorySchemas]
    public async ValueTask emits_only_draft_2020_12_keywords(string path) {
        // Arrange
        var exported = AvroJsonSchema.Instance.Export(await Source(path));

        // Act
        var stray = Keywords(exported).Where(keyword => !Draft202012Keywords.Contains(keyword)).ToList();

        // Assert
        await Assert.That(stray).IsEmpty().Because($"2020-12 does not define {string.Join(", ", stray)}");
    }

    [Test]
    [RepositorySchemas]
    public async ValueTask the_exported_document_satisfies_the_2020_12_meta_schema(string path) {
        // Arrange — the keyword gate proves no stray keyword is emitted. It cannot prove a keyword
        // carries a legal value, which is what the official meta-schema decides.
        var exported = AvroJsonSchema.Instance.ExportAsString(await Source(path));

        // Act
        using var document = JsonDocument.Parse(exported);

        var result = MetaSchemas.Draft202012.Evaluate(document.RootElement);

        // Assert
        await Assert.That(result.IsValid).IsTrue().Because(exported);
    }

    [Test]
    [RepositorySchemas]
    public async ValueTask carries_no_kurrent_specific_keywords(string path) {
        // Arrange — the document is portable JSON Schema. Nothing reads it back into an Avro schema, so
        // nothing needs an annotation to do it with.
        var exported = AvroJsonSchema.Instance.Export(await Source(path));

        // Act
        var extensions = Keywords(exported).Where(keyword => keyword.StartsWith("x-", StringComparison.Ordinal)).ToList();

        // Assert
        await Assert.That(extensions).IsEmpty().Because($"the document carries {string.Join(", ", extensions)}");
    }

    [Test]
    [RepositorySchemas]
    public async ValueTask exported_refs_are_legal_uri_references(string path) {
        // Arrange
        var exported = AvroJsonSchema.Instance.Export(await Source(path));

        // Act
        var references = Collect(exported, "$ref").ToList();

        // Assert — Uri.TryCreate with RelativeOrAbsolute accepts a space and a bare '!!!', so it asserts
        // nothing. RFC 3986 does not, and neither does the only shape this exporter emits. A schema whose
        // root is not a named type has no $defs and no references at all, which is legal.
        if (exported["$defs"] is JsonObject) await Assert.That(references).IsNotEmpty();

        foreach (var reference in references)
            await Assert.That(IsResolvablePointer(reference))
                .IsTrue()
                .Because($"'{reference}' must be '#/$defs/' followed by an Avro fullname");

        static bool IsResolvablePointer(string reference) {
            const string Prefix = "#/$defs/";

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
        var exported = AvroJsonSchema.Instance.Export(await Source(path));
        var defs     = exported["$defs"] as JsonObject ?? new JsonObject();

        // Act
        var targets = Collect(exported, "$ref").Select(reference => reference["#/$defs/".Length..]).ToList();

        // Assert
        foreach (var target in targets)
            await Assert.That(defs.ContainsKey(target)).IsTrue().Because($"$defs has no entry '{target}'");
    }

    [Test]
    [RepositorySchemas]
    public async ValueTask the_registry_can_resolve_every_exported_schema(string path) {
        // Arrange
        var exported = AvroJsonSchema.Instance.ExportAsString(await Source(path));

        // Act — reference resolution is lazy, so an unresolvable schema throws here rather than returning
        // errors. Validation errors are fine; an exception means the validator cannot read the document.
        var thrown = null as Exception;

        try {
            JsonSchemaNetAgent.Instance.ParseSchema(exported).Validate("{}");
        }
        catch (Exception exception) {
            thrown = exception;
        }

        // Assert
        await Assert.That(thrown).IsNull().Because(thrown?.Message ?? "");
    }

    [Test]
    [RepositorySchemas]
    public async ValueTask canonical_sorts_every_object_key(string path) {
        // Arrange
        var exported = AvroJsonSchema.Instance.Export(await Source(path), Canonical);

        // Act
        var unsorted = Unsorted(exported).ToList();

        // Assert — two structurally identical schemas must export byte for byte alike, which only holds
        // if every object key is ordered. Array items are never sorted: oneOf order is the union tag and
        // enum order is the symbol ordinal, so reordering either would change what the schema says.
        await Assert.That(unsorted).IsEmpty();
    }

    [Test]
    public async ValueTask a_source_carrying_several_schemas_is_rejected() {
        // Arrange — an Avro document declares one root type, so a source with two roots has no answer.
        var text   = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Schemas", "TestOrder.avsc"));
        var source = SchemaSource.From(null, text, text);

        // Act
        var act = () => AvroJsonSchema.Instance.Export(source);

        // Assert
        await Assert.That(act).Throws<ArgumentException>();
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

    #region ->> The facts standard keywords carry <<-

    [Test]
    public async ValueTask a_fixed_asserts_its_exact_byte_count() {
        // Arrange
        var expectedSize = 17;

        // Act
        var checksum = Coverage()["$defs"]!["kurrent.surge.codecs.avro.tests.corpus.Checksum"]!;

        // Assert — minLength and maxLength recover the byte count, because the encoding is one character
        // per byte.
        await Assert.That(checksum["minLength"]!.GetValue<int>()).IsEqualTo(expectedSize);
        await Assert.That(checksum["maxLength"]!.GetValue<int>()).IsEqualTo(expectedSize);
        await Assert.That(checksum["pattern"]!.GetValue<string>()).IsEqualTo(AvroJsonSchema.ByteStringPattern);
    }

    [Test]
    public async ValueTask preserves_bytes_default_as_avro_encodes_it() {
        // Arrange
        var expected = "ÿþABCDEFGHIJKLMNO";

        // Act
        var checksum = CoverageRecord()["properties"]!["checksum"]!;

        // Assert — Avro writes a bytes or fixed default as one code point per byte, and the exported schema
        // describes that same encoding, so the value crosses unchanged. Reading it as base64 is the bug
        // this codec already shipped once, and base64 would not survive this comparison.
        await Assert.That(checksum["default"]!.GetValue<string>()).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask preserves_an_enum_default() {
        // Arrange
        var expected = "Unknown";

        // Act — the enum's own default is a symbol, so the standard keyword carries it whole.
        var status = Coverage()["$defs"]!["kurrent.surge.codecs.avro.tests.corpus.Status"]!;

        // Assert
        await Assert.That(status["default"]!.GetValue<string>()).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask a_union_of_records_with_a_null_branch_tags_only_the_records() {
        // Arrange — the Avro JSON encoding writes the null branch bare and wraps every other branch in
        // an object keyed by the branch's type name. A record branch is keyed by its fullname.
        var sighting = CoverageRecord()["properties"]!["sighting"]!["oneOf"]!.AsArray();

        // Act
        var nullBranch = sighting[0]!;
        var cat        = sighting[1]!;

        // Assert
        await Assert.That(nullBranch["type"]!.GetValue<string>()).IsEqualTo("null");
        await Assert.That(nullBranch["properties"]).IsNull();

        await Assert.That(cat["type"]!.GetValue<string>()).IsEqualTo("object");
        await Assert.That(cat["required"]![0]!.GetValue<string>())
            .IsEqualTo("kurrent.surge.codecs.avro.tests.corpus.Cat");
        await Assert.That(cat["additionalProperties"]!.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async ValueTask a_logical_type_is_written_as_its_base_type() {
        // Arrange — a logical type does not change the Avro JSON encoding, so the schema asserts the base
        // type the wire carries. The logical name itself is not carried at all.
        var properties = CoverageRecord()["properties"]!;

        // Act
        var observedAt = properties["observedAt"]!;
        var recordedAt = properties["recordedAt"]!;

        // Assert
        foreach (var node in (JsonNode[])[observedAt, recordedAt]) {
            await Assert.That(node["type"]!.GetValue<string>()).IsEqualTo("integer");
            await Assert.That(node["format"]).IsNull();
        }
    }

    #endregion // The facts standard keywords carry

    #region ->> Validation coherence <<-

    [Test]
    public async ValueTask a_transcoded_payload_validates_against_the_exported_schema() {
        // Arrange — the gate that decides what the exported schema even is. The schema describes the JSON
        // the transcoder emits, so the transcoder's own output must validate against it.
        var order    = AvroJsonWriterTests.NewComplexOrder("handle with care");
        var source   = await ComplexOrder();
        var payload  = new AvroSchemaCodec().Encode(order);
        var exported = AvroJsonSchema.Instance.ExportAsString(source);

        // Act
        var transcoded = AvroSchemaTranscoder.Instance.ToJson(source, payload);
        var result     = JsonSchemaNetAgent.Instance.ParseSchema(exported).Validate(transcoded);

        // Assert
        await Assert.That(result.IsValid)
            .IsTrue()
            .Because($"{transcoded} — {string.Join("; ", result.Errors.Select(error => error.ToString()))}");
    }

    [Test]
    public async ValueTask the_exported_schema_rejects_an_untagged_union_branch() {
        // Arrange
        var order      = AvroJsonWriterTests.NewComplexOrder("handle with care");
        var source     = await ComplexOrder();
        var exported   = AvroJsonSchema.Instance.ExportAsString(source);
        var transcoded = AvroSchemaTranscoder.Instance.ToJsonNode(source, new AvroSchemaCodec().Encode(order));

        // Act — strip the branch tag the Avro JSON encoding requires.
        transcoded["Note"] = transcoded["Note"]!["string"]!.DeepClone();

        var result = JsonSchemaNetAgent.Instance.ParseSchema(exported).Validate(transcoded.ToJsonString());

        // Assert — a schema that accepted this would not be asserting the wrapper at all.
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask the_exported_schema_rejects_an_undeclared_field() {
        // Arrange — a schema that accepts anything validates nothing.
        var order      = AvroJsonWriterTests.NewComplexOrder("handle with care");
        var source     = await ComplexOrder();
        var exported   = AvroJsonSchema.Instance.ExportAsString(source);
        var transcoded = AvroSchemaTranscoder.Instance.ToJsonNode(source, new AvroSchemaCodec().Encode(order));

        // Act — added on the node rather than by trimming the closing brace, which the union wrapper's
        // own brace would take with it.
        transcoded["notAField"] = 1;

        var result = JsonSchemaNetAgent.Instance.ParseSchema(exported).Validate(transcoded.ToJsonString());

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    #endregion // Validation coherence

    #region ->> Corpus <<-

    public sealed class RepositorySchemas : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() =>
            Corpus().Select(path => new TestCase<string>(path, DisplayName: Path.GetRelativePath(AppContext.BaseDirectory, path)));
    }

    static IEnumerable<string> Corpus() =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "*.avsc", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);

    static async Task<SchemaSource> Source(string path) =>
        SchemaSource.Text(await File.ReadAllTextAsync(path));

    static async Task<SchemaSource> ComplexOrder() =>
        await Source(Path.Combine(AppContext.BaseDirectory, "Schemas", "ComplexOrder.avsc"));

    static JsonNode Coverage() =>
        AvroJsonSchema.Instance.Export(
            SchemaSource.Text(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Corpus", "AnnotationCoverage.avsc"))));

    static JsonNode CoverageRecord() => Coverage()["$defs"]![CoverageType]!;

    #endregion // Corpus

    #region ->> Helpers <<-

    static IEnumerable<string> Keywords(JsonNode? node) {
        switch (node) {
            case JsonObject o:
                foreach (var (key, value) in o) {
                    yield return key;

                    // The keys under these are names the schema author chose, not keywords.
                    var named = key is "properties" or "$defs";

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
}
