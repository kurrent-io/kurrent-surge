using Kurrent.Client.Streams;
using Kurrent.Processors.Models;

namespace Kurrent.Processors.Checkpointing;

public interface ICheckpointStore {
    /// <summary>
    /// Gets the processor ID this store is bound to.
    /// </summary>
    string ProcessorId { get; }

    /// <summary>
    /// Loads the latest checkpoint for this processor.
    /// </summary>
    ValueTask<ProcessorCheckpoint> LoadLatestAsync(CancellationToken ct = default);

    /// <summary>
    /// Loads the latest checkpoint for this processor,
    /// resolving positions for the given partitions.
    /// </summary>
    ValueTask<ProcessorCheckpoint> LoadLatestAsync(
        Partition[] partitions,
        SubscriptionInitialPosition initialPosition,
        CancellationToken ct = default
    );

    /// <summary>
    /// Stores checkpoint positions for the specified partitions.
    /// </summary>
    /// <param name="workerId">The worker storing the checkpoint (for attribution).</param>
    /// <param name="checkpointId">The checkpoint barrier that triggered this store.</param>
    /// <param name="positions">Partition positions to checkpoint.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask StoreAsync(
        string workerId,
        string checkpointId,
        PartitionPosition[] positions,
        CancellationToken ct = default
    );

    /// <summary>
    /// Resets specific partition positions. Selective reset — only the specified partitions are repositioned.
    /// Empty positions = full reset (barrier, all prior state wiped).
    /// </summary>
    ValueTask ResetAsync(
        string workerId,
        string checkpointId,
        PartitionPosition[] positions,
        CancellationToken ct = default
    );

    /// <summary>
    /// Resets all partitions to the same position.
    /// </summary>
    ValueTask ResetAsync(
        string workerId,
        string checkpointId,
        RecordPosition position,
        CancellationToken ct = default
    );
}

/// <summary>
/// A partition and its record position.
/// </summary>
[PublicAPI]
public readonly record struct PartitionPosition(Partition Partition, RecordPosition Position);

/// <summary>
/// Loaded checkpoint for a processor, with resolved positions for all worker assigned partitions.
/// </summary>
[PublicAPI]
public sealed record ProcessorCheckpoint {
    /// <summary>
    /// Empty checkpoint with no partitions.
    /// </summary>
    public static readonly ProcessorCheckpoint Empty = new();

    /// <summary>
    /// Per-partition positions.
    /// </summary>
    public PartitionPosition[] Positions { get; init; } = [];

    /// <summary>
    /// UTC timestamp when the checkpoint was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.MinValue;

    /// <summary>
    /// The minimum position across all partitions (subscription start position).
    /// </summary>
    public RecordPosition LowWatermark =>
        Positions.Length > 0 ? Positions.Min(p => p.Position) : RecordPosition.Unset;

    /// <summary>
    /// The maximum position across all partitions.
    /// </summary>
    public RecordPosition HighWatermark =>
        Positions.Length > 0 ? Positions.Max(p => p.Position) : RecordPosition.Unset;
}
