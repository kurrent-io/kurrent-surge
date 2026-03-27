using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents a request to append records to a single stream.
/// All records are assigned to the specified stream regardless of their individual Stream property.
/// </summary>
[PublicAPI]
public record AppendToStreamRequest() {
    /// <summary>
    /// Creates a builder for appending records to the specified stream.
    /// </summary>
    public static AppendToStreamRequestBuilder For(StreamName stream) => new(stream);

    [method: SetsRequiredMembers]
    public AppendToStreamRequest(
        StreamName stream,
        ImmutableArray<AppendRecord> records,
        ImmutableArray<ConsistencyCheck> checks
    ) : this() {
        Stream  = stream;
        Records = records;
        Checks  = checks;
    }

    /// <summary>
    /// The target stream for all records.
    /// </summary>
    public required StreamName Stream { get; init; }

    /// <summary>
    /// The records to append. All will be assigned to <see cref="Stream"/>.
    /// </summary>
    public required ImmutableArray<AppendRecord> Records { get; init; }

    /// <summary>
    /// Consistency checks to enforce before appending.
    /// </summary>
    public required ImmutableArray<ConsistencyCheck> Checks { get; init; }
}

/// <summary>
/// The result of appending records to a single stream.
/// </summary>
[PublicAPI]
[method: SetsRequiredMembers]
public record AppendToStreamResponse(StreamName Stream, Revision Revision, LogPosition Position) {
    public required StreamName Stream { get; init; } = Stream;
    public required Revision Revision { get; init; } = Revision;
    public required LogPosition Position { get; init; } = Position;
}

