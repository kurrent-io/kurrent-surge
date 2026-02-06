using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Immutable;
using Kurrent.Client.Streams;

namespace Kurrent.Processors.Checkpointing;

/// <summary>
/// High-performance position tracker using ArrayPool for single-threaded partition processing.
/// </summary>
/// <remarks>
/// <para>
/// This implementation is optimized for scenarios where each partition has a dedicated tracker
/// and positions arrive strictly in order. All read operations return spans into the internal
/// buffer with zero allocations.
/// </para>
/// <para>
/// <b>IMPORTANT:</b> Returned spans are valid only until the next mutating operation.
/// Callers must consume span data immediately. This tracker is NOT thread-safe.
/// </para>
/// <para>
/// The implementation uses a linear buffer with a compaction strategy. When the buffer runs
/// out of space, it first attempts to compact by moving elements to the front. If compaction
/// is insufficient, it grows the buffer using ArrayPool.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class PositionTracker : IPositionTracker {
    const int DefaultCapacity = 4096;
    const int MinimumCapacity = 256;

    RecordPosition[] _buffer;

    int  _start;
    int  _count;
    bool _disposed;

    /// <summary>
    /// Initializes a new instance with the default capacity of 4096 positions.
    /// </summary>
    public PositionTracker() : this(DefaultCapacity) { }

    /// <summary>
    /// Initializes a new instance with the specified initial capacity.
    /// </summary>
    /// <param name="capacity">The initial capacity. Must be at least 256.</param>
    public PositionTracker(int capacity) {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, MinimumCapacity);
        _buffer = ArrayPool<RecordPosition>.Shared.Rent(capacity);
    }

    /// <summary>
    /// Initializes a new instance with the specified last flushed position.
    /// </summary>
    public PositionTracker(RecordPosition lastFlushedPosition) : this() {
        LastFlushedPosition = lastFlushedPosition;
    }

    /// <inheritdoc />
    public RecordPosition[] Positions {
        get {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _count == 0
                ? []
                : _buffer.AsSpan(_start, _count).ToArray();
        }
    }

    /// <inheritdoc />
    public RecordPosition LastFlushedPosition { get; private set; } = RecordPosition.Unset;

    /// <inheritdoc />
    public int Track(RecordPosition position) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (position == RecordPosition.Unset)
            throw new ArgumentException("Position cannot be Unset", nameof(position));

        if (LastFlushedPosition != RecordPosition.Unset && position <= LastFlushedPosition)
            throw new InvalidOperationException(
                $"Tracked position {position.LogPosition} must be greater than last flushed position {LastFlushedPosition.LogPosition}");

        EnsureCapacity();

        _buffer[_start + _count] = position;
        _count++;

        return _count;
    }

    /// <inheritdoc />
    public RecordPosition[] FlushUpTo(RecordPosition position) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Find positions up to the specified position
        var count = 0;
        for (var i = 0; i < _count; i++) {
            if (_buffer[_start + i] > position)
                break;

            count = i + 1;
        }

        if (count == 0)
            return [];

        var positionsToFlush = _buffer.AsSpan(_start, count).ToArray();
        LastFlushedPosition  = positionsToFlush[^1];

        _start += count;
        _count -= count;

        return positionsToFlush;
    }

    /// <summary>
    /// Ensures there is capacity for at least one more element.
    /// Compacts first if possible, then grows if needed.
    /// </summary>
    void EnsureCapacity() {
        if (_start + _count < _buffer.Length)
            return;

        // Try compaction first (move elements to front)
        if (_start > 0) {
            _buffer
                .AsSpan(_start, _count)
                .CopyTo(_buffer.AsSpan(0, _count));

            _start = 0;

            if (_count < _buffer.Length)
                return;
        }

        // Need to grow - rent a larger buffer
        var newCapacity = Math.Max(_buffer.Length * 2, _buffer.Length + 1024);

        var newBuffer = ArrayPool<RecordPosition>
            .Shared.Rent(newCapacity);

        _buffer.AsSpan(0, _count).CopyTo(newBuffer);

        ArrayPool<RecordPosition>
            .Shared.Return(_buffer);

        _buffer = newBuffer;
    }

    /// <summary>
    /// Resets the tracker to a new initial state, reusing the internal buffer.
    /// </summary>
    /// <param name="lastFlushedPosition">The new last flushed position.</param>
    public void Reset(RecordPosition lastFlushedPosition) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _start = 0;
        _count = 0;
        LastFlushedPosition = lastFlushedPosition;
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed)
            return;

        ArrayPool<RecordPosition>.Shared
            .Return(_buffer);

        _buffer   = [];
        _disposed = true;
    }
}

