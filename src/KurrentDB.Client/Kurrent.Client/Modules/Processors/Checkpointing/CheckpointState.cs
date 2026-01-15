using System.Collections.Immutable;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Processors.Checkpointing;

/// <summary>
/// Progress information for checkpoint tracking and diagnostics.
/// </summary>
[PublicAPI]
public readonly record struct CheckpointState() {
    /// <summary>
    /// Currently tracked positions.
    /// </summary>
    public ImmutableArray<RecordPosition> TrackedPositions { get; init; } = [];

    /// <summary>
    /// Last checkpoint barrier info.
    /// </summary>
    public CheckpointBarrierInfo LastBarrier { get; init; } = CheckpointBarrierInfo.Unspecified;

    /// <summary>
    /// Last worker checkpoint info.
    /// </summary>
    public CheckpointInfo LastCheckpoint { get; init; } = CheckpointInfo.Unspecified;

    /// <summary>
    /// Last tracked position.
    /// </summary>
    public RecordPosition LastTrackedPosition =>
        TrackedPositions.LastOrDefault(RecordPosition.Unset);
}

public readonly record struct CheckpointBarrierInfo(LogPosition BarrierId, CheckpointBarrierTrigger Trigger, DateTimeOffset Timestamp) {
    public static readonly CheckpointBarrierInfo Unspecified = new(LogPosition.Unset, CheckpointBarrierTrigger.Unspecified, DateTimeOffset.MinValue);
}

public readonly record struct CheckpointInfo(ImmutableDictionary<int, LogPosition> Checkpoints) {
    public static readonly CheckpointInfo Unspecified = new(ImmutableDictionary<int, LogPosition>.Empty);
}

// public readonly record struct PartitionCheckpoint(int Partition, LogPosition Position, DateTimeOffset Timestamp) {
//     public static readonly PartitionCheckpoint Unspecified = new(-1, LogPosition.Unset, DateTimeOffset.MinValue);
// }
