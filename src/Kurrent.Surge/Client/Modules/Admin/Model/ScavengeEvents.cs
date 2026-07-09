using System.Text.Json;
using System.Text.Json.Serialization;
using Kurrent.Surge.Client.Admin;
using Kurrent.Surge.Client.Schema;
using Kurrent.Schema.Catalogs;

namespace Kurrent.Surge.Client;

/// <summary>
/// Handles the standard .NET TimeSpan format strings written by the KurrentDB server (e.g. "00:01:23.4567890"),
/// as opposed to the ISO 8601 duration format used by the SDK's default TimeSpan converter.
/// </summary>
sealed class ServerTimeSpanConverter : JsonConverter<TimeSpan> {
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.Null)
            return TimeSpan.Zero;

        var value = reader.GetString();

        return string.IsNullOrEmpty(value) ? TimeSpan.Zero : TimeSpan.Parse(value);
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>
/// Event written when a scavenge operation starts.
/// Schema: <c>$scavengeStarted</c>
/// </summary>
record ScavengeStarted {
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

record ScavengeCompleted {
    [JsonPropertyName("scavengeId")]
    public string ScavengeId { get; init; } = "";

    [JsonPropertyName("nodeEndpoint")]
    public string NodeEndpoint { get; init; } = "";

    [JsonPropertyName("result")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ScavengeState Result { get; init; }

    [JsonPropertyName("error")]
    public string Error { get; init; } = "";

    [JsonPropertyName("timeTaken")]
    [JsonConverter(typeof(ServerTimeSpanConverter))]
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
record ScavengeChunksCompleted {
    [JsonPropertyName("scavengeId")]
    public string ScavengeId { get; init; } = "";

    [JsonPropertyName("chunkStartNumber")]
    public int ChunkStartNumber { get; init; }

    [JsonPropertyName("chunkEndNumber")]
    public int ChunkEndNumber { get; init; }

    [JsonPropertyName("timeTaken")]
    [JsonConverter(typeof(ServerTimeSpanConverter))]
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
record ScavengeMergeCompleted {
    [JsonPropertyName("scavengeId")]
    public string ScavengeId { get; init; } = "";

    [JsonPropertyName("chunkStartNumber")]
    public int ChunkStartNumber { get; init; }

    [JsonPropertyName("chunkEndNumber")]
    public int ChunkEndNumber { get; init; }

    [JsonPropertyName("timeTaken")]
    [JsonConverter(typeof(ServerTimeSpanConverter))]
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
record ScavengeIndexCompleted {
    [JsonPropertyName("scavengeId")]
    public string ScavengeId { get; init; } = "";

    [JsonPropertyName("level")]
    public int Level { get; init; }

    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("timeTaken")]
    [JsonConverter(typeof(ServerTimeSpanConverter))]
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

// ScavengeCompletionResult is replaced by Kurrent.Surge.Client.Admin.ScavengeState

static class ScavengeMessageCatalogExtensions {
    extension(MessageCatalog catalog) {
        public MessageCatalog MapScavengeEvents() => 
            catalog
                .Map<ScavengeStarted>("$scavengeStarted")
                .Map<ScavengeCompleted>("$scavengeCompleted")
                .Map<ScavengeChunksCompleted>("$scavengeChunksCompleted")
                .Map<ScavengeMergeCompleted>("$scavengeMergeCompleted")
                .Map<ScavengeIndexCompleted>("$scavengeIndexCompleted");
    }
}
