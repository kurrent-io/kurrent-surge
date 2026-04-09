using System.Text.Json.Serialization;
using Kurrent.Client.Schema;

namespace Kurrent.Client;

/// <summary>
/// Event written when a scavenge operation starts.
/// Schema: <c>$scavengeStarted</c>
/// </summary>
[PublicAPI]
public record ScavengeStarted {
    [JsonPropertyName("scavengeId")]
    public string ScavengeId { get; init; } = "";

    [JsonPropertyName("nodeEndpoint")]
    public string NodeEndpoint { get; init; } = "";

    [JsonPropertyName("alwaysKeepScavenged")]
    public bool? AlwaysKeepScavenged { get; init; }

    [JsonPropertyName("mergeChunks")]
    public bool? MergeChunks { get; init; }

    [JsonPropertyName("startFromChunk")]
    public int? StartFromChunk { get; init; }

    [JsonPropertyName("threads")]
    public int? Threads { get; init; }
}

/// <summary>
/// Event written when a scavenge operation completes, stops, or errors.
/// Schema: <c>$scavengeCompleted</c>
/// </summary>
[PublicAPI]
public record ScavengeCompleted {
    [JsonPropertyName("scavengeId")]
    public string ScavengeId { get; init; } = "";

    [JsonPropertyName("nodeEndpoint")]
    public string NodeEndpoint { get; init; } = "";

    [JsonPropertyName("result")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ScavengeCompletionResult Result { get; init; }

    [JsonPropertyName("error")]
    public string Error { get; init; } = "";

    [JsonPropertyName("timeTaken")]
    public TimeSpan TimeTaken { get; init; }

    [JsonPropertyName("spaceSaved")]
    public long SpaceSaved { get; init; }

    [JsonPropertyName("maxChunkScavenged")]
    public int MaxChunkScavenged { get; init; }
}

/// <summary>
/// Event written when chunks are scavenged (or skipped).
/// Schema: <c>$scavengeChunksCompleted</c>
/// </summary>
[PublicAPI]
public record ScavengeChunksCompleted {
    [JsonPropertyName("scavengeId")]
    public string ScavengeId { get; init; } = "";

    [JsonPropertyName("chunkStartNumber")]
    public int ChunkStartNumber { get; init; }

    [JsonPropertyName("chunkEndNumber")]
    public int ChunkEndNumber { get; init; }

    [JsonPropertyName("timeTaken")]
    public TimeSpan TimeTaken { get; init; }

    [JsonPropertyName("wasScavenged")]
    public bool WasScavenged { get; init; }

    [JsonPropertyName("spaceSaved")]
    public long SpaceSaved { get; init; }

    [JsonPropertyName("nodeEndpoint")]
    public string NodeEndpoint { get; init; } = "";

    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; init; } = "";
}

/// <summary>
/// Event written when chunks are merged (or skipped).
/// Schema: <c>$scavengeMergeCompleted</c>
/// </summary>
[PublicAPI]
public record ScavengeMergeCompleted {
    [JsonPropertyName("scavengeId")]
    public string ScavengeId { get; init; } = "";

    [JsonPropertyName("chunkStartNumber")]
    public int ChunkStartNumber { get; init; }

    [JsonPropertyName("chunkEndNumber")]
    public int ChunkEndNumber { get; init; }

    [JsonPropertyName("timeTaken")]
    public TimeSpan TimeTaken { get; init; }

    [JsonPropertyName("spaceSaved")]
    public long SpaceSaved { get; init; }

    [JsonPropertyName("wasMerged")]
    public bool WasMerged { get; init; }

    [JsonPropertyName("nodeEndpoint")]
    public string NodeEndpoint { get; init; } = "";

    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; init; } = "";
}

/// <summary>
/// Event written when index tables are scavenged (or skipped).
/// Schema: <c>$scavengeIndexCompleted</c>
/// </summary>
[PublicAPI]
public record ScavengeIndexCompleted {
    [JsonPropertyName("scavengeId")]
    public string ScavengeId { get; init; } = "";

    [JsonPropertyName("level")]
    public int Level { get; init; }

    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("timeTaken")]
    public TimeSpan TimeTaken { get; init; }

    [JsonPropertyName("entriesDeleted")]
    public long EntriesDeleted { get; init; }

    [JsonPropertyName("entriesKept")]
    public long EntriesKept { get; init; }

    [JsonPropertyName("spaceSaved")]
    public long SpaceSaved { get; init; }

    [JsonPropertyName("wasScavenged")]
    public bool WasScavenged { get; init; }

    [JsonPropertyName("nodeEndpoint")]
    public string NodeEndpoint { get; init; } = "";

    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; init; } = "";
}

/// <summary>
/// The result of a completed scavenge operation, as written by the server.
/// </summary>
[PublicAPI]
public enum ScavengeCompletionResult {
    Success,
    Stopped,
    Errored,
    Interrupted
}


public static class TypeMapperExtensions {
    extension(MessageTypeMapper mapper) {
        public void MapScavengeEvents() {
           mapper.Map<ScavengeStarted>("$scavengeStarted");
           mapper.Map<ScavengeCompleted>("$scavengeCompleted");
           mapper.Map<ScavengeChunksCompleted>("$scavengeChunksCompleted");
           mapper.Map<ScavengeMergeCompleted>("$scavengeMergeCompleted");
           mapper.Map<ScavengeIndexCompleted>("$scavengeIndexCompleted");
        }
    }
}
