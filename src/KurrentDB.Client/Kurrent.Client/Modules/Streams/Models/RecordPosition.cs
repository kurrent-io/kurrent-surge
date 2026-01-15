using System.Diagnostics.CodeAnalysis;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents the record's position in detail in the system.
/// </summary>
/// <remarks>
/// A log position uniquely and unambiguously identifies a record in the system.
/// Therefore, and when relevant, it can be used to obtain the record's stream
/// information.
/// <para />
/// This type encapsulates the details of a record's position in the system,
/// and allows it to be represented in different ways depending on the context.
/// * A record can be identified solely by its log position.
/// * A record can be identified by its stream and stream revision.
/// * A record can be identified by its stream, stream revision and partition.
/// <para />
/// A stream name is a powerful identifier since it might contain multiple parts such as:
/// * Category
/// * Natural Partition Key Value or Entity ID
/// * Partition : "games-2077|001"
/// <para />
/// Stream name, revision and partition are simply details associated with
/// that log position. In scenarios where only the log position is known or relevant,
/// the other properties can be left unset or set to their respective "none" values.
/// </remarks>
[PublicAPI]
public readonly record struct RecordPosition() : IComparable<RecordPosition>, IComparable {
    public static readonly RecordPosition Unset    = new();
    public static readonly RecordPosition Earliest = new() { LogPosition = LogPosition.Earliest };
    public static readonly RecordPosition Latest   = new() { LogPosition = LogPosition.Latest };

    public LogPosition LogPosition { get; init; } = LogPosition.Unset;

    /// <summary>
    /// The stream name. Use <see cref="StreamName.None"/> for log position only.
    /// </summary>
    public StreamName Stream { get; init; } = StreamName.None;

    /// <summary>
    /// The stream revision. Use <see cref="StreamRevision.Unset"/> if not applicable.
    /// </summary>
    /// <remarks>
    /// A log position uniquely and unambiguously identifies a record in the system.
    /// Therefore, and when relevant, it can be used to obtain the record's stream information.
    /// <para />
    ///
    /// In a stream will always belong to a stream revision and a log position. Nonetheless,
    /// there are scenarios where only one of these values is known or relevant. In such cases,
    /// you can use <see cref="StreamRevision.Unset"/> or <see cref="LogPosition.Unset"/> to indicate
    /// that the respective value is not set or not applicable.
    /// </remarks>
    public StreamRevision StreamRevision { get; init; } = StreamRevision.Unset;
    public Partition      Partition      { get; init; } = Partition.Any;

    /// <summary>
    /// Indicates whether this position is <see cref="Unset"/>.
    /// </summary>
    public bool IsUnset => this == Unset;

    [Experimental("KNET0001")]
    internal StreamName StreamPartition =>
        Partition > Partition.Any
            ? $"{Stream}-{Partition.Value:0000}" : Stream;

    public override string ToString() {
        return Stream == StreamName.None
            ? $"@{LogPosition}"
            : Partition == Partition.Any
                ? $"{Stream} @{LogPosition}"
                : $"{Stream} [{Partition}] @{LogPosition}";
    }

    /// <summary>
    /// Creates a record position from a specific log position.
    /// This position can be used to read from the log directly or to obtain the record's stream information.
    /// </summary>
    public static RecordPosition FromLogPosition(LogPosition logPosition, Partition? partition = null) {
        if (logPosition == LogPosition.Unset)
            throw new ArgumentException("LogPosition cannot be Unset.", nameof(logPosition));

        return new RecordPosition {
            LogPosition = logPosition,
            Partition = partition ?? Partition.Any
        };
    }

    // /// <summary>
    // /// Creates a record position from a specific log position.
    // /// This position can be used to read from the log directly or to obtain the record's stream information.
    // /// </summary>
    // public static RecordPosition FromLogPosition(LogPosition logPosition) {
    //     if (logPosition == LogPosition.Unset)
    //         throw new ArgumentException("LogPosition cannot be Unset.", nameof(logPosition));
    //
    //     return new RecordPosition { LogPosition = logPosition };
    // }

    /// <summary>
    /// Creates a record position from a specific stream and stream revision, with an optional log position and partition.
    /// </summary>
    /// <remarks>
    /// A record in a stream will always have both a stream revision and a log position. Nonetheless,
    /// there are scenarios where only one of these values is known or relevant. In such cases,
    /// you can use <see cref="StreamRevision.Unset"/> or <see cref="LogPosition.Unset"/> to indicate
    /// that the respective value is not set or not applicable.
    /// If the stream is not partitioned, use <see cref="Partition.Any"/> or omit the parameter.
    /// </remarks>
    public static RecordPosition FromStream(StreamName stream, StreamRevision revision, LogPosition? logPosition = null, Partition? partition = null) {
        if (stream == StreamName.None)
            throw new ArgumentException("Stream cannot be None.", nameof(stream));

        return new RecordPosition {
            Stream         = stream,
            StreamRevision = revision,
            Partition      = partition ?? Partition.Any,
            LogPosition    = logPosition ?? LogPosition.Unset
        };
    }

    public static RecordPosition FromStream(StreamName stream, LogPosition logPosition) {
        if (stream == StreamName.None)
            throw new ArgumentException("Stream cannot be None.", nameof(stream));

        if (logPosition == LogPosition.Unset)
            throw new ArgumentException("LogPosition cannot be Unset.", nameof(logPosition));

        return new RecordPosition {
            Stream         = stream,
            LogPosition    = logPosition
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

    public static implicit operator RecordPosition(LogPosition position) => FromLogPosition(position);
    public static implicit operator RecordPosition(long position)        => FromLogPosition(position);
    public static implicit operator long(RecordPosition position)        => position.LogPosition;
}
