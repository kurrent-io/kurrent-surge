using System.Text.Json.Nodes;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Tests.Fixtures;
using NJsonSchema;
using NJ = Kurrent.Surge.Tests.Fixtures.JsonNodeSchemaHelpers;

namespace Kurrent.Surge.Tests.Schema;

/// <summary>
/// Proves the NJsonSchema-free <see cref="JsonNodeSchemaHelpers"/> produce schemas semantically
/// identical to the original NJsonSchema-based <see cref="JsonSchemaExtensions"/> helpers, for the
/// same sequence of operations. Comparison is on a canonical form (sorted keys, sorted `required`
/// and `type` arrays, `$schema` dialect marker ignored) since both describe the same schema.
/// </summary>
[Category("Schema")]
public class SchemaHelperEquivalenceTests {
	[Test]
	public async ValueTask base_definitions_are_equivalent() {
		var njson    = KurrentDBFixture.NewJsonSchemaDefinition().ToJson();
		var jsonNode = NJ.NewJsonNodeSchemaDefinition().ToJson();

		await AssertEquivalent(njson, jsonNode);
	}

	[Test]
	public async ValueTask add_optional_is_equivalent() {
		var njson    = KurrentDBFixture.NewJsonSchemaDefinition().AddOptional("email", JsonObjectType.String).ToJson();
		var jsonNode = NJ.NewJsonNodeSchemaDefinition().AddOptional("email", NJ.String).ToJson();

		await AssertEquivalent(njson, jsonNode);
	}

	[Test]
	public async ValueTask add_required_is_equivalent() {
		var njson    = KurrentDBFixture.NewJsonSchemaDefinition().AddRequired("email", JsonObjectType.String).ToJson();
		var jsonNode = NJ.NewJsonNodeSchemaDefinition().AddRequired("email", NJ.String).ToJson();

		await AssertEquivalent(njson, jsonNode);
	}

	[Test]
	public async ValueTask make_required_is_equivalent() {
		var njson    = KurrentDBFixture.NewJsonSchemaDefinition().MakeRequired("name").ToJson();
		var jsonNode = NJ.NewJsonNodeSchemaDefinition().MakeRequired("name").ToJson();

		await AssertEquivalent(njson, jsonNode);
	}

	[Test]
	public async ValueTask make_optional_is_equivalent() {
		var njson    = KurrentDBFixture.NewJsonSchemaDefinition().MakeOptional("id").ToJson();
		var jsonNode = NJ.NewJsonNodeSchemaDefinition().MakeOptional("id").ToJson();

		await AssertEquivalent(njson, jsonNode);
	}

	[Test]
	public async ValueTask remove_is_equivalent() {
		var njson = KurrentDBFixture.NewJsonSchemaDefinition()
			.AddOptional("email", JsonObjectType.String).Remove("email").ToJson();
		var jsonNode = NJ.NewJsonNodeSchemaDefinition()
			.AddOptional("email", NJ.String).RemoveProperty("email").ToJson();

		await AssertEquivalent(njson, jsonNode);
	}

	[Test]
	public async ValueTask change_type_is_equivalent() {
		var njson    = KurrentDBFixture.NewJsonSchemaDefinition().ChangeType("id", JsonObjectType.Integer).ToJson();
		var jsonNode = NJ.NewJsonNodeSchemaDefinition().ChangeType("id", NJ.Integer).ToJson();

		await AssertEquivalent(njson, jsonNode);
	}

	[Test]
	public async ValueTask widen_type_is_equivalent() {
		var njson    = KurrentDBFixture.NewJsonSchemaDefinition().WidenType("id", JsonObjectType.Null).ToJson();
		var jsonNode = NJ.NewJsonNodeSchemaDefinition().WidenType("id", NJ.Null).ToJson();

		await AssertEquivalent(njson, jsonNode);
	}

	static async ValueTask AssertEquivalent(string njsonSchemaJson, string jsonNodeJson) {
		// Arrange — canonicalize both representations using the exporter's canonical form.
		var expected = Canonical(njsonSchemaJson);
		var actual   = Canonical(jsonNodeJson);

		// Assert
		await Assert.That(actual).IsEqualTo(expected);
	}

	static string Canonical(string json) =>
		SystemJsonSchemaExporter.Canonicalize(JsonNode.Parse(json))!.ToJsonString();
}
