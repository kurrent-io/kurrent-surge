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
    public AppendRecordsRequest(
        ImmutableArray<AppendRecord> records, ImmutableArray<ConsistencyCheck> checks
    ) : this() {
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
    public required ImmutableArray<ConsistencyCheck> Checks { get; init; }
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
    public required LogPosition Position { get; init; } = Position;
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
[Obsolete("Use AppendRecordsRequest with a single stream and appropriate consistency checks instead.")]
record AppendStreamRequest(StreamName Stream, ExpectedStreamState ExpectedState, IEnumerable<AppendRecord> Messages) {
    /// <summary>
    /// Gets the target stream name.
    /// </summary>
    public required StreamName Stream { get; init; } = Stream;

    /// <summary>
    /// Gets the messages to be appended to the stream.
    /// </summary>
    public required IEnumerable<AppendRecord> Messages { get; init; } = Messages;

    /// <summary>
    /// Gets the expected state condition that must be satisfied before appending.
    /// </summary>
    public required ExpectedStreamState ExpectedState { get; init; } = ExpectedState;
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
[Obsolete("Use AppendRecordsSuccess with appropriate stream revisions instead.")]
public record AppendStreamSuccess(StreamName Stream, LogPosition Position, Revision Revision) {
    /// <summary>
    /// Gets the stream name where the messages were appended.
    /// </summary>
    public required StreamName Stream { get; init; } = Stream;

    /// <summary>
    /// Gets the log position where the messages were persisted.
    /// </summary>
    public required LogPosition Position { get; init; } = Position;

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
[Obsolete("Use AppendRecordsSuccess with appropriate stream revisions instead.")]
public record AppendSessionSuccess(IReadOnlyList<AppendStreamSuccess> Results, LogPosition Position)
    : IReadOnlyList<AppendStreamSuccess> {
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
/// A pre-commit condition that must hold true for the append transaction to succeed.
/// Can be either a <see cref="StreamStateCheck"/> (stream must be at a specific state)
/// or a <see cref="QueryPredicateCheck"/> (a query predicate must evaluate to true).
/// </summary>
public readonly partial record struct ConsistencyCheck : IVariant<StreamStateCheck, QueryPredicateCheck> {
    public static readonly ConsistencyCheck None = new();

    /// <summary>
    /// Starts building a stream state check for the specified stream.
    /// </summary>
    /// <param name="stream">The stream name to check.</param>
    public static StreamStateCheckBuilder Stream(StreamName stream) => new(stream);
}

/// <summary>
/// Fluent builder for constructing a <see cref="StreamStateCheck"/> against a specific stream.
/// </summary>
[PublicAPI]
public readonly struct StreamStateCheckBuilder(StreamName stream) {
    /// <summary>
    /// The stream must be at the specified revision.
    /// </summary>
    /// <param name="revision">The exact revision the stream is expected to be at.</param>
    public StreamStateCheck AtRevision(Revision revision) => new(stream, revision);

    /// <summary>
    /// The stream must exist.
    /// </summary>
    public StreamStateCheck Exists() => new(stream, ExpectedStreamCondition.Exists);

    /// <summary>
    /// The stream must not exist. A soft-deleted stream satisfies this check
    /// because its data has been removed and it can be recreated.
    /// </summary>
    public StreamStateCheck DoesNotExist() => new(stream, ExpectedStreamCondition.NoStream);

    /// <summary>
    /// The stream must be in a soft-deleted state.
    /// </summary>
    public StreamStateCheck IsDeleted() => new(stream, ExpectedStreamCondition.Deleted);

    /// <summary>
    /// The stream must be in a tombstoned (hard-deleted) state.
    /// </summary>
    public StreamStateCheck IsTombstoned() => new(stream, ExpectedStreamCondition.Tombstoned);
}

/// <summary>
/// Asserts a stream is at a specific revision or lifecycle state before commit.
/// </summary>
public readonly record struct StreamStateCheck(StreamName Stream, ExpectedStreamState ExpectedState);

/// <summary>
/// A query predicate evaluated before commit.
/// The transaction is aborted if the predicate expression evaluates to false.
/// </summary>
public readonly record struct QueryPredicateCheck(string Expression) {
    /// <summary>
    /// Gets the predicate expression to evaluate.
    /// </summary>
    public required string Expression { get; init; } = Expression;
}
