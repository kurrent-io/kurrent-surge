using System.Text.Json.Serialization;
using ModelContextProtocol.Extensions.Tasks;

namespace Kurrent.Surge.Mcp;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy   = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(McpTaskInfo))]
partial class McpTaskStoreSerializerContext : JsonSerializerContext;