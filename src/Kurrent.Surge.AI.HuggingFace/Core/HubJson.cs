using System.Text.Json.Serialization;

namespace Kurrent.Surge.AI.HuggingFace;

// Hugging Face tree-listing entry: [{"type":"file","path":"onnx/model.onnx","size":2048,"lfs":{"oid":"…","size":…}}]
sealed class TreeEntry {
    public string?  Type { get; set; }
    public string?  Path { get; set; }
    public long?    Size { get; set; }
    public LfsInfo? Lfs  { get; set; }
}

sealed class LfsInfo {
    public string? Oid  { get; set; }
    public long?   Size { get; set; }
}

// Source-generated (AOT-safe) — no reflection-based JSON.
[JsonSerializable(typeof(List<TreeEntry>))]
[JsonSerializable(typeof(TreeEntry))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
sealed partial class HubJson : JsonSerializerContext;