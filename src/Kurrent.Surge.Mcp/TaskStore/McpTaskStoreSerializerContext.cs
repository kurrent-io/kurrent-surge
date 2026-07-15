using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace Kurrent.Surge.Mcp;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy   = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(McpTaskInfo))]
partial class McpTaskStoreSerializerContext : JsonSerializerContext;