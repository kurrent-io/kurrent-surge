namespace Kurrent.Client.Streams.Checkpoints;

/// <summary>
/// Tracks the positions of events in a stream, allowing for gaps and flushes.
/// </summary>
[PublicAPI]
public class PositionTracker : IDisposable {
    readonly object _locker = new();

    readonly SortedSet<TrackedPosition> _trackedPositions = [];

    /// <summary>
    /// Gets the next ready positions.
    /// </summary>
    public IReadOnlyList<TrackedPosition> NextReadyPositions { get; private set; } = [];

    /// <summary>
    /// Gets the latest next ready position.
    /// </summary>
    public TrackedPosition NextReadyPosition { get; private set; } = TrackedPosition.None;

    /// <summary>
    /// Gets the last flushed position.
    /// </summary>
    public TrackedPosition LastFlushedPosition { get; private set; } = TrackedPosition.None;

    /// <summary>
    /// Gets the currently tracked positions.
    /// </summary>
    public IReadOnlyList<TrackedPosition> TrackedPositions {
        get {
            lock (_locker)
                return _trackedPositions.ToList();
        }
    }

    /// <summary>
    /// Gets the number of currently tracked positions.
    /// </summary>
    public int Count => TrackedPositions.Count;

    /// <summary>
    /// Gets the gaps in the tracked positions.
    /// </summary>
    public IReadOnlyList<(ulong Start, ulong End)> Gaps {
        get {
            lock (_locker) {
                var positions = TrackedPositions;
                if (positions.Count == 0)
                    return [];

                var gaps = new List<(ulong Start, ulong End)>();

                var previous = positions.FirstOrDefault(TrackedPosition.None);
                if (previous.SequenceId > 1)
                    gaps.Add((1, previous.SequenceId));

                foreach (var current in positions.Skip(1)) {
                    if (current.SequenceId != previous.SequenceId + 1)
                        gaps.Add((previous.SequenceId, current.SequenceId));

                    previous = current;
                }

                return gaps;
            }
        }
    }

    /// <summary>
    /// Tracks a new position.
    /// </summary>
    /// <param name="sequenceId">The sequence ID of the position.</param>
    /// <param name="position">The record position.</param>
    /// <returns>The tracked position.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the position is unset.</exception>
    /// <exception cref="ArgumentException">Thrown when the sequence ID is less than the last flushed position.</exception>
    public TrackedPosition Track(SequenceId sequenceId, RecordPosition position) {
        if (sequenceId == SequenceId.None)
            throw new ArgumentNullException(nameof(sequenceId));

        if (position == RecordPosition.Unset)
            throw new ArgumentNullException(nameof(position));

        lock (_locker) {
            // Ensure the sequence ID is higher than the last flushed position sequence ID
            if (sequenceId < LastFlushedPosition.SequenceId)
                throw new InvalidOperationException(
                    $"Tracked position sequence {sequenceId} @ {position.LogPosition} must be higher than last flushed"
                  + $" {LastFlushedPosition.SequenceId} @ {LastFlushedPosition.Position.LogPosition}"
                );

            // Ensure the sequence ID is not already tracked
            if (_trackedPositions.Any(x => x.SequenceId == sequenceId && x.Position.LogPosition != position.LogPosition))
                throw new InvalidOperationException($"A different position is already tracked with the same sequence {sequenceId}");

            var trackedPosition = new TrackedPosition(sequenceId, position);

            // ignore duplicates
            if (!_trackedPositions.Add(trackedPosition))
                return TrackedPosition.None;

            RecalculateNextReadyPositions();

            return trackedPosition;
        }
    }

    /// <summary>
    /// Tracks a new position from an event store record.
    /// </summary>
    /// <param name="record">The event store record.</param>
    /// <returns>The tracked position.</returns>
    public TrackedPosition Track(Record record) =>
        Track(record.SequenceId, record.Position);

    /// <summary>
    /// Flushes the next ready positions.
    /// </summary>
    public void Flush() {
        lock (_locker) {
            if (NextReadyPositions.Count != 0)
                FlushPositions(NextReadyPositions);
        }
    }

    /// <summary>
    /// Resets the tracker.
    /// </summary>
    void Reset() {
        lock (_locker) {
            _trackedPositions.Clear();
            NextReadyPositions = [];
        }
    }

    /// <summary>
    /// Disposes the tracker.
    /// </summary>
    public void Dispose() => Reset();

    /// <summary>
    /// Processes the ready positions.
    /// </summary>
    /// <param name="process">The function to execute on the ready positions.</param>
    /// <returns>The list of record positions.</returns>
    public async Task<IReadOnlyList<RecordPosition>> ProcessReadyPositions(Func<IReadOnlyList<RecordPosition>, Task> process) {
        IReadOnlyList<TrackedPosition> readyPositions = [..NextReadyPositions];

        var recordPositions = readyPositions.Select(x => x.Position).ToList();
        if (recordPositions.Count == 0)
            return recordPositions;

        await process(recordPositions);

        FlushPositions(readyPositions);

        return recordPositions;
    }

    void RecalculateNextReadyPositions() {
        if (_trackedPositions.Count == 0) {
            NextReadyPositions = [];
            NextReadyPosition  = TrackedPosition.None;
            return;
        }

        var expectedSequenceId = LastFlushedPosition == TrackedPosition.None
            ? 1 : LastFlushedPosition.SequenceId + 1;

        var nextReadyPositions = _trackedPositions
            .TakeWhile(position => position.SequenceId == expectedSequenceId)
            .Aggregate(new List<TrackedPosition>(), (list, position) => {
                expectedSequenceId++;
                list.Add(position);
                return list;
            });

        NextReadyPositions = nextReadyPositions;
        NextReadyPosition  = NextReadyPositions.Count != 0
            ? NextReadyPositions[^1] : TrackedPosition.None;
    }

    void FlushPositions(IReadOnlyList<TrackedPosition> positions) {
        lock (_locker) {
            LastFlushedPosition = positions[^1];
            _trackedPositions.RemoveWhere(x => x.SequenceId <= LastFlushedPosition.SequenceId);
            RecalculateNextReadyPositions();
        }
    }
}
