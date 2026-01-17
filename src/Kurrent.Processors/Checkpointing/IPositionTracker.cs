using Kurrent.Client.Streams;

namespace Kurrent.Client.Processors.Checkpointing;

/// <summary>
/// Tracks positions. Not thread-safe.
/// </summary>
public interface IPositionTracker : IDisposable {
    /// <summary>
    /// Currently tracked positions.
    /// </summary>
    RecordPosition[] Positions { get; }

    /// <summary>
    /// Last flushed position, or <see cref="RecordPosition.Unset"/> if none.
    /// </summary>
    RecordPosition LastFlushedPosition { get; }

    /// <summary>
    /// Last barrier position passed to <see cref="FlushUpTo"/>, or <see cref="RecordPosition.Unset"/> if none.
    /// </summary>
    RecordPosition LastBarrierPosition { get; }

    /// <summary>
    /// Tracks a position for later flushing.
    /// </summary>
    int Track(RecordPosition position);

    /// <summary>
    /// Flushes tracked positions up to and including the specified barrier position.
    /// </summary>
    RecordPosition[] FlushUpTo(RecordPosition barrierPosition);

    /// <summary>
    /// Gets all tracked positions up to and including the specified position.
    /// </summary>
    RecordPosition[] PositionsUpTo(RecordPosition position);
}
