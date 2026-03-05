using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Kurrent.Client.Schema.Serialization.Json;

[UsedImplicitly]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(Dictionary<string, JsonValue>))]
[JsonSerializable(typeof(Metadata))]
[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	UseStringEnumConverter = true)]
internal partial class KurrentJsonContext : JsonSerializerContext;
