#pragma warning disable CS8604 // Possible null reference argument.

using System.Diagnostics.CodeAnalysis;
using Kurrent.Variant;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// Lightweight options for the streaming <see cref="StreamsClient.AppendRecords(IEnumerable{AppendRecord}, AppendRecordsOptions?, CancellationToken)"/>
/// overload. Carries consistency checks and optional batch-level overrides without
/// requiring record materialization.
/// </summary>
[PublicAPI]
public record AppendRecordsOptions {
    /// <summary>
    /// Default maximum serialized size, in bytes, of a single record. Mirrors the server's hard
    /// ceiling (<c>TFConsts.MaxLogRecordSize</c>, 16 MiB): a record larger than this can never be
    /// accepted by any server, so the client rejects it before sending.
    /// </summary>
    internal const int DefaultMaxRecordSize = 16 * 1024 * 1024;

    /// <summary>
    /// Default maximum serialized size, in bytes, of the whole append transaction. Mirrors the
    /// server's hard ceiling (<c>TFConsts.ChunkSize</c>, 256 MiB): a transaction larger than this
    /// can never be accepted by any server, so the client rejects it before sending.
    /// </summary>
    internal const int DefaultMaxTransactionSize = 256 * 1024 * 1024;

    /// <summary>
    /// Pre-commit consistency checks evaluated before any records are written.
    /// </summary>
    public IReadOnlyList<ConsistencyCheck> Checks { get; init; } = [];

    /// <summary>
    /// Key-value pairs merged into every record's metadata. Empty by default.
    /// </summary>
    public Metadata Metadata { get; init; } = new();

    /// <summary>
    /// Maximum serialized size, in bytes, allowed for a single record before the append is
    /// rejected client-side. Defaults to <see cref="DefaultMaxRecordSize"/>.
    /// </summary>
    internal int MaxRecordSize { get; init; } = DefaultMaxRecordSize;

    /// <summary>
    /// Maximum serialized size, in bytes, allowed for the whole transaction before the append is
    /// rejected client-side. Defaults to <see cref="DefaultMaxTransactionSize"/>.
    /// </summary>
    internal int MaxTransactionSize { get; init; } = DefaultMaxTransactionSize;

    /// <summary>
    /// Returns a copy of these options with the given consistency checks appended.
    /// </summary>
    public AppendRecordsOptions AddChecks(params ConsistencyCheck[] checks) =>
        this with { Checks = [..Checks, ..checks] };

    /// <summary>
    /// Returns a copy of these options with the given key-value pair merged into the batch metadata overlay.
    /// </summary>
    public AppendRecordsOptions AddMetadata(string key, object value) =>
        this with { Metadata = new Metadata(Metadata).With(key, value) };
    
    // // its a hack and evil cause record should be immutable.
    // public AppendRecordsOptions AddMetadata(string key, object value) {
    //     Metadata.With(key, value);
    //     return this;
    // }
    
    public bool SkipValidation { get; init; }
}

/// <summary>
/// The result of appending records across multiple streams.
/// </summary>
[PublicAPI]
[method: SetsRequiredMembers]
public record AppendRecordsResponse(StreamPosition[] Revisions, LogPosition Position) {
    public required StreamPosition[] Revisions { get; init; } = Revisions;
    public required LogPosition      Position  { get; init; } = Position;
}

/// <summary>
/// A pre-commit condition that must hold true for the append transaction to succeed.
/// Can be either a <see cref="StreamStateCheck"/> (stream must be at a specific state)
/// or a <see cref="QueryPredicateCheck"/> (a query predicate must evaluate to true).
/// </summary>
public readonly partial record struct ConsistencyCheck : IVariant<StreamStateCheck, QueryPredicateCheck> {
    public static readonly ConsistencyCheck None = new();

    public static StreamStateCheck StreamAtRevision(StreamName stream, Revision revision) =>
        new(stream, revision);

    public static StreamStateCheck StreamExists(StreamName stream) =>
        new(stream, ExpectedStreamState.Exists);

    public static StreamStateCheck StreamDoesNotExist(StreamName stream) =>
        new(stream, ExpectedStreamState.NoStream);

    public static StreamStateCheck StreamIsDeleted(StreamName stream) =>
        new(stream, ExpectedStreamState.Deleted);

    public static StreamStateCheck StreamIsTombstoned(StreamName stream) =>
        new(stream, ExpectedStreamState.Tombstoned);
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

/// <summary>
/// The result of appending records to a single stream. Carries the full position of the
/// last appended record; <see cref="Stream"/> and <see cref="Revision"/> are views over it.
/// </summary>
[PublicAPI]
public record AppendStreamResponse(RecordPosition Position) {
    /// <summary>
    /// The stream the records were appended to.
    /// </summary>
    public StreamName Stream => Position.Stream;

    /// <summary>
    /// The revision of the last appended record.
    /// </summary>
    public Revision Revision => Position.Revision;
}
