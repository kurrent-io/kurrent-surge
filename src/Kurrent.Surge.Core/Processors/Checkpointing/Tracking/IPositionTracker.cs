using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Processors.Checkpointing;

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
    /// Tracks a position for later flushing.
    /// </summary>
    int Track(RecordPosition position);

    int Track(Record record) => Track(record.CheckpointPosition);

    /// <summary>
    /// Flushes tracked positions up to and including the specified position.
    /// </summary>
    RecordPosition[] FlushUpTo(RecordPosition position);

    /// <summary>
    /// Resets the tracker to a new initial state, reusing the internal buffer.
    /// </summary>
    /// <param name="lastFlushedPosition">The new last flushed position.</param>
    void Reset(RecordPosition lastFlushedPosition);
}
