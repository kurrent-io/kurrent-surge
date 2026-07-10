using System.Text.Json.Nodes;
using Kurrent.Surge.Client.Schema.Serialization.Json;
using Kurrent.Surge.Schema;
using NJsonSchema;
using NJsonSchema.Generation;

namespace Kurrent.Surge.Client.Schema;

public interface ISchemaExporter {
	string Export(Type messageType, SchemaDataFormat dataFormat);

	/// <summary>
	/// Exports the schema as a <see cref="JsonNode"/>. The default implementation parses the
	/// string output; exporters that build a node internally override it to skip the round-trip.
	/// </summary>
	JsonNode ExportAsJsonNode(Type messageType, SchemaDataFormat dataFormat) =>
		JsonNode.Parse(Export(messageType, dataFormat))!;
}

sealed class NJsonSchemaExporter(SystemTextJsonSchemaGeneratorSettings? settings = null) : ISchemaExporter {
	readonly SystemTextJsonSchemaGeneratorSettings _settings = settings ?? new() {
		SerializerOptions = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions
	};

	public static ISchemaExporter Instance => new NJsonSchemaExporter();

	public string Export(Type messageType, SchemaDataFormat dataFormat) {
		if (dataFormat != SchemaDataFormat.Json)
			throw new NotSupportedException($"Unsupported schema data format. Expected {SchemaDataFormat.Json} but got {dataFormat}.");

		var schema = JsonSchema.FromType(messageType, _settings);

		return schema.ToJson();
	}
}
