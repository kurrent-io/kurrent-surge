using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors.Models;

namespace Kurrent.Surge.Processors.Checkpointing;

public interface ICheckpointStore {
    /// <summary>Loads the latest checkpoint for this processor.</summary>
    ValueTask<ProcessorCheckpoint> LoadLatestAsync(string droneId, CancellationToken ct = default);

    /// <summary>Loads the latest checkpoint, resolving positions for the given partitions.</summary>
    ValueTask<ProcessorCheckpoint> LoadLatestAsync(
        string droneId,
        Partition[] partitions,
        SubscriptionInitialPosition initialPosition,
        CancellationToken ct = default);

    /// <summary>Persists checkpoint positions for the specified partitions.</summary>
    ValueTask StoreAsync(
        string droneId,
        string checkpointId,
        RecordPosition[] positions,
        CancellationToken ct = default);

    /// <summary>
    /// Resets the whole processor group to a single log position — a barrier that supersedes all prior
    /// state; every partition resumes from <paramref name="position"/>. <see cref="LogPosition.Unset"/>
    /// is a full wipe (partitions fall back to the configured initial position). Sentinels are allowed.
    /// </summary>
    /// <returns>The previous checkpoint before the reset.</returns>
    ValueTask<ProcessorCheckpoint> ResetAsync(
        string droneId,
        string checkpointId,
        LogPosition position,
        CancellationToken ct = default);
}

/// <summary>
/// Represents a checkpoint for a processor across multiple partitions.
/// </summary>
public sealed record ProcessorCheckpoint {
    /// <summary>
    /// Empty checkpoint with no partitions.
    /// </summary>
    public static readonly ProcessorCheckpoint Empty = new();

    /// <summary>
    /// Per-partition positions.
    /// </summary>
    public RecordPosition[] Positions { get; init; } = [];

    /// <summary>
    /// UTC timestamp when the checkpoint was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.MinValue;

    /// <summary>
    /// The minimum position across all partitions (subscription start position).
    /// </summary>
    public RecordPosition LowWatermark =>
        Positions.Length > 0 ? Positions.Min() : RecordPosition.Unset;

    /// <summary>
    /// The maximum position across all partitions.
    /// </summary>
    public RecordPosition HighWatermark =>
        Positions.Length > 0 ? Positions.Max() : RecordPosition.Unset;

    /// <summary>
    /// Returns a new checkpoint with positions resolved for the given partitions. Missing
    /// or unset partitions will default to <paramref name="initialPosition"/>.
    /// </summary>
    public ProcessorCheckpoint ResolveStartPositions(SubscriptionInitialPosition initialPosition, params Partition[] partitions) {
        var startFrom = initialPosition == SubscriptionInitialPosition.Earliest
            ? RecordPosition.Earliest
            : RecordPosition.Latest;

        var existing = Positions.ToDictionary(x => x.Partition);

        var resolved = partitions
            .Select(p => existing.TryGetValue(p, out var pos) && !pos.IsUnset
                ? pos
                : startFrom with { Partition = p })
            .ToArray();

        return this with { Positions = resolved };
    }
    
    public ProcessorCheckpoint ForPartitions(params Partition[] partitions) =>
        this with { Positions = Positions.Where(x => partitions.Contains(x.Partition)).ToArray() };
}
