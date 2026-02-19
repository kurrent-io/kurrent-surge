// ReSharper disable CheckNamespace

using Google.Rpc;
using Kurrent.Client.Streams;
using KurrentDB.Protocol.V2.Streams.Errors;
using Resources;
using static System.String;

using ConsistencyCheckError = Kurrent.Client.Streams.ConsistencyCheckError;

namespace Kurrent.Client.Exceptions;

[KurrentOperationError(StreamsError.StreamNotFound)]
public partial class StreamNotFoundException {
    public StreamNotFoundException(StreamName stream) : this(Format(ErrorMessages.StreamNotFoundException, stream)) =>
        Stream = stream;

    public StreamName Stream { get; }
}

[KurrentOperationError(StreamsError.StreamAlreadyExists)]
public partial class StreamAlreadyExistsException;

[KurrentOperationError(StreamsError.StreamDeleted)]
public partial class StreamDeletedException {
    public StreamDeletedException(StreamName stream) : this(Format(ErrorMessages.StreamDeletedException, stream)) =>
        Stream = stream;

    public StreamName Stream { get; }
}

[KurrentOperationError(StreamsError.StreamTombstoned)]
public partial class StreamTombstonedException {
    public StreamTombstonedException(StreamName stream) : this(Format(ErrorMessages.StreamTombstonedException, stream)) =>
        Stream = stream;

    public StreamName Stream { get; }
}

[KurrentOperationError(StreamsError.StreamRevisionConflict)]
public partial class StreamRevisionConflictException {
    public ExpectedStreamState ExpectedRevision { get; private set; } = ExpectedStreamState.Any;
    public ActualStreamState   ActualRevision   { get; private set; } = ActualStreamCondition.NoStream;
    public StreamName          Stream           { get; private set; } = StreamName.None;

    public StreamRevisionConflictException(string operationName, StreamName stream, ExpectedStreamState expectedRevision, ActualStreamState actualRevision)
        : this(Format(ErrorMessages.StreamRevisionConflictException, operationName, stream, expectedRevision, actualRevision)) {
        ExpectedRevision = expectedRevision;
        ActualRevision   = actualRevision;
        Stream           = stream;
    }

    public void AfterParseRpcStatus(Status status, StreamRevisionConflictException self) {
        var details = status.GetDetail<StreamRevisionConflictErrorDetails>()!;
        self.ExpectedRevision = ExpectedStreamState.FromWireValue(details.ExpectedRevision);
        self.ActualRevision   = ActualStreamState.FromWireValue(details.ActualRevision);
        self.Stream           = details.Stream;
    }
}

[KurrentOperationError(StreamsError.ConsistencyCheckFailed)]
public partial class StreamConsistencyCheckException {
    public IReadOnlyDictionary<int, ConsistencyCheckError> FailedChecks { get; private set; } = new Dictionary<int, ConsistencyCheckError>();

    public StreamConsistencyCheckException(Dictionary<int, ConsistencyCheckError> failedChecks)
        : this(Format(ErrorMessages.StreamRevisionConflictException, failedChecks)) {
        FailedChecks = failedChecks;
    }

    public StreamConsistencyCheckException(RevisionConsistencyCheckError failedCheck)
        : this(new Dictionary<int, ConsistencyCheckError> { { 0, failedCheck } }) { }

    public StreamConsistencyCheckException(StreamName stream, ActualStreamState actualState)
        : this(new RevisionConsistencyCheckError { Stream = stream, State  = actualState }) { }

    public void AfterParseRpcStatus(Status status, StreamConsistencyCheckException self) {
        var details = status.GetDetail<ConsistencyCheckFailedErrorDetails>()!;

        self.FailedChecks = details.FailedChecks.ToDictionary<KeyValuePair<uint, KurrentDB.Protocol.V2.Streams.Errors.ConsistencyCheckError>, int, ConsistencyCheckError>(
            kvp => (int)kvp.Key,
            kvp => kvp.Value.TypeCase switch {
                KurrentDB.Protocol.V2.Streams.Errors.ConsistencyCheckError.TypeOneofCase.Revision => new RevisionConsistencyCheckError {
                    Stream = kvp.Value.Revision.Stream,
                    State  = ActualStreamState.FromWireValue(kvp.Value.Revision.ActualRevision)
                },
                _ => ConsistencyCheckError.None
            }
        );
    }
}

[KurrentOperationError(StreamsError.MaxAppendRecordSizeExceeded)]
public partial class MaxAppendRecordSizeExceededException {
    public Guid RecordId         { get; private set; }
    public int  RecordActualSize { get; private set; }
    public int  RecordMaxSize    { get; private set; }

    public void AfterParseRpcStatus(Status status, MaxAppendRecordSizeExceededException self) {
        var details = status.GetDetail<AppendRecordSizeExceededErrorDetails>()!;
        self.RecordId         = Guid.Parse(details.RecordId);
        self.RecordActualSize = details.Size;
        self.RecordMaxSize    = details.MaxSize;
    }
}

[KurrentOperationError(StreamsError.MaxAppendTransactionSizeExceeded)]
public partial class MaxAppendTransactionSizeExceededException {
    public int TransactionActualSize { get; private set; }
    public int TransactionMaxSize    { get; private set; }

    public void AfterParseRpcStatus(Status status, MaxAppendTransactionSizeExceededException self) {
        var details = status.GetDetail<AppendTransactionSizeExceededErrorDetails>()!;
        self.TransactionActualSize = details.Size;
        self.TransactionMaxSize    = details.MaxSize;
    }
}

[KurrentOperationError(StreamsError.StreamAlreadyInAppendSession)]
public partial class StreamAlreadyInAppendSessionException;

[KurrentOperationError(StreamsError.AppendSessionNoRequests)]
public partial class AppendSessionNoRequestsException;

[KurrentOperationError(StreamsError.LogPositionNotFound)]
public partial class LogPositionNotFoundException {
    public LogPositionNotFoundException(ulong position)
        : this($"The provided log position does not exist: {position}.") { }

    public LogPositionNotFoundException(ulong position, string details)
        : this($"The provided log position does not exist: {position}. Details: {details}") { }
}
