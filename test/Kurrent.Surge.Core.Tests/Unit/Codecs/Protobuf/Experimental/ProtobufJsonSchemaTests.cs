using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Validation;
using Transcoder.Probe;

namespace Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;

/// <summary>
/// The exported document, measured against the draft it declares and against the writer whose output
/// it describes.
/// </summary>
public class ProtobufJsonSchemaTests {
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

    static SchemaSource Order { get; } = SchemaSource.Text(ProbeSchema.Text, ProbeSchema.OrderTypeName);

    static JsonNode Export(JsonSchemaExportOptions? options = null) =>
        ProtobufJsonSchema.Instance.Export(Order, options);

    static byte[] Payload() => new Transcoder.Probe.Order {
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
    }.ToByteArray();

    #region ->> Compliance <<-

    [Test]
    public async ValueTask declares_draft_2020_12() {
        // Act
        var exported = Export();

        // Assert
        await Assert.That(exported["$schema"]!.GetValue<string>()).IsEqualTo(ProtobufJsonSchema.Dialect);
    }

    [Test]
    public async ValueTask emits_only_draft_2020_12_keywords() {
        // Arrange
        var exported = Export();

        // Act
        var stray = Keywords(exported).Where(keyword => !Draft202012Keywords.Contains(keyword)).ToList();

        // Assert
        await Assert.That(stray).IsEmpty().Because($"2020-12 does not define {string.Join(", ", stray)}");
    }

    [Test]
    public async ValueTask hoists_every_named_type_and_every_ref_resolves() {
        // Arrange
        var exported = Export();
        var defs     = (JsonObject)exported["$defs"]!;

        // Act
        var targets = Collect(exported, "$ref").Select(reference => reference["#/$defs/".Length..]).ToList();

        // Assert — the root is a $ref too, so there is always at least one.
        await Assert.That(targets).IsNotEmpty();

        foreach (var target in targets)
            await Assert.That(defs.ContainsKey(target)).IsTrue().Because($"$defs has no entry '{target}'");
    }

    [Test]
    public async ValueTask the_exported_document_satisfies_the_2020_12_meta_schema() {
        // Arrange — the keyword gate proves no stray keyword is emitted. It cannot prove a keyword
        // carries a legal value, which is what the official meta-schema decides.
        var exported = ProtobufJsonSchema.Instance.ExportAsString(Order);

        // Act
        using var document = JsonDocument.Parse(exported);

        var result = Json.Schema.MetaSchemas.Draft202012.Evaluate(document.RootElement);

        // Assert
        await Assert.That(result.IsValid).IsTrue().Because(exported);
    }

    [Test]
    public async ValueTask carries_no_kurrent_specific_keywords() {
        // Arrange — the document is portable JSON Schema. Nothing reads it back into a .proto, so
        // nothing needs an annotation to do it with.
        var exported = Export();

        // Act
        var extensions = Keywords(exported).Where(keyword => keyword.StartsWith("x-", StringComparison.Ordinal)).ToList();

        // Assert
        await Assert.That(extensions).IsEmpty();
    }

    [Test]
    public async ValueTask canonical_sorts_every_object_key() {
        // Arrange
        var exported = Export(Canonical);

        // Act
        var unsorted = Unsorted(exported).ToList();

        // Assert — two structurally identical schemas must export byte for byte alike, which only
        // holds if every object key is ordered.
        await Assert.That(unsorted).IsEmpty();
    }

    #endregion // Compliance

    #region ->> Validation coherence <<-

    [Test]
    public async ValueTask a_transcoded_payload_validates_against_the_exported_schema() {
        // Arrange — the gate that decides what the exported schema even is. The schema describes the
        // JSON the writer emits, so the writer's own output must validate against it.
        var exported = ProtobufJsonSchema.Instance.ExportAsString(Order);

        // Act
        var transcoded = ProtobufSchemaTranscoder.Instance.ToJson(Order, Payload());
        var result     = JsonSchemaNetAgent.Instance.ParseSchema(exported).Validate(transcoded);

        // Assert
        await Assert.That(result.IsValid)
            .IsTrue()
            .Because($"{transcoded} — {string.Join("; ", result.Errors.Select(error => error.ToString()))}");
    }

    [Test]
    public async ValueTask the_exported_schema_rejects_an_undeclared_field() {
        // Arrange — a schema that accepts anything validates nothing.
        var exported   = ProtobufJsonSchema.Instance.ExportAsString(Order);
        var transcoded = ProtobufSchemaTranscoder.Instance.ToJsonNode(Order, Payload());

        // Act — added on the node rather than by trimming the closing brace, which a nested object's
        // own brace would take with it.
        transcoded["notAField"] = 1;

        var result = JsonSchemaNetAgent.Instance.ParseSchema(exported).Validate(transcoded.ToJsonString());

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    #endregion // Validation coherence

    #region ->> The mapping <<-

    [Test]
    public async ValueTask writes_64_bit_integers_as_digit_strings() {
        // Arrange — the mapping spells them as strings because a JSON number loses the range.
        var properties = (JsonObject)Export()["$defs"]![ProbeSchema.OrderTypeName]!["properties"]!;

        // Act
        var sequence = properties["sequence"]!;
        var checksum = properties["checksum"]!;

        // Assert
        await Assert.That(sequence["type"]!.GetValue<string>()).IsEqualTo("string");
        await Assert.That(sequence["pattern"]!.GetValue<string>()).IsEqualTo("^[0-9]+$");
        await Assert.That(checksum["type"]!.GetValue<string>()).IsEqualTo("string");
    }

    [Test]
    public async ValueTask writes_an_open_enum_as_a_name_or_a_number() {
        // Arrange — the probe schema is proto3, so its enums accept a value they have never heard of
        // and the mapping emits that as a bare number. A names-only schema would reject it.
        // Written inline rather than hoisted: only a type that can be referenced twice needs a name,
        // and nothing reads the document back into a .proto any more.
        var exported = Export();

        // Act
        var priority = exported["$defs"]![ProbeSchema.OrderTypeName]!["properties"]!["priority"]!;

        // Assert
        await Assert.That(priority["oneOf"]).IsNotNull();
        await Assert.That(priority["oneOf"]![0]!["enum"]).IsNotNull();
        await Assert.That(priority["oneOf"]![1]!["type"]!.GetValue<string>()).IsEqualTo("integer");
    }

    [Test]
    public async ValueTask describes_a_declaration_with_its_leading_comment() {
        // Arrange — the row that exists only because the parser stopped discarding comments.
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
        var exported = ProtobufJsonSchema.Instance.Export(SchemaSource.Text(schema, "probe.Order"));
        var order    = exported["$defs"]!["probe.Order"]!;

        // Assert
        await Assert.That(order["description"]!.GetValue<string>()).IsEqualTo(" An order placed by a customer.\n");
        await Assert.That(order["properties"]!["orderId"]!["description"]!.GetValue<string>())
            .IsEqualTo(" The identifier the customer quotes.\n");
    }

    #endregion // The mapping

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
