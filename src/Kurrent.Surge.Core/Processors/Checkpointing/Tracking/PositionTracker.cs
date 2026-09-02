using System.Buffers;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Checkpointing;

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
/// A prepared flush that can be committed after external persistence succeeds.
/// If <see cref="Execute"/> is not called, positions remain tracked for retry.
/// </summary>
[PublicAPI]
public readonly struct PendingPositionsFlush {
    readonly PartitionedPositionTracker _tracker;

    /// <summary>
    /// The positions to be flushed on <see cref="Execute"/>.
    /// </summary>
    public RecordPosition[] Positions { get; }

    internal PendingPositionsFlush(PartitionedPositionTracker tracker, RecordPosition[] positions) {
        _tracker  = tracker;
        Positions = positions;
    }

    /// <summary>
    /// Commits the flush, removing the positions from the tracker.
    /// </summary>
    public void Execute() => _tracker.FlushUpTo(Positions);

    public override string ToString() =>
        string.Join(", ", Positions.Select(p => $"P{p.Partition}={p.LogPosition}"));
}
