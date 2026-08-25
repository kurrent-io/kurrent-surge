using System.Collections.Frozen;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Kurrent.Surge.Codecs.Protobuf.Experimental;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Validation;
using Kurrent.Testkit.TUnit;
using Transcoder.Probe;

namespace Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;

/// <summary>
/// The exported document, measured against the draft it declares and against the writer whose output
/// it describes.
/// </summary>
public class ProtobufJsonSchemaTests {
    static readonly ProtobufJsonSchemaOptions Canonical = new() { Canonical = true };

    static readonly FrozenSet<string> Draft04Keywords = new[] {
        "$schema", "$ref", "id", "definitions", "title", "description", "default",
        "type", "enum", "allOf", "anyOf", "oneOf", "not",
        "properties", "additionalProperties", "patternProperties", "required", "dependencies",
        "maxProperties", "minProperties",
        "items", "additionalItems", "maxItems", "minItems", "uniqueItems",
        "maximum", "minimum", "exclusiveMaximum", "exclusiveMinimum", "multipleOf",
        "maxLength", "minLength", "pattern", "format"
    }.ToFrozenSet(StringComparer.Ordinal);

    static JsonNode Export(ProtobufJsonSchemaOptions? options = null) =>
        ProtobufJsonSchema.ExportAsJsonNode(ProbeSchema.Set, ProbeSchema.OrderTypeName, options);

    #region ->> Compliance <<-

    [Test]
    public async ValueTask declares_draft_04_by_default() {
        // Act
        var exported = Export();

        // Assert
        await Assert.That(exported["$schema"]!.GetValue<string>()).IsEqualTo(ProtobufJsonSchema.Draft04Dialect);
    }

    [Test]
    public async ValueTask emits_only_draft_04_keywords() {
        // Arrange
        var exported = Export();

        // Act
        var stray = Keywords(exported).Where(keyword => !Draft04Keywords.Contains(keyword)).ToList();

        // Assert
        await Assert.That(stray).IsEmpty().Because($"draft 4 does not define {string.Join(", ", stray)}");
    }

    [Test]
    public async ValueTask every_ref_resolves_into_the_container() {
        // Arrange
        var exported = Export();
        var defs     = exported["definitions"] as JsonObject ?? new JsonObject();

        // Act — '#' is the document root and resolves by definition, so only container pointers are checked.
        var targets = Collect(exported, "$ref")
            .Where(reference => reference != "#")
            .Select(reference => reference["#/definitions/".Length..])
            .ToList();

        // Assert
        await Assert.That(targets).IsNotEmpty();

        foreach (var target in targets)
            await Assert.That(defs.ContainsKey(target)).IsTrue().Because($"definitions has no entry '{target}'");
    }

