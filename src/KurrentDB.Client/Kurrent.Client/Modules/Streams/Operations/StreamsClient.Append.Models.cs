#pragma warning disable CS8604 // Possible null reference argument.

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Kurrent.Client.Exceptions;
using Kurrent.Variant;

namespace Kurrent.Client.Streams;

[PublicAPI]
[method: SetsRequiredMembers]
public record AppendStreamRequest(string Stream, ExpectedStreamState ExpectedState, IEnumerable<AppendRecord> Messages) {
    public required string                    Stream        { get; init; } = Stream;
    public required IEnumerable<AppendRecord> Messages      { get; init; } = Messages;
    public required ExpectedStreamState       ExpectedState { get; init; } = ExpectedState;

    public static AppendStreamRequestBuilder New() => new();
}

[PublicAPI]
[method: SetsRequiredMembers]
public record AppendStreamSuccess(string Stream, LogPosition Position, StreamRevision StreamRevision) {
    public required string         Stream         { get; init; } = Stream;
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
