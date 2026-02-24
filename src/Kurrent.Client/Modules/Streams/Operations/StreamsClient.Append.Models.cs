#pragma warning disable CS8604 // Possible null reference argument.

using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Kurrent.Variant;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents a request to append multiple records to the system.
/// </summary>
/// <remarks>
/// This request type allows atomically appending multiple records across different streams,
/// with optional consistency checks to ensure data integrity.
/// </remarks>
[PublicAPI]
public record AppendRecordsRequest() {
    /// <summary>
    /// Creates a builder for appending all records to a single stream.
    /// </summary>
    /// <param name="stream">The target stream for all records.</param>
    public static SingleStreamAppendBuilder ToStream(StreamName stream) => new(stream);

    /// <summary>
    /// Creates a builder for appending records across one or more streams,
    /// with optional consistency checks.
    /// </summary>
    public static MultiStreamAppendBuilder New() => new();

    [method: SetsRequiredMembers]
    public AppendRecordsRequest(ImmutableArray<AppendRecord> records, ImmutableArray<ConsistencyCheck> checks) : this() {
        Records = records;
        Checks  = checks;
    }

    /// <summary>
    /// Gets the records to be appended.
    /// </summary>
    public required ImmutableArray<AppendRecord> Records { get; init; }

    /// <summary>
    /// Gets the consistency checks to be performed before appending.
    /// </summary>
    public required ImmutableArray<ConsistencyCheck> Checks  { get; init; }
}

/// <summary>
/// Represents the successful result of appending multiple records.
/// </summary>
/// <remarks>
/// Contains the stream revisions for each appended record and the log position
/// where the records were persisted.
/// </remarks>
[PublicAPI]
[method: SetsRequiredMembers]
public record AppendRecordsSuccess(StreamRevision[] Revisions, LogPosition Position) {
    /// <summary>
    /// Gets the stream revisions for each appended record.
    /// </summary>
    public required StreamRevision[] Revisions { get; init; } = Revisions;

    /// <summary>
    /// Gets the log position where the records were persisted.
    /// </summary>
    public required LogPosition                      Position  { get; init; } = Position;
}

/// <summary>
/// Represents a request to append messages to a specific stream.
/// </summary>
/// <remarks>
/// This request type allows appending a sequence of messages to a single stream with
/// an optional expected state condition to ensure consistency.
/// </remarks>
[PublicAPI]
[method: SetsRequiredMembers]
public record AppendStreamRequest(StreamName Stream, ExpectedStreamState ExpectedState, IEnumerable<AppendRecord> Messages) {
    /// <summary>
    /// Gets the target stream name.
    /// </summary>
    public required StreamName                Stream        { get; init; } = Stream;

    /// <summary>
    /// Gets the messages to be appended to the stream.
    /// </summary>
    public required IEnumerable<AppendRecord> Messages      { get; init; } = Messages;

    /// <summary>
    /// Gets the expected state condition that must be satisfied before appending.
    /// </summary>
    public required ExpectedStreamState       ExpectedState { get; init; } = ExpectedState;

    /// <summary>
    /// Creates a new builder for fluently constructing an <see cref="AppendStreamRequest"/>.
    /// </summary>
    /// <returns>A new <see cref="AppendStreamRequestBuilder"/> instance.</returns>
    public static AppendStreamRequestBuilder New() => new();
}

/// <summary>
/// Represents the successful result of appending messages to a specific stream.
/// </summary>
/// <remarks>
/// Contains the stream name, log position where the messages were persisted,
/// and the stream revision after appending.
/// </remarks>
[PublicAPI]
[method: SetsRequiredMembers]
public record AppendStreamSuccess(StreamName Stream, LogPosition Position, Revision Revision) {
    /// <summary>
    /// Gets the stream name where the messages were appended.
    /// </summary>
    public required StreamName     Stream         { get; init; } = Stream;

    /// <summary>
    /// Gets the log position where the messages were persisted.
    /// </summary>
    public required LogPosition    Position       { get; init; } = Position;

    /// <summary>
    /// Gets the stream revision after appending.
    /// </summary>
    public required Revision Revision { get; init; } = Revision;
}

/// <summary>
/// Represents the successful result of appending messages to multiple streams in a single session.
/// </summary>
/// <remarks>
/// Provides read-only list semantics over the collection of individual stream append results,
/// allowing enumeration and indexed access to each stream's append outcome.
/// </remarks>
[PublicAPI]
public record AppendSessionSuccess(IReadOnlyList<AppendStreamSuccess> Results, LogPosition Position) : IReadOnlyList<AppendStreamSuccess> {
    /// <summary>
    /// Gets the number of stream append results in this session.
    /// </summary>
    public int Count => Results.Count;

    /// <summary>
    /// Gets the append result for the stream at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the result to retrieve.</param>
    /// <returns>The <see cref="AppendStreamSuccess"/> result at the specified index.</returns>
    public AppendStreamSuccess this[int index] => Results[index];

    /// <summary>
    /// Returns an enumerator that iterates through the stream append results.
    /// </summary>
    /// <returns>An enumerator for the collection of <see cref="AppendStreamSuccess"/> results.</returns>
    public IEnumerator<AppendStreamSuccess> GetEnumerator() => Results.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Represents a mechanism to verify and enforce the consistency and integrity of data or operations
/// within a given context, ensuring that predefined conditions or rules are met.
/// </summary>
public readonly partial record struct ConsistencyCheck : IVariant<StreamConsistencyCheck, QueryConsistencyCheck> {
    public static readonly ConsistencyCheck None = new();

    /// <summary>
    /// Starts building a stream consistency check for the specified stream.
    /// </summary>
    /// <param name="stream">The stream name to check.</param>
    public static StreamConsistencyCheckBuilder Stream(StreamName stream) => new(stream);
}

/// <summary>
/// Fluent builder for constructing a <see cref="StreamConsistencyCheck"/> against a specific stream.
/// </summary>
[PublicAPI]
public readonly struct StreamConsistencyCheckBuilder(StreamName stream) {
    /// <summary>
    /// The stream must be at the specified revision.
    /// </summary>
    /// <param name="revision">The exact revision the stream is expected to be at.</param>
    public StreamConsistencyCheck AtRevision(Revision revision) => new(stream, revision);

    /// <summary>
    /// The stream must exist.
    /// </summary>
    public StreamConsistencyCheck Exists() => new(stream, ExpectedStreamCondition.Exists);

    /// <summary>
    /// The stream must not exist. A soft-deleted stream satisfies this check
    /// because its data has been removed and it can be recreated.
    /// </summary>
    public StreamConsistencyCheck DoesNotExist() => new(stream, ExpectedStreamCondition.NoStream);

    /// <summary>
    /// The stream must be in a soft-deleted state.
    /// </summary>
    public StreamConsistencyCheck IsDeleted() => new(stream, ExpectedStreamCondition.Deleted);

    /// <summary>
    /// The stream must be in a tombstoned (hard-deleted) state.
    /// </summary>
    public StreamConsistencyCheck IsTombstoned() => new(stream, ExpectedStreamCondition.Tombstoned);
}

/// <summary>
/// A consistency check for a specific stream's expected state.
/// </summary>
public readonly record struct StreamConsistencyCheck(StreamName Stream, ExpectedStreamState ExpectedState);

/// <summary>
/// Defines a consistency check using a custom query expression to validate data consistency.
/// </summary>
public readonly record struct QueryConsistencyCheck(string Expression) {
    /// <summary>
    /// Gets the query expression used to validate consistency.
    /// </summary>
    public required string Expression { get; init; } = Expression;
}