    [Test]
    public async ValueTask the_registry_can_resolve_the_exported_schema() {
        // Arrange
        var exported = ProtobufJsonSchema.Export(ProbeSchema.Set, ProbeSchema.OrderTypeName);

        // Act — reference resolution is lazy, so an unresolvable schema throws here rather than
        // returning errors. Validation errors are fine; an exception means the registry cannot read it.
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
    public async ValueTask the_2020_12_dialect_hoists_into_defs() {
        // Arrange
        var options = new ProtobufJsonSchemaOptions { Dialect = JsonSchemaDialect.Draft202012 };

        // Act
        var exported = Export(options);

        // Assert — the two dialects differ in the declaration and the container, and in nothing else.
        await Assert.That(exported["$schema"]!.GetValue<string>()).IsEqualTo(ProtobufJsonSchema.Draft202012Dialect);
        await Assert.That(exported["definitions"]).IsNull();
        await Assert.That(exported["$defs"]).IsNotNull();
    }

    [Test]
    public async ValueTask canonical_sorts_every_object_key() {
        // Arrange
        var exported = Export(Canonical);

        // Act
        var unsorted = Unsorted(exported).ToList();

        // Assert — two structurally identical schemas must export byte for byte alike, which only holds
        // if every object key is ordered.
        await Assert.That(unsorted).IsEmpty();
    }

    [Test]
    public async ValueTask disabling_annotations_removes_nothing_but_the_annotations() {
        // Arrange
        var stripped = Export(Canonical);

        Strip(stripped);

        // Act
        var portable = ProtobufJsonSchema.ExportAsJsonNode(
            ProbeSchema.Set, ProbeSchema.OrderTypeName,
            new ProtobufJsonSchemaOptions { Canonical = true, Annotations = false });

        // Assert
        await Assert.That(JsonNode.DeepEquals(portable, stripped))
            .IsTrue()
            .Because("turning annotations off must remove the annotations and nothing else");
    }

    #endregion // Compliance

    #region ->> Round trip <<-

    [Test]
    public async ValueTask a_reimported_schema_parses() {
        // Arrange
        var exported = ProtobufJsonSchema.Export(ProbeSchema.Set, ProbeSchema.OrderTypeName);

        // Act
        var reimported = ProtobufJsonSchema.Import(exported);

        // Assert — the printer's output is read by the parser, so an unparseable print is a failure
        // even when every fact survived.
        var act = () => ProtoSchemaSet.Build(reimported, "reimported.proto");

        await Assert.That(act).ThrowsNothing().Because(reimported);
    }

    [Test]
    public async ValueTask exporting_a_reimported_schema_reproduces_the_document() {
        // Arrange — hoisting and layout legitimately change the text, so the comparison is between the
        // two exported documents rather than between two .proto sources.
        var expected = ProtobufJsonSchema.Export(ProbeSchema.Set, ProbeSchema.OrderTypeName, Canonical);

        // Act
        var reimported = ProtobufJsonSchema.Import(expected);
        var rebuilt    = ProtoSchemaSet.Build(reimported, "reimported.proto");
        var actual     = ProtobufJsonSchema.Export(rebuilt, ProbeSchema.OrderTypeName, Canonical);

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask round_trips_the_annotation_coverage_schema() {
        // Arrange — proto2, reserved ranges and names, an aliased enum on non-ordinal numbers, a
        // json_name override, and trailing and detached comments. Without this the minimality gate
        // passes vacuously: a schema that never round-trips fails every stripped variant too, which
        // reads as every key being load-bearing.
        var (set, message) = MinimalityCorpus().Last();

        var expected = ProtobufJsonSchema.Export(set, message, Canonical);

        // Act
        var reimported = ProtobufJsonSchema.Import(expected);
        var rebuilt    = ProtoSchemaSet.Build(reimported, "reimported.proto");
        var actual     = ProtobufJsonSchema.Export(rebuilt, message, Canonical);

        // Assert
        await Assert.That(actual).IsEqualTo(expected).Because(reimported);
    }

    #endregion // Round trip

    #region ->> Validation coherence <<-

    [Test]
    public async ValueTask a_transcoded_payload_validates_against_the_exported_schema() {
        // Arrange — the gate that decides what the exported schema even is. The schema describes the
        // JSON the writer emits, so the writer's own output must validate against it.
        var order    = NewOrder();
        var exported = ProtobufJsonSchema.Export(ProbeSchema.Set, ProbeSchema.OrderTypeName);

        // Act
        var transcoded = ProbeSchema.Write(order.ToByteArray());
        var result     = NJsonSchemaAgent.Instance.ParseSchema(exported).Validate(transcoded);

        // Assert
        await Assert.That(result.IsValid)
            .IsTrue()
            .Because(string.Join("; ", result.Errors.Select(error => error.ToString())));
    }

    [Test]
    public async ValueTask a_payload_transcoded_with_defaults_validates_against_a_matching_schema() {
        // Arrange — EmitDefaults changes which fields are always written, so the schema exported for it
        // differs from the canonical one. Both sides must be told the same thing.
        var writer   = new ProtobufJsonWriterOptions { EmitDefaults = true };
        var exported = ProtobufJsonSchema.Export(
            ProbeSchema.Set, ProbeSchema.OrderTypeName, new ProtobufJsonSchemaOptions { Writer = writer });

        // Act
        var transcoded = ProbeSchema.Write(NewOrder().ToByteArray(), options: writer);
        var result     = NJsonSchemaAgent.Instance.ParseSchema(exported).Validate(transcoded);

        // Assert
        await Assert.That(result.IsValid)
            .IsTrue()
            .Because(string.Join("; ", result.Errors.Select(error => error.ToString())));
    }

    [Test]
    public async ValueTask a_payload_transcoded_with_proto_field_names_validates_against_a_matching_schema() {
        // Arrange — UseProtoFieldNames changes every property key.
        var writer   = new ProtobufJsonWriterOptions { UseProtoFieldNames = true };
        var exported = ProtobufJsonSchema.Export(
            ProbeSchema.Set, ProbeSchema.OrderTypeName, new ProtobufJsonSchemaOptions { Writer = writer });

        // Act
        var transcoded = ProbeSchema.Write(NewOrder().ToByteArray(), options: writer);
        var result     = NJsonSchemaAgent.Instance.ParseSchema(exported).Validate(transcoded);

        // Assert
        await Assert.That(result.IsValid)
            .IsTrue()
            .Because(string.Join("; ", result.Errors.Select(error => error.ToString())));
    }

    #endregion // Validation coherence

    #region ->> The facts the keys exist for <<-

    [Test]
    public async ValueTask writes_the_root_message_at_the_document_root() {
        // Act
        var exported = Export();

        // Assert — draft 4 keeps the root type here rather than hoisting it, so a reference back is '#'.
        await Assert.That(exported["title"]!.GetValue<string>()).IsEqualTo(ProbeSchema.OrderTypeName);
        await Assert.That(exported["type"]!.GetValue<string>()).IsEqualTo("object");
        await Assert.That(exported["additionalProperties"]!.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async ValueTask carries_every_field_number() {
        // Arrange — no keyword carries the wire tag, and a .proto cannot be written without one.
        var exported   = Export();
        var properties = (JsonObject)exported["properties"]!;
        var expected   = ProbeSchema.Order.Fields.InDeclarationOrder().Count;

        // Act
        var numbered = properties.Count(property => property.Value!["x-protobuf"]?["field"] is not null);

        // Assert
        await Assert.That(numbered).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask writes_64_bit_integers_as_digit_strings() {
        // Arrange — the mapping spells them as strings because a JSON number loses the range.
        var properties = (JsonObject)Export()["properties"]!;

        // Act
        var sequence = properties["sequence"]!;
        var checksum = properties["checksum"]!;

        // Assert
        await Assert.That(sequence["type"]!.GetValue<string>()).IsEqualTo("string");
        await Assert.That(sequence["pattern"]!.GetValue<string>()).IsEqualTo("^[0-9]+$");
        await Assert.That(checksum["type"]!.GetValue<string>()).IsEqualTo("string");
    }

    [Test]
    public async ValueTask marks_only_the_scalar_encodings_the_json_form_cannot_pin() {
        // Arrange — three signed 32-bit encodings share one JSON form. int32 is the documented default
        // Import infers, so stating it would state it twice.
        var properties = (JsonObject)Export()["properties"]!;

        // Act
        var flags = properties["flags"]!;
        var delta = properties["delta"]!;

        // Assert
        await Assert.That(flags["x-protobuf"]!["type"]).IsNull();
        await Assert.That(delta["x-protobuf"]!["type"]!.GetValue<string>()).IsEqualTo("sint32");
    }

    [Test]
    public async ValueTask describes_a_declaration_with_its_leading_comment() {
        // Arrange — the row that exists only because the parser stopped discarding comments. The probe
        // schema documents nothing inside Order, so the case needs a schema that does.
        var schema = """
                     syntax = "proto3";
                     package probe;
                     // An order placed by a customer.
                     message Order {
                       // The identifier the customer quotes.
                       string order_id = 1;
                     }
                     """;

        // Act
        var exported = ProtobufJsonSchema.ExportAsJsonNode(ProtoSchemaSet.Build(schema), "probe.Order");

        // Assert
        await Assert.That(exported["description"]!.GetValue<string>()).IsEqualTo(" An order placed by a customer.\n");
        await Assert.That(exported["properties"]!["orderId"]!["description"]!.GetValue<string>())
            .IsEqualTo(" The identifier the customer quotes.\n");
    }

    #endregion // The facts the keys exist for

    #region ->> Annotation minimality <<-

    public sealed class AnnotationKeys : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() =>
            new[] {
                "field", "type", "presence", "name", "oneof", "map", "wellKnown",
                "syntax", "package", "reservedRanges", "reservedNames", "comments",
                "detached", "values", "allowAlias"
            }.Select(key => new TestCase<string>(key, DisplayName: key));
    }

    /// <remarks>
    /// The probe schema is proto3 and documents nothing, so nine keys have nothing to lose there. A
    /// key that survives because no schema exercises it is a gap in the corpus, not a redundant key.
    /// </remarks>
    static IEnumerable<(ProtoSchemaSet Set, string Message)> MinimalityCorpus() {
        yield return (ProbeSchema.Set, ProbeSchema.OrderTypeName);

        var coverage = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "protos", "annotation_coverage.proto"));

        yield return (ProtoSchemaSet.Build(new ProtoFile("annotation_coverage.proto", coverage)), "coverage.Coverage");
    }

    [Test]
    [AnnotationKeys]
    public async ValueTask dropping_an_annotation_key_breaks_the_round_trip(string key) {
        // Arrange — canonical on purpose: sorting object keys is a legal JSON Schema edit, and running
        // without it would report facts as recoverable that survive only until someone reformats.
        var schemas   = MinimalityCorpus().ToList();
        var surviving = new List<string>();

        // Act
        foreach (var (set, message) in schemas) {
            var expected = ProtobufJsonSchema.Export(set, message, Canonical);
            var stripped = ProtobufJsonSchema.ExportAsJsonNode(set, message, Canonical);

            Strip(stripped, key);

            if (RoundTrips(stripped, message, expected)) surviving.Add(message);
        }

        // Assert — a key whose removal still round-trips on every schema carries nothing the standard
        // keywords do not, and the spec says to delete it and its conversion-table row with it.
        await Assert.That(surviving.Count)
            .IsNotEqualTo(schemas.Count)
            .Because($"'{key}' survived on all {schemas.Count} schemas without contributing anything");

        static bool RoundTrips(JsonNode stripped, string message, string expected) {
            string reimported;

            // A key whose absence makes the document unimportable is necessary by the strongest
            // evidence there is.
            try {
                reimported = ProtobufJsonSchema.Import(stripped);
            }
            catch (ProtobufJsonSchemaException) {
                return false;
            }

            ProtoSchemaSet rebuilt;

            // Scoped to the build alone. A printed schema that no longer parses, resolves or declares
            // the message is the same evidence — but catching it around the export as well would let a
            // bug there read as proof that the key was carrying its weight.
            try {
                rebuilt = ProtoSchemaSet.Build(reimported, "reimported.proto");

                rebuilt.FindMessage(message);
            }
            catch (Exception exception) when (exception is ProtoParseException or ArgumentException) {
                return false;
            }

            return ProtobufJsonSchema.Export(rebuilt, message, Canonical) == expected;
        }
    }

    #endregion // Annotation minimality

    #region ->> Helpers <<-

    static Order NewOrder() => new() {
        OrderId  = "8d2b1f6c-1c4e-4a2f-9d3a-77b0c1a5e4d2",
        Priority = Priority.High,
        Flags    = { 1, 2, 3 },
        Payload  = ByteString.CopyFrom(0xCA, 0xFE),
        Ratio    = 0.125,
        Score    = 2.5f,
        Sequence = ulong.MaxValue,
        Delta    = -7,
        Checksum = 0xDEADBEEFCAFEBABE,
        Archived = true,
        Note     = "handle with care",
        Retries  = 3,
        Tags     = { ["channel"] = "web" }
    };

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

    static void Strip(JsonNode? node, string? key = null) {
        switch (node) {
            case JsonObject o:
                if (o["x-protobuf"] is JsonObject annotation) {
                    if (key is null) {
                        o.Remove("x-protobuf");
                    }
                    else {
                        annotation.Remove(key);

                        if (annotation.Count == 0) o.Remove("x-protobuf");
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
}
