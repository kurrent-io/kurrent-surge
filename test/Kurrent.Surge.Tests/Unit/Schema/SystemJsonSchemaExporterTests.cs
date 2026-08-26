// ReSharper disable ClassNeverInstantiated.Local
// ReSharper disable NotAccessedPositionalProperty.Local

using System.Text.Json.Nodes;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Schema;
using NodaTime;

namespace Kurrent.Surge.Tests.Schema;

/// <summary>
/// Unit-level checks on <see cref="SystemJsonSchemaExporter"/> output shape (no server). Verifies the
/// number-handling normalization and both `required` models.
/// </summary>
[Category("Schema")]
public class SystemJsonSchemaExporterTests {
	static JsonObject Export<T>(bool nullableIsOptional) =>
		new SystemJsonSchemaExporter(nullableIsOptional: nullableIsOptional)
			.ExportAsJsonNode(typeof(T), SchemaDataFormat.Json).AsObject();

	[Test]
	public async ValueTask numeric_property_is_a_plain_integer_not_a_string_union() {
		// Arrange — the SDK options enable AllowReadingFromString; the exporter must NOT leak that
		// read-side leniency into the schema as {"type":["string","integer"]}.
		var schema = Export<HasNumber>(nullableIsOptional: true);

		// Act
		var countType  = (string?)schema["properties"]!["count"]!["type"];
		var hasPattern = schema["properties"]!["count"]!.AsObject().ContainsKey("pattern");

		// Assert — canonical integer, no string union, no numeric pattern
		await Assert.That(countType).IsEqualTo("integer");
		await Assert.That(hasPattern).IsFalse();
	}

	[Test]
	public async ValueTask nullable_without_default_is_optional_under_lenient_model() {
		// Arrange
		var schema = Export<HasNullable>(nullableIsOptional: true);

		// Act
		var required = RequiredNames(schema);

		// Assert — nullable member is NOT required; non-nullable member is
		await Assert.That(required).Contains("id");
		await Assert.That(required).DoesNotContain("note");
	}

	[Test]
	public async ValueTask nullable_without_default_is_required_under_strict_model() {
		// Arrange
		var schema = Export<HasNullable>(nullableIsOptional: false);

		// Act
		var required = RequiredNames(schema);

		// Assert — strict model keeps the nullable member in `required`
		await Assert.That(required).Contains("id");
		await Assert.That(required).Contains("note");
	}

	[Test]
	[Arguments("instant", "date-time")]
	[Arguments("localDateTime", "date-time")]
	[Arguments("offsetDateTime", "date-time")]
	[Arguments("zonedDateTime", "date-time")]
	[Arguments("localDate", "date")]
	[Arguments("localTime", "time")]
	[Arguments("duration", "duration")]
	public async ValueTask nodatime_types_map_to_string_with_format_like_njsonschema(string property, string expectedFormat) {
		// Arrange — matches NJsonSchema's built-in NodaTime table (otherwise STJ would emit `true`).
		var schema = Export<HasNodaTime>(nullableIsOptional: true);

		// Act
		var node       = schema["properties"]![property]!.AsObject();
		var nodeType   = (string?)node["type"];
		var nodeFormat = (string?)node["format"];

		// Assert
		await Assert.That(nodeType).IsEqualTo("string");
		await Assert.That(nodeFormat).IsEqualTo(expectedFormat);
	}

	[Test]
	public async ValueTask nullable_nodatime_type_permits_null() {
		// Arrange
		var schema = Export<HasNullableInstant>(nullableIsOptional: true);

		// Act
		var typeValues = schema["properties"]!["moment"]!["type"]!.AsArray().Select(n => (string?)n).ToArray();
		var format     = (string?)schema["properties"]!["moment"]!["format"];

		// Assert — nullable Instant becomes ["string","null"], still carrying the format
		await Assert.That(typeValues).Contains("string").And.Contains("null");
		await Assert.That(format).IsEqualTo("date-time");
	}

