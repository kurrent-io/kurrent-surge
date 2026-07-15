namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// Represents a specific position within a stream, combining a stream name with a revision number.
/// This immutable record provides a way to reference an exact location in a stream's event history.
/// </summary>
/// <param name="Stream">The name of the stream.</param>
/// <param name="Revision">The revision number within the stream.</param>
/// <remarks>
/// StreamPosition is used to identify a precise point in a stream's event sequence.
/// It provides several predefined positions:
/// <br/>- Unset: an uninitialized position
/// <br/>- Earliest: the beginning of a stream (minimum revision)
/// <br/>- Latest: the end of a stream (maximum revision)
/// </remarks>
public record StreamPosition(StreamName Stream, Revision Revision) {
    public static readonly StreamPosition Unset    = new(StreamName.Unset, Revision.Unset);
    public static readonly StreamPosition Earliest = new(StreamName.Unset, Revision.Min);
    public static readonly StreamPosition Latest   = new(StreamName.Unset, Revision.Max);
};
