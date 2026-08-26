using System.Text.Json.Nodes;

namespace Kurrent.Surge.Schema;

public interface ISchemaExporter {
	string Export(Type messageType, SchemaDataFormat dataFormat);

	/// <summary>
	/// Exports the schema as a <see cref="JsonNode"/>. The default implementation parses the
	/// string output; exporters that build a node internally override it to skip the round-trip.
	/// </summary>
	JsonNode ExportAsJsonNode(Type messageType, SchemaDataFormat dataFormat) =>
		JsonNode.Parse(Export(messageType, dataFormat))!;
}
