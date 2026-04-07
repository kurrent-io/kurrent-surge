using System.Text.Json.Serialization;

namespace Kurrent.Processors.Checkpointing;

static partial class Contracts {
    public record ProcessorCheckpoint {
        [JsonPropertyName("prx_id")] public string ProcessorId { get; init; } = null!;

        [JsonPropertyName("pstns")] public PartitionCheckpointed[] Checkpoints { get; init; } = [];

        [JsonPropertyName("wrk_id")] public string WorkerId { get; init; } = null!;

        [JsonPropertyName("ckpt_id")] public string CheckpointId { get; init; } = null!;

        [JsonPropertyName("ckpt_rsn")] public string CheckpointReason { get; init; } = null!;

        [JsonPropertyName("ts")] public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.MinValue;
    }

    /// <summary>
    /// A checkpointed position for a single partition.
    /// </summary>
    public record PartitionCheckpointed {
        [JsonPropertyName("prt")] public int Partition { get; init; }

        [JsonPropertyName("pos")] public long LogPosition { get; init; }

        [JsonPropertyName("str")] public string? Stream { get; init; }

        [JsonPropertyName("rev")] public int? StreamRevision { get; init; }

        [JsonPropertyName("prx_id")] public string ProcessorId { get; init; } = null!;

        [JsonPropertyName("wrk_id")] public string WorkerId { get; init; } = null!;

        [JsonPropertyName("ts")] public DateTimeOffset Timestamp { get; init; }

        // examples:
        // [000] @123
        // [230] @7847222
        // [100] @4587302 (race-0eb72ea5-88b5-462e-b47a-7149d1ae9623 rev.2)
        public override string ToString() =>
            Stream is { } && StreamRevision is { }
                ? $"[{Partition:000}] @{LogPosition} ({Stream} r{StreamRevision})"
                : $"[{Partition:000}] @{LogPosition}";
    }
}
