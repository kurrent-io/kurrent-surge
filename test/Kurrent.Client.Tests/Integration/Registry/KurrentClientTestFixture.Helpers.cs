// ReSharper disable CheckNamespace

using Google.Protobuf;
using NJsonSchema;
using Sdk.TestKit.Fixtures;

namespace Kurrent.Client.Tests.Fixtures;

public static class KurrentClientTestFixtureRegistryExtensions {
    static readonly TimeSpan EventualConsistencyDelay = TimeSpan.FromMilliseconds(200);

    extension(KurrentClientTestFixture) {

        /// <summary>
        /// Waits for the schema registry read model to catch up after a state-changing operation.
        /// </summary>
        public static Task WaitForEventualConsistency(CancellationToken ct = default) =>
            Task.Delay(EventualConsistencyDelay, ct);

        public static string NewSchemaName() => $"test-schema-{Guid.NewGuid():N}";

        public static JsonSchema NewJsonSchemaDefinition() {
            return JsonSchema.FromJsonAsync(
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
            ).GetAwaiter().GetResult();
        }
    }
}

public static class JsonSchemaExtensions {
	public static ByteString ToByteString(this JsonSchema schema) =>
		ByteString.CopyFromUtf8(schema.ToJson());

	static JsonSchema AddField(this JsonSchema schema, string name, JsonObjectType type, object? defaultValue = null, bool? required = false) {
		var clone = Clone(schema);
		clone.Properties[name] = new JsonSchemaProperty {
			Type = type,
			Default = defaultValue
		};
		switch (required) {
			case true when !clone.RequiredProperties.Contains(name):
				clone.RequiredProperties.Add(name);
				break;
			case false:
				clone.RequiredProperties.Remove(name);
				break;
		}
		return clone;
	}

	public static JsonSchema AddOptional(this JsonSchema schema, string name, JsonObjectType type, object? defaultValue = null) =>
		AddField(schema, name, type, defaultValue, required: false);

	public static JsonSchema AddRequired(this JsonSchema schema, string name, JsonObjectType type, object? defaultValue = null) =>
		AddField(schema, name, type, defaultValue, required: true);

	public static JsonSchema MakeRequired(this JsonSchema schema, string name) {
		var clone = Clone(schema);
		clone.RequiredProperties.Add(name);
		return clone;
	}

	public static JsonSchema Remove(this JsonSchema schema, string name) {
		var clone = Clone(schema);
		clone.Properties.Remove(name);
		clone.RequiredProperties.Remove(name);
		return clone;
	}

	public static JsonSchema MakeOptional(this JsonSchema schema, string name) {
		var clone = Clone(schema);
		clone.RequiredProperties.Remove(name);
		return clone;
	}

	public static JsonSchema ChangeType(this JsonSchema schema, string name, JsonObjectType newType) {
		var clone = Clone(schema);

		if (!clone.Properties.TryGetValue(name, out var property))
			throw new ArgumentException($"Property '{name}' does not exist in the schema");

		property.Type = newType;
		return clone;
	}

	public static JsonSchema WidenType(this JsonSchema schema, string name, JsonObjectType additionalType) {
		var clone = Clone(schema);

		if (!clone.Properties.TryGetValue(name, out var property))
			throw new ArgumentException($"Property '{name}' does not exist in the schema");

		property.Type |= additionalType;
		return clone;
	}

	static JsonSchema Clone(JsonSchema original) => JsonSchema.FromJsonAsync(original.ToJson()).GetAwaiter().GetResult();
}