	[Test]
	public async ValueTask timespan_maps_to_string_with_duration_format() {
		// Arrange — the SDK's TimeSpanIso8601Converter is a custom converter, so without the mapping
		// JsonSchemaExporter would emit the wildcard `true`. NJsonSchema maps TimeSpan to duration.
		var schema = Export<HasTimeSpan>(nullableIsOptional: true);

		// Act
		var node       = schema["properties"]!["elapsed"]!.AsObject();
		var nodeType   = (string?)node["type"];
		var nodeFormat = (string?)node["format"];

		// Assert
		await Assert.That(nodeType).IsEqualTo("string");
		await Assert.That(nodeFormat).IsEqualTo("duration");
	}

	[Test]
	[Arguments("count", "integer", "int32")]
	[Arguments("total", "integer", "int64")]
	[Arguments("price", "number", "decimal")]
	[Arguments("ratio", "number", "double")]
	[Arguments("payload", "string", "byte")]
	public async ValueTask numeric_and_binary_types_carry_njsonschema_format_hints(string property, string expectedType, string expectedFormat) {
		// Arrange — NJsonSchema adds OpenAPI-style format hints (int32, int64, decimal, byte, ...)
		var schema = Export<HasFormats>(nullableIsOptional: true);

		// Act
		var node       = schema["properties"]![property]!.AsObject();
		var nodeType   = (string?)node["type"];
		var nodeFormat = (string?)node["format"];

		// Assert
		await Assert.That(nodeType).IsEqualTo(expectedType);
		await Assert.That(nodeFormat).IsEqualTo(expectedFormat);
	}

	[Test]
	public async ValueTask integer_enum_is_constrained_to_declared_values() {
		// Arrange — without the transform an int-serialized enum exports as a bare, unconstrained
		// integer; NJsonSchema constrains it to the declared values with x-enumNames. Expectations
		// derive from the real enum so they track its evolution.
		var schema         = Export<HasEnum>(nullableIsOptional: true);
		var expectedValues = Enum.GetValues<SchemaDataFormat>().Cast<int>().ToArray();
		var expectedNames  = Enum.GetNames<SchemaDataFormat>();

		// Act
		var node     = schema["properties"]!["format"]!.AsObject();
		var nodeType = (string?)node["type"];
		var values   = node["enum"]!.AsArray().Select(n => (int)n!).ToArray();
		var names    = node["x-enumNames"]!.AsArray().Select(n => (string)n!).ToArray();

		// Assert
		await Assert.That(nodeType).IsEqualTo("integer");
		await Assert.That(values).IsEquivalentTo(expectedValues);
		await Assert.That(names).IsEquivalentTo(expectedNames);
	}

	[Test]
	public async ValueTask enum_as_oneof_uses_const_title_pairs() {
		// Arrange — the spec-idiomatic labelled-enum form, opted in via enumAsOneOf. Expectations
		// derive from the real enum so they track its evolution.
		var exporter       = new SystemJsonSchemaExporter(enumAsOneOf: true);
		var expectedConsts = Enum.GetValues<SchemaDataFormat>().Cast<int>().ToArray();
		var expectedTitles = Enum.GetNames<SchemaDataFormat>();

		// Act
		var schema   = exporter.ExportAsJsonNode(typeof(HasEnum), SchemaDataFormat.Json).AsObject();
		var node     = schema["properties"]!["format"]!.AsObject();
		var branches = node["oneOf"]!.AsArray();
		var nodeType = (string?)node["type"];
		var consts   = branches.Select(branch => (int)branch!["const"]!).ToArray();
		var titles   = branches.Select(branch => (string)branch!["title"]!).ToArray();

		var hasEnumKeyword     = node.ContainsKey("enum");
		var hasVendorExtension = node.ContainsKey("x-enumNames");

		// Assert — const/title pairs, type kept, no enum keyword, no vendor extension
		await Assert.That(nodeType).IsEqualTo("integer");
		await Assert.That(consts).IsEquivalentTo(expectedConsts);
		await Assert.That(titles).IsEquivalentTo(expectedTitles);
		await Assert.That(hasEnumKeyword).IsFalse();
		await Assert.That(hasVendorExtension).IsFalse();
	}