/// <summary>
/// Manages position tracking across multiple partitions. Not thread-safe.
/// </summary>
[PublicAPI]
public sealed class PartitionedPositionTracker : IDisposable {
    FrozenDictionary<Partition, PositionTracker> _trackers;

    bool _disposed;

    /// <summary>
    /// Creates a tracker with initial positions per partition.
    /// </summary>
    public PartitionedPositionTracker(IReadOnlyDictionary<Partition, RecordPosition> positions) {
        _trackers = positions.ToFrozenDictionary(
            x => x.Key,
            x => new PositionTracker(lastFlushedPosition: x.Value));
    }

    /// <summary>
    /// Creates a tracker with no initial partitions.
    /// </summary>
    public PartitionedPositionTracker() : this(new Dictionary<Partition, RecordPosition>()) { }

    /// <summary>
    /// Gets the last flushed (checkpointed) position for each partition.
    /// </summary>
    public Dictionary<Partition, LogPosition> LastFlushedPositions =>
        _trackers.ToDictionary(
            x => x.Key,
            x => x.Value.LastFlushedPosition.LogPosition
        );

    /// <summary>
    /// Gets the last tracked (pending, not yet flushed) position for each partition.
    /// Returns <see cref="LogPosition.Unset"/> for partitions with no pending positions.
    /// </summary>
    public Dictionary<Partition, LogPosition> LastTrackedPositions =>
        _trackers.ToDictionary(
            x => x.Key,
            x => x.Value.Positions.LastOrDefault(RecordPosition.Unset).LogPosition
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

    // /// <summary>
    // /// Tracks a position for the specified partition.
    // /// </summary>
    // public int Track(Partition partition, LogPosition position) {
    //     ObjectDisposedException.ThrowIf(_disposed, this);
    //     return !_trackers.TryGetValue(partition, out var tracker)
    //         ? throw new ArgumentException($"Partition {partition} is not being tracked.", nameof(partition))
    //         : tracker.Track(position);
    // }
    //
    // /// <summary>
    // /// Tracks a position for its partition.
    // /// </summary>
    // public int Track(RecordPosition position) =>
    //     Track(position.Partition, position.LogPosition);

    /// <summary>
    /// Prepares a flush without committing. Call <see cref="PendingPositionsFlush.Execute"/> after persistence succeeds.
    /// </summary>
    /// <remarks>
    /// Snapshots ALL pending positions at the moment of call (no position filtering).
    /// This enables distributed checkpoint coordination across workers.
    /// </remarks>
    public PendingPositionsFlush PrepareFlush() {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new PendingPositionsFlush(this, GetAllPendingPositions());
    }

    /// <summary>
    /// Gets all pending positions for each partition.
    /// Returns the last tracked position per partition, or last flushed position if no pending positions.
    /// </summary>
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
            } else {
                newTrackers[partition] = new PositionTracker(position);
            }
        }

        // Only scan for removed partitions if count decreased
        if (newTrackers.Count < oldCount) {
            foreach (var (partition, tracker) in _trackers) {
                if (!newTrackers.ContainsKey(partition))
                    tracker.Dispose();
            }
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

/// <summary>
/// A prepared flush that can be committed after external persistence succeeds.
/// If <see cref="Execute"/> is not called, positions remain tracked for retry.
/// </summary>
[PublicAPI]
public readonly struct PendingPositionsFlush {
    readonly PartitionedPositionTracker _tracker;

    /// <summary>
    /// The positions to be flushed on <see cref="Execute"/>.
    /// </summary>
    public ImmutableDictionary<Partition, RecordPosition> Positions { get; }

    internal PendingPositionsFlush(PartitionedPositionTracker tracker, Dictionary<Partition, RecordPosition> positions) {
        _tracker  = tracker;
        Positions = positions.ToImmutableDictionary();
    }

    /// <summary>
    /// Commits the flush, removing the positions from the tracker.
    /// </summary>
    public void Execute() => _tracker.FlushUpTo(Positions);

    public override string ToString() =>
        string.Join(", ", Positions.Select(kvp => $"P{kvp.Key}={kvp.Value.LogPosition}")); // TODO SS: just use log positions if we keep partition
}
