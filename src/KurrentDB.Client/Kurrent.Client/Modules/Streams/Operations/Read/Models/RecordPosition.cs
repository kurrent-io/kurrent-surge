using System.Diagnostics.CodeAnalysis;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents the position in detail of the record in the system.
/// </summary>
/// <remarks>
/// A stream partition position is represented as:
/// <br/>- `stream` is the name of the stream (e.g., "orders").
/// <br/>- `partition` is the partition ID (e.g., 0, 1, 2, ...).
/// <br/>- `logPosition` is the position in the global log (e.g., 69420).
/// <br/>A normal stream position is represented as:
/// <br/>- `stream` is the name of the stream (e.g., "users-187").
/// <br/>- `logPosition` is the position in the global log (e.g., 69420).
/// </remarks>
[PublicAPI]
public readonly record struct RecordPosition() : IComparable<RecordPosition>, IComparable {
    public static readonly RecordPosition Unset    = new();
    public static readonly RecordPosition Earliest = new() { LogPosition = LogPosition.Earliest };
    public static readonly RecordPosition Latest   = new() { LogPosition = LogPosition.Latest };

    public StreamName     Stream         { get; init; } = StreamName.None;
    public StreamRevision StreamRevision { get; init; } = StreamRevision.Unset;
    public PartitionId    PartitionId    { get; init; } = PartitionId.Any;
    public LogPosition    LogPosition    { get; init; } = LogPosition.Unset;

    [Experimental("KNET0001")]
    internal StreamName StreamPartition =>
        PartitionId > PartitionId.Any
            ? $"{Stream}-{PartitionId.Value:0000}" : Stream;

    public override string ToString() {
        return Stream == StreamName.None
            ? $"@{LogPosition}"
            : PartitionId == PartitionId.Any
                ? $"{Stream} @{LogPosition}"
                : $"{Stream} [{PartitionId}] @{LogPosition}";
    }

    /// <summary>
    /// Creates a RecordPosition for a specific log position.
    /// </summary>
    public static RecordPosition FromLogPosition(LogPosition logPosition) =>
        new RecordPosition { LogPosition = logPosition };

    /// <summary>
    /// Creates a RecordPosition for a specific stream.
    /// </summary>
    public static RecordPosition FromStream(StreamName stream, StreamRevision revision, LogPosition? logPosition = null) {
        if (revision != StreamRevision.Unset && stream == StreamName.None)
            throw new ArgumentException("Stream cannot be None when specifying a StreamRevision.", nameof(stream));

        return new RecordPosition { Stream = stream, StreamRevision = revision, LogPosition = logPosition ?? LogPosition.Unset };
    }

    /// <summary>
    /// Creates a RecordPosition for a specific partitioned stream.
    /// </summary>
    [Experimental("KNET0001")]
    internal static RecordPosition FromPartitionedStream(StreamName stream, PartitionId partition, LogPosition logPosition) {
        if (stream == StreamName.None)
            throw new ArgumentException("Stream cannot be None.", nameof(stream));

        if (partition == PartitionId.Any)
            throw new ArgumentException("PartitionID cannot be Any(-1).", nameof(partition));

        if (logPosition == LogPosition.Unset || logPosition == LogPosition.Earliest || logPosition == LogPosition.Latest)
            throw new ArgumentException("LogPosition cannot be Unset, Earliest or Latest.", nameof(logPosition));

        return new RecordPosition {
            Stream      = stream,
            PartitionId = partition,
            LogPosition = logPosition,
        };
    }

    #region . relational members .

    public int CompareTo(RecordPosition other) => LogPosition.CompareTo(other.LogPosition);

    public int CompareTo(object? obj) {
        if (ReferenceEquals(null, obj))
            return 1;

        return obj is RecordPosition other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(RecordPosition)}");
    }

    public static bool operator <(RecordPosition left, RecordPosition right)  => left.CompareTo(right) < 0;
    public static bool operator >(RecordPosition left, RecordPosition right)  => left.CompareTo(right) > 0;
    public static bool operator <=(RecordPosition left, RecordPosition right) => left.CompareTo(right) <= 0;
    public static bool operator >=(RecordPosition left, RecordPosition right) => left.CompareTo(right) >= 0;

    #endregion

    public static implicit operator RecordPosition(LogPosition _) => FromLogPosition(_);
    public static implicit operator RecordPosition(long _)        => FromLogPosition(_);
    public static implicit operator long(RecordPosition _)        => _.LogPosition;
}
