using System.Collections.Frozen;
using Kurrent.Client.Streams;

namespace Kurrent.Processors.Checkpointing;

/// <summary>
/// Manages position tracking across multiple partitions.
/// </summary>
[PublicAPI]
public sealed class PartitionedPositionTracker : IDisposable {
    FrozenDictionary<Partition, PositionTracker> _trackers;

    volatile bool _disposed;

    /// <summary>
    /// Creates a tracker with initial positions per partition.
    /// </summary>
    public PartitionedPositionTracker(IReadOnlyDictionary<Partition, RecordPosition> positions) =>
        _trackers = positions.ToFrozenDictionary(
            x => x.Key,
            x => new PositionTracker(lastFlushedPosition: x.Value));

    /// <summary>
    /// Creates a tracker with no initial partitions.
    /// </summary>
    public PartitionedPositionTracker() =>
        _trackers = FrozenDictionary<Partition, PositionTracker>.Empty;

    /// <summary>
    /// Gets the last flushed (checkpointed) position for each partition.
    /// </summary>
    public Dictionary<Partition, RecordPosition> LastFlushedPositions =>
        _trackers.ToDictionary(
            x => x.Key,
            x => x.Value.LastFlushedPosition
        );

    /// <summary>
    /// Gets the last tracked (pending, not yet flushed) position for each partition.
    /// Returns <see cref="LogPosition.Unset"/> for partitions with no pending positions.
    /// </summary>
    public Dictionary<Partition, RecordPosition> LastTrackedPositions =>
        _trackers.ToDictionary(
            x => x.Key,
            x => x.Value.Positions.LastOrDefault(RecordPosition.Unset)
        );

    /// <summary>
    /// Gets the number of partitions being tracked.
    /// </summary>
    public int PartitionCount => _trackers.Count;

    /// <summary>
    /// Tracks positions from a batch of records, grouped by partition.
    /// </summary>
    public int Track(IEnumerable<Record> records) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Single pass: find max position per partition
        Dictionary<Partition, RecordPosition>? maxPositions = null;

        foreach (var record in records) {
            var position  = record.Position;
            var partition = position.Partition;

            // Skip partitions we're not tracking, wonte even happen in practice...
            if (!_trackers.ContainsKey(partition))
                continue;

            maxPositions ??= new Dictionary<Partition, RecordPosition>(_trackers.Count);

            if (!maxPositions.TryGetValue(partition, out var current) || position > current)
                maxPositions[partition] = position;
        }

        if (maxPositions is null)
            return 0;

        // Track the max position for each partition
        var total = 0;
        foreach (var (partition, position) in maxPositions)
            total += _trackers[partition].Track(position);

        return total;
    }

    /// <summary>
    /// Tracks a position for its partition.
    /// </summary>
    public int Track(RecordPosition position) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return !_trackers.TryGetValue(position.Partition, out var tracker)
            ? throw new ArgumentException($"Partition {position.Partition} is not being tracked.", nameof(position))
            : tracker.Track(position);
    }

    /// <summary>
    /// Tracks a record's position for its partition.
    /// </summary>
    public int Track(Record record) => Track(record.Position);

    /// <summary>
    /// Prepares a flush without committing. Call <see cref="PendingPositionsFlush.Execute"/> after persistence succeeds.
    /// </summary>
    /// <remarks>
    /// Snapshots ALL pending positions at the moment of call (no position filtering).
    /// This enables distributed checkpoint coordination across drones.
    /// </remarks>
    public PendingPositionsFlush PrepareFlush() {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new PendingPositionsFlush(this, GetAllPendingPositions());

        Dictionary<Partition, RecordPosition> GetAllPendingPositions() =>
            _trackers.ToDictionary(
                x => x.Key,
                x => {
                    var positions = x.Value.Positions;
                    return positions.Length > 0
                        ? positions[^1]
                        : x.Value.LastFlushedPosition;
                }
            );
    }

    /// <summary>
    /// Flushes tracked positions up to the specified positions for each partition.
    /// </summary>
    public void FlushUpTo(IReadOnlyDictionary<Partition, RecordPosition> positions) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var (partition, tracker) in _trackers) {
            if (positions.TryGetValue(partition, out var position))
                tracker.FlushUpTo(position);
        }
    }

    /// <summary>
    /// Resets the tracker with new partition positions, reusing existing trackers where possible.
    /// </summary>
    /// <param name="positions">The new partition positions.</param>
    public void Reset(IReadOnlyDictionary<Partition, RecordPosition> positions) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var oldCount    = _trackers.Count;
        var newTrackers = new Dictionary<Partition, PositionTracker>(positions.Count);

        foreach (var (partition, position) in positions) {
            if (_trackers.TryGetValue(partition, out var existing)) {
                existing.Reset(position);
                newTrackers[partition] = existing;
            } else
                newTrackers[partition] = new PositionTracker(position);
        }

        // Only scan for removed partitions if count decreased
        if (newTrackers.Count < oldCount)
            foreach (var (partition, tracker) in _trackers) {
                if (!newTrackers.ContainsKey(partition))
                    tracker.Dispose();
            }

        _trackers = newTrackers.ToFrozenDictionary();
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed)
            return;

        foreach (var tracker in _trackers.Values)
            tracker.Dispose();

        _disposed = true;
    }
}
