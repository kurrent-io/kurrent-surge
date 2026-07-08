using System.Text.Json.Nodes;

// ReSharper disable CheckNamespace
namespace Kurrent.Client.Tests.Fixtures;

/// <summary>
/// NJsonSchema-free equivalents of the <see cref="JsonSchemaExtensions"/> test helpers, built on
/// <see cref="JsonObject"/>. Kept ALONGSIDE the NJsonSchema helpers (which are left untouched) so
/// the two can be compared for equivalence — see SchemaHelperEquivalenceTests.
///
/// Type tokens are the raw JSON Schema keywords ("string", "integer", "null", ...) rather than
/// NJsonSchema's JsonObjectType enum, keeping this dependency-free. Classic `this` extension syntax
/// mirrors the sibling <see cref="JsonSchemaExtensions"/> so the comparison reads 1:1.
/// </summary>
public static class JsonNodeSchemaHelpers {
	public const string String  = "string";
	public const string Integer = "integer";
	public const string Boolean = "boolean";
	public const string Null    = "null";

	public static JsonObject NewJsonNodeSchemaDefinition() =>
		JsonNode.Parse(
			"""
			{
			    "type": "object",
			    "properties": {
			        "id": { "type": "string" },
			        "name": { "type": "string" }
			    },
			    "required": ["id"]
			}
			"""
		)!.AsObject();

	public static string ToJson(this JsonObject schema) => schema.ToJsonString();

	static JsonObject Clone(this JsonObject schema) => JsonNode.Parse(schema.ToJsonString())!.AsObject();

	static JsonObject Properties(this JsonObject schema) => schema["properties"]!.AsObject();

	// Ensures a `required` array exists and returns it.
	static JsonArray Required(this JsonObject schema) {
		if (schema["required"] is not JsonArray required) {
			required = [];
			schema["required"] = required;
		}

		return required;
	}

	static bool RequiredContains(this JsonObject schema, string name) =>
		schema["required"] is JsonArray required && required.Any(node => (string?)node == name);

	static void RequireName(this JsonObject schema, string name) {
		if (!schema.RequiredContains(name))
			schema.Required().Add(name);
	}

	static void UnrequireName(this JsonObject schema, string name) {
		if (schema["required"] is not JsonArray required)
			return;

		for (var i = required.Count - 1; i >= 0; i--)
			if ((string?)required[i] == name)
				required.RemoveAt(i);

		// NJsonSchema omits an empty `required` rather than emitting `"required": []`; match it.
		if (required.Count == 0)
			schema.Remove("required");
	}

	static JsonObject AddField(this JsonObject schema, string name, string type, bool required) {
		var clone = schema.Clone();
		clone.Properties()[name] = new JsonObject { ["type"] = type };

		if (required)
			clone.RequireName(name);
		else
			clone.UnrequireName(name);

		return clone;
	}

	public static JsonObject AddOptional(this JsonObject schema, string name, string type) =>
		schema.AddField(name, type, required: false);

	public static JsonObject AddRequired(this JsonObject schema, string name, string type) =>
		schema.AddField(name, type, required: true);

	public static JsonObject MakeRequired(this JsonObject schema, string name) {
		var clone = schema.Clone();
		clone.RequireName(name);
		return clone;
	}

	public static JsonObject MakeOptional(this JsonObject schema, string name) {
		var clone = schema.Clone();
		clone.UnrequireName(name);
		return clone;
	}

	// Named RemoveProperty (not Remove) because JsonObject already has an instance Remove(string)
	// that returns bool; an instance method would shadow a same-named extension.
	public static JsonObject RemoveProperty(this JsonObject schema, string name) {
		var clone = schema.Clone();
		clone.Properties().Remove(name);
		clone.UnrequireName(name);
		return clone;
	}

	public static JsonObject ChangeType(this JsonObject schema, string name, string newType) {
		var clone = schema.Clone();

		if (clone.Properties()[name] is not JsonObject property)
			throw new ArgumentException($"Property '{name}' does not exist in the schema");

		property["type"] = newType;
		return clone;
	}

	public static JsonObject WidenType(this JsonObject schema, string name, string additionalType) {
		var clone = schema.Clone();

		if (clone.Properties()[name] is not JsonObject property)
			throw new ArgumentException($"Property '{name}' does not exist in the schema");

		// Existing scalar type becomes a [existing, additional] array, mirroring NJsonSchema's `type |= ...`.
		var existing = (string?)property["type"];
		property["type"] = new JsonArray { existing, additionalType };
		return clone;
	}
}
