#pragma warning disable CS8604 // Possible null reference argument.

using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Kurrent.Client.Exceptions;
using Kurrent.Variant;

namespace Kurrent.Client.Streams;


public readonly partial record struct ConsistencyCheck : IVariant<RevisionConsistencyCheck, QueryConsistencyCheck>;

public readonly record struct RevisionConsistencyCheck(StreamName Stream, ExpectedStreamState ExpectedState);

public readonly record struct QueryConsistencyCheck(string Expression);


[PublicAPI]
[method: SetsRequiredMembers]
public record AppendRecordsRequest(ImmutableArray<AppendRecord> Records, ImmutableArray<ConsistencyCheck> Checks) {
    public required ImmutableArray<AppendRecord>     Records { get; init; } = Records;
    public required ImmutableArray<ConsistencyCheck> Checks  { get; init; } = Checks;
}

[PublicAPI]
[method: SetsRequiredMembers]
public record AppendRecordsSuccess((string Stream, long Revision)[] Revisions, LogPosition Position) {
    public required (string Stream, long Revision)[] Revisions { get; init; } = Revisions;
    public required LogPosition                      Position  { get; init; } = Position;
}


[PublicAPI]
public partial class AppendRecordsError : IVariantException<
    StreamConsistencyCheckException,
    MaxAppendRecordSizeExceededException,
    MaxAppendTransactionSizeExceededException,
    Exception> {
    public static AppendRecordsError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<AppendRecordsError>(
            static (s, e) => StreamConsistencyCheckException.From(s, e),
            static (s, e) => MaxAppendRecordSizeExceededException.From(s, e),
            static (s, e) => MaxAppendTransactionSizeExceededException.From(s, e));
}


[PublicAPI]
[method: SetsRequiredMembers]
public record AppendStreamRequest(StreamName Stream, ExpectedStreamState ExpectedState, IEnumerable<AppendRecord> Messages) {
    public required StreamName                Stream        { get; init; } = Stream;
    public required IEnumerable<AppendRecord> Messages      { get; init; } = Messages;
    public required ExpectedStreamState       ExpectedState { get; init; } = ExpectedState;

    public static AppendStreamRequestBuilder New() => new();
}

[PublicAPI]
[method: SetsRequiredMembers]
public record AppendStreamSuccess(StreamName Stream, LogPosition Position, StreamRevision StreamRevision) {
    public required StreamName     Stream         { get; init; } = Stream;
    public required LogPosition    Position       { get; init; } = Position;
    public required StreamRevision StreamRevision { get; init; } = StreamRevision;
}

[PublicAPI]
public record AppendSessionSuccess(IReadOnlyList<AppendStreamSuccess> Results, LogPosition Position) : IReadOnlyList<AppendStreamSuccess> {
    public int Count => Results.Count;

    public AppendStreamSuccess this[int index] => Results[index];

    public IEnumerator<AppendStreamSuccess> GetEnumerator() => Results.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

[PublicAPI]
public partial class AppendSessionError : IVariantException<
    StreamNotFoundException,
    StreamAlreadyExistsException,
    StreamTombstonedException,
    StreamRevisionConflictException,
    StreamAlreadyInAppendSessionException,
    MaxAppendRecordSizeExceededException,
    MaxAppendTransactionSizeExceededException,
    AppendSessionNoRequestsException,
    Exception> {
    public static AppendSessionError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<AppendSessionError>(
            static (s, e) => StreamNotFoundException.From(s, e),
            static (s, e) => StreamAlreadyExistsException.From(s, e),
            static (s, e) => StreamTombstonedException.From(s, e),
            static (s, e) => StreamRevisionConflictException.From(s, e),
            static (s, e) => StreamAlreadyInAppendSessionException.From(s, e),
            static (s, e) => MaxAppendRecordSizeExceededException.From(s, e),
            static (s, e) => MaxAppendTransactionSizeExceededException.From(s, e),
            static (s, e) => AppendSessionNoRequestsException.From(s, e));
}