	[Test]
	public async ValueTask dialect_is_declared_as_2020_12_by_default() {
		// Arrange — JsonSchemaExporter targets draft 2020-12 but never declares it; we do.
		var expectedDialect = "https://json-schema.org/draft/2020-12/schema";
		var schema          = Export<HasNumber>(nullableIsOptional: true);

		// Act
		var dialect  = (string?)schema["$schema"];
		var firstKey = schema.Select(property => property.Key).First();

		// Assert — declared with the official URI, as the first key
		await Assert.That(dialect).IsEqualTo(expectedDialect);
		await Assert.That(firstKey).IsEqualTo("$schema");
	}

	[Test]
	public async ValueTask dialect_declaration_can_be_disabled() {
		// Arrange
		var exporter = new SystemJsonSchemaExporter(declareDialect: false);

		// Act
		var schema     = exporter.ExportAsJsonNode(typeof(HasNumber), SchemaDataFormat.Json).AsObject();
		var hasDialect = schema.ContainsKey("$schema");

		// Assert
		await Assert.That(hasDialect).IsFalse();
	}

	[Test]
	public async ValueTask canonical_export_sorts_keys_and_matches_canonical_form_of_default_output() {
		// Arrange — dialect declaration disabled on both sides: Canonicalize strips `$schema` by
		// design, so this test isolates the canonical flag itself.
		var canonicalExporter = new SystemJsonSchemaExporter(canonical: true, declareDialect: false);
		var defaultExporter   = new SystemJsonSchemaExporter(declareDialect: false);

		var defaultJson = defaultExporter.Export(typeof(HasNumber), SchemaDataFormat.Json);
		var expected    = SystemJsonSchemaExporter.Canonicalize(JsonNode.Parse(defaultJson))!.ToJsonString();

		// Act
		var canonicalJson = canonicalExporter.Export(typeof(HasNumber), SchemaDataFormat.Json);

		var rootKeys         = JsonNode.Parse(canonicalJson)!.AsObject().Select(property => property.Key).ToList();
		var expectedKeyOrder = string.Join(",", rootKeys.OrderBy(key => key, StringComparer.Ordinal));

		// Assert — the flag applies the same canonical form, and object keys come out ordinally sorted
		await Assert.That(canonicalJson).IsEqualTo(expected);
		await Assert.That(string.Join(",", rootKeys)).IsEqualTo(expectedKeyOrder);
	}

	[Test]
	public async ValueTask njsonschema_resolves_a_recursive_export() {
		// Arrange — a self-referencing type is the shape that forces a hoisted definition and a $ref back
		// into it. NJsonSchema's resolver is the consumer that has failed on that shape before, and this
		// exporter is what the client now registers with.
		var definition = new SystemJsonSchemaExporter().Export(typeof(TreeNode), SchemaDataFormat.Json);
		var valid      = """{"name":"root","children":[{"name":"leaf","children":[]}]}""";
		var invalid    = """{"name":"root","children":[{"name":1,"children":[]}]}""";

		// Act
		var schema = await NJsonSchema.JsonSchema.FromJsonAsync(definition);

		// Assert — the emitted $ref is asserted first, because without one the two cases below would
		// pass on a document that never exercised reference resolution at all.
		await Assert.That(definition).Contains("$ref");

		// The negative case is what proves the nested reference resolved. A document whose reference
		// did not resolve accepts both.
		await Assert.That(schema.Validate(valid)).IsEmpty();
		await Assert.That(schema.Validate(invalid)).IsNotEmpty();
	}

	static List<string> RequiredNames(JsonObject schema) =>
		schema["required"] is JsonArray required
			? required.Select(node => (string)node!).ToList()
			: [];

	record HasNumber(int Count, string Name);
	record HasNullable(string Id, string? Note);
	record HasTimeSpan(TimeSpan Elapsed);
	record HasFormats(int Count, long Total, decimal Price, double Ratio, byte[] Payload);

	// Uses the SDK's real SchemaDataFormat enum (Unspecified=0..Bytes=4) instead of a synthetic one.
	record HasEnum(string Id, SchemaDataFormat Format);

	record HasNodaTime(
		Instant Instant, LocalDateTime LocalDateTime, OffsetDateTime OffsetDateTime, ZonedDateTime ZonedDateTime,
		LocalDate LocalDate, LocalTime LocalTime, Duration Duration);

	record HasNullableInstant(Instant? Moment);

	record TreeNode(string Name, TreeNode[] Children);
}
