namespace Kurrent.Surge.Client.Streams;

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
    public static readonly RecordPosition Unset = new();

    public static readonly RecordPosition Earliest = new() {
        LogPosition    = LogPosition.Earliest,
        StreamPosition = new(StreamName.Unset, Revision.Min)
    };

    public static readonly RecordPosition Latest = new() {
        LogPosition    = LogPosition.Latest,
        StreamPosition = new(StreamName.Unset, Revision.Max)
    };

    public LogPosition LogPosition { get; init; } = LogPosition.Unset;

    public StreamPosition StreamPosition { get; init; } = StreamPosition.Unset;

    public StreamName Stream => StreamPosition.Stream;

    public Revision Revision => StreamPosition.Revision;

    public Partition Partition { get; init; } = Partition.Any;

    /// <summary>
    /// Indicates whether this position is <see cref="Unset"/>.
    /// </summary>
    public bool IsUnset => this == Unset;

    public override string ToString() =>
        Stream == StreamName.Unset
            ? Partition == Partition.Any
                ? $"{LogPosition}"
                : $"[{Partition}] @{LogPosition}"
            : Partition == Partition.Any
                ? $"{Stream} r{Revision} @{LogPosition}"
                : $"{Stream} r{Revision} [{Partition}] @{LogPosition}";

    // public static RecordPosition From(LogPosition logPosition) {
    //     // if (logPosition == LogPosition.Unset)
    //     //     throw new ArgumentException("LogPosition cannot be Unset.", nameof(logPosition));
    //
    //     return new() { LogPosition = logPosition };
    // }

    public static RecordPosition From(LogPosition logPosition, StreamName stream, Revision revision, Partition partition = default) {
        // if (logPosition == LogPosition.Unset)
        //     throw new ArgumentException("LogPosition cannot be Unset.", nameof(logPosition));
        //
        // if (stream == StreamName.Unset)
        //     throw new ArgumentException("Stream cannot be Unset.", nameof(stream));
        //
        // if (revision == Revision.Unset)
        //     throw new ArgumentException("Revision cannot be Unset.", nameof(revision));

        return new() {
            LogPosition    = logPosition,
            StreamPosition = new(stream, revision),
            Partition      = partition
        };
    }

    public static RecordPosition From(LogPosition logPosition, Partition partition = default) =>
        new() { LogPosition = logPosition, Partition = partition };

    /// <summary>
    /// A log position alone is a valid record position — the stream half stays unset and is
    /// resolved by the operation's routing when a stream target needs it.
    /// </summary>
    public static implicit operator RecordPosition(LogPosition logPosition) =>
        new() { LogPosition = logPosition };

    #region ->> relational members <<-

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
}
