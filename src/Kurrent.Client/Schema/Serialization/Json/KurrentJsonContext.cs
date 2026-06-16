using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Schema.Serialization.Json;

[UsedImplicitly]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(Dictionary<string, JsonValue>))]
[JsonSerializable(typeof(Metadata))]
[JsonConverter(typeof(StreamMetadataJsonConverter))] 
[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	UseStringEnumConverter = true)]
partial class KurrentJsonContext : JsonSerializerContext;
