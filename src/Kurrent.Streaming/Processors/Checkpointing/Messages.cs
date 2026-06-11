using System.Text.Json.Serialization;

namespace Kurrent.Streaming.Checkpointing;

public static partial class Contracts {
    /// <summary>
    /// One or more partition positions checkpointed by a drone.
    /// </summary>
    public record PartitionsCheckpointed {
        [JsonPropertyName("prx_id")] public string ProcessorId { get; init; } = null!;

        [JsonPropertyName("drn_id")] public string DroneId { get; init; } = null!;

        [JsonPropertyName("ckpt_id")] public string CheckpointId { get; init; } = null!;

        [JsonPropertyName("pstns")] public PartitionPosition[] Positions { get; init; } = [];

        [JsonPropertyName("ts")] public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.MinValue;
    }

    /// <summary>
    /// Position data for a single partition within a checkpoint event.
    /// </summary>
    public record PartitionPosition {
        [JsonPropertyName("prt")] public int Partition { get; init; }

        [JsonPropertyName("pos")] public long LogPosition { get; init; }

        [JsonPropertyName("str")] public string? Stream { get; init; }

        [JsonPropertyName("rev")] public int? StreamRevision { get; init; }

        // examples:
        // [000] @123
        // [230] @7847222
        // [100] @4587302 (race-0eb72ea5-88b5-462e-b47a-7149d1ae9623 rev.2)
        public override string ToString() =>
            Stream is { } && StreamRevision is { }
                ? $"[{Partition:000}] @{LogPosition} ({Stream} r{StreamRevision})"
                : $"[{Partition:000}] @{LogPosition}";
    }

    // public record PartitionCheckpoint {
    //     public int     Partition      { get; init; }
    //     public long    LogPosition    { get; init; }
    //     public string? Stream         { get; init; }
    //     public int?    StreamRevision { get; init; }
    //     public byte[]  State          { get; init; } = [];
    // }

    /// <summary>
    /// Checkpoint was reset to a specific position.
    /// <para>
    /// <c>Empty positions</c> - full reset (barrier, all prior events superseded).
    /// <c>Positions present</c> - selective reset (those partitions repositioned, others unaffected).
    /// </para>
    /// </summary>
    public record CheckpointReset {
        [JsonPropertyName("prx_id")] public string ProcessorId { get; init; } = null!;

        [JsonPropertyName("drn_id")] public string DroneId { get; init; } = null!;

        [JsonPropertyName("ckpt_id")] public string CheckpointId { get; init; } = null!;

        [JsonPropertyName("pstns")] public PartitionPosition[] Positions { get; init; } = [];

        [JsonPropertyName("ts")] public DateTimeOffset Timestamp { get; init; }
    }
}
