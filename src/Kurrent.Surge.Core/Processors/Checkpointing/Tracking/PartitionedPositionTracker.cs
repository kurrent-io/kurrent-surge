using System.Collections.Frozen;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Processors.Checkpointing;

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
    public PartitionedPositionTracker(RecordPosition[] positions) =>
        _trackers = positions.ToFrozenDictionary(
            x => x.Partition,
            x => new PositionTracker(lastFlushedPosition: x));

    /// <summary>
    /// Creates a tracker with no initial partitions.
    /// </summary>
    public PartitionedPositionTracker() =>
        _trackers = FrozenDictionary<Partition, PositionTracker>.Empty;

    /// <summary>
    /// Gets the last flushed (checkpointed) position for each partition.
    /// </summary>
    public RecordPosition[] LastFlushedPositions =>
        _trackers.Select(x => x.Value.LastFlushedPosition).ToArray();

    /// <summary>
    /// Gets the last tracked (pending, not yet flushed) position for each partition.
    /// Returns <see cref="LogPosition.Unset"/> for partitions with no pending positions.
    /// </summary>
    public RecordPosition[] LastTrackedPositions =>
        _trackers
            .Select(x => x.Value.Positions.LastOrDefault(RecordPosition.Unset))
            .ToArray();

    /// <summary>
    /// Gets the number of partitions being tracked.
    /// </summary>
    public int PartitionCount => _trackers.Count;

    public bool HasPendingPositions => _trackers.Any(x => x.Value.Positions.Length > 0);

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
    public PendingPositionsFlush PrepareFlush() =>
        new(this, Snapshot());

    /// <summary>
    /// Snapshots all pending positions at the moment of call (last pending per partition,
    /// or the partition's last flushed position if no pending). Pure read — does not prepare
    /// a flush handle, does not advance the tracker. Use this when the caller persists via
    /// an explicit positions dictionary and advances the tracker via <see cref="FlushUpTo"/>
    /// directly (no <see cref="PendingPositionsFlush.Execute"/> coordination required).
    /// </summary>
    public RecordPosition[] Snapshot() {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        return _trackers
            .Select(x => x.Value.Positions.LastOrDefault(x.Value.LastFlushedPosition))
            .ToArray();
    }

    /// <summary>
    /// Flushes tracked positions up to the specified positions for each partition.
    /// </summary>
    public void FlushUpTo(RecordPosition[] positions) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var position in positions) {
            if (_trackers.TryGetValue(position.Partition, out var tracker))
                tracker.FlushUpTo(position);
        }
    }

    /// <summary>
    /// Resets the tracker with new partition positions, reusing existing trackers where possible.
    /// </summary>
    /// <param name="positions">The new partition positions.</param>
    public void Reset(RecordPosition[] positions) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var oldCount    = _trackers.Count;
        var newTrackers = new Dictionary<Partition, PositionTracker>(positions.Length);

        foreach (var position in positions) {
            var partition = position.Partition;
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
