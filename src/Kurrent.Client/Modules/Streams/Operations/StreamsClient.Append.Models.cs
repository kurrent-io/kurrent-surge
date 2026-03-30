#pragma warning disable CS8604 // Possible null reference argument.

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
    /// Creates a builder for appending records across one or more streams,
    /// with optional consistency checks.
    /// </summary>
    public static AppendRecordsRequestBuilder New() => new();

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

    /// <summary>
    /// Validates all records meet their invariants and have a stream assigned.
    /// </summary>
    public AppendRecordsRequest EnsureValid() {
        foreach (var record in Records) {
            if (record.Stream == StreamName.Unset)
                throw new InvalidOperationException(
                    $"Record '{record.RecordId}' has no stream assigned. Set the stream on each record or use AppendToStream for single-stream appends.");

            record.EnsureValid();
        }

        return this;
    }
}

/// <summary>
/// The result of appending records across multiple streams.
/// </summary>
[PublicAPI]
[method: SetsRequiredMembers]
public record AppendRecordsResponse(StreamRevision[] Revisions, LogPosition Position) {
    public required StreamRevision[] Revisions { get; init; } = Revisions;
    public required LogPosition Position { get; init; } = Position;
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

    public static StreamStateCheck StreamAtRevision(StreamName stream, Revision revision) =>
        new StreamStateCheck(stream, revision);

    public static StreamStateCheck StreamExists(StreamName stream) =>
        new StreamStateCheck(stream, ExpectedStreamState.Exists);

    public static StreamStateCheck StreamDoesNotExist(StreamName stream) =>
        new StreamStateCheck(stream, ExpectedStreamState.NoStream);

    public static StreamStateCheck StreamIsDeleted(StreamName stream) =>
        new StreamStateCheck(stream, ExpectedStreamState.Deleted);

    public static StreamStateCheck StreamIsTombstoned(StreamName stream) =>
        new StreamStateCheck(stream, ExpectedStreamState.Tombstoned);
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
