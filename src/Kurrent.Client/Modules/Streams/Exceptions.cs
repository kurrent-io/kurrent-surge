// ReSharper disable CheckNamespace

using System.Diagnostics;
using Google.Rpc;
using Kurrent.Client.Streams;
using Resources;
using static System.String;

using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Client.Exceptions;

[KurrentOperationError(Contracts.Errors.StreamsError.StreamNotFound)]
public partial class StreamNotFoundException {
    public StreamNotFoundException(StreamName stream) : this(Format(ErrorMessages.StreamNotFoundException, stream)) =>
        Stream = stream;

    public StreamName Stream { get; }
}

[KurrentOperationError(Contracts.Errors.StreamsError.StreamAlreadyExists)]
public partial class StreamAlreadyExistsException {
    public Revision   ActualRevision { get; private set; } = Revision.Unset;
    public StreamName Stream         { get; private set; } = StreamName.None;

    public StreamAlreadyExistsException(StreamName stream, Revision actualRevision)
        : this(Format(ErrorMessages.StreamRevisionConflictException, "", stream, ExpectedStreamState.NoStream, actualRevision)) {
        ActualRevision = actualRevision;
        Stream         = stream;
    }
}

[KurrentOperationError(Contracts.Errors.StreamsError.StreamDeleted)]
public partial class StreamDeletedException {
    public StreamDeletedException(StreamName stream) : this(Format(ErrorMessages.StreamDeletedException, stream)) =>
        Stream = stream;

    public StreamName Stream { get; }
}

[KurrentOperationError(Contracts.Errors.StreamsError.StreamTombstoned)]
public partial class StreamTombstonedException {
    public StreamTombstonedException(StreamName stream) : this(Format(ErrorMessages.StreamTombstonedException, stream)) =>
        Stream = stream;

    public StreamName Stream { get; }
}

[KurrentOperationError(Contracts.Errors.StreamsError.StreamRevisionConflict)]
public partial class StreamRevisionConflictException {
    public Revision   ExpectedRevision { get; private set; } = Revision.Unset;
    public Revision   ActualRevision   { get; private set; } = Revision.Unset;
    public StreamName Stream           { get; private set; } = StreamName.None;

    public StreamRevisionConflictException(StreamName stream, Revision expectedRevision, Revision actualRevision)
        : this(Format(ErrorMessages.StreamRevisionConflictException, stream, expectedRevision, actualRevision)) {
        ExpectedRevision = expectedRevision;
        ActualRevision   = actualRevision;
        Stream           = stream;
    }

    public void AfterParseRpcStatus(Status status, StreamRevisionConflictException self) {
        var details = status.GetDetail<Contracts.Errors.StreamRevisionConflictErrorDetails>()!;
        self.ExpectedRevision = ExpectedStreamState.FromWireValue(details.ExpectedRevision);
        self.ActualRevision   = ActualStreamState.FromWireValue(details.ActualRevision);
        self.Stream           = details.Stream;
    }
}

[KurrentOperationError(Contracts.Errors.StreamsError.ConsistencyViolation)]
public partial class ConsistencyViolationException {
    public IReadOnlyDictionary<int, ConsistencyCheckFailure> FailedChecks { get; private set; } = new Dictionary<int, ConsistencyCheckFailure>();

    public ConsistencyViolationException(Dictionary<int, ConsistencyCheckFailure> failedChecks)
        : this(Format(ErrorMessages.StreamRevisionConflictException, failedChecks)) {
        FailedChecks = failedChecks;
    }

    public ConsistencyViolationException(StreamConsistencyCheckFailure failedCheck)
        : this(new Dictionary<int, ConsistencyCheckFailure> { { 0, failedCheck } }) { }

    public ConsistencyViolationException(QueryConsistencyCheckFailure failedCheck)
        : this(new Dictionary<int, ConsistencyCheckFailure> { { 0, failedCheck } }) { }

    public void AfterParseRpcStatus(Status status, ConsistencyViolationException self) {
        var details = status.GetDetail<Contracts.Errors.ConsistencyViolationErrorDetails>()!;

        self.FailedChecks = details.FailedChecks.ToDictionary(
            kvp => (int)kvp.Key,
            kvp => {
                ConsistencyCheckFailure f = kvp.Value.TypeCase switch {
                    Contracts.Errors.ConsistencyCheckFailure.TypeOneofCase.Revision => new StreamConsistencyCheckFailure {
                        Stream        = kvp.Value.Revision.Stream,
                        ExpectedState = ExpectedStreamState.FromWireValue(kvp.Value.Revision.ActualRevision), //TODO SS: duh!
                        ActualState   = ActualStreamState.FromWireValue(kvp.Value.Revision.ActualRevision)
                    },
                    _ => throw new UnreachableException($"Unknown consistency check error type: {kvp.Value.TypeCase}")
                };

                return f;
            }
        );

        // self.FailedChecks = details.FailedChecks.ToDictionary<KeyValuePair<uint, Contracts.Errors.ConsistencyCheckFailure>, int, Contracts.Errors.ConsistencyCheckFailure>(
        //     kvp => (int)kvp.Key,
        //     kvp => kvp.Value.TypeCase switch {
        //         Contracts.Errors.ConsistencyCheckFailure.TypeOneofCase.Revision => new StreamConsistencyCheckFailure {
        //             Stream        = kvp.Value.Revision.Stream,
        //             ExpectedState = ExpectedStreamState.FromWireValue(kvp.Value.Revision.ActualRevision), //TODO SS: duh!
        //             ActualState   = ActualStreamState.FromWireValue(kvp.Value.Revision.ActualRevision)
        //         },
        //         _ => throw new UnreachableException($"Unknown consistency check error type: {kvp.Value.TypeCase}")
        //     }
        // );
    }
}

[KurrentOperationError(Contracts.Errors.StreamsError.MaxAppendRecordSizeExceeded)]
public partial class MaxAppendRecordSizeExceededException {
    public Guid RecordId         { get; private set; }
    public int  RecordActualSize { get; private set; }
    public int  RecordMaxSize    { get; private set; }

    public void AfterParseRpcStatus(Status status, MaxAppendRecordSizeExceededException self) {
        var details = status.GetDetail<Contracts.Errors.AppendRecordSizeExceededErrorDetails>()!;
        self.RecordId         = Guid.Parse(details.RecordId);
        self.RecordActualSize = details.Size;
        self.RecordMaxSize    = details.MaxSize;
    }
}

[KurrentOperationError(Contracts.Errors.StreamsError.MaxAppendTransactionSizeExceeded)]
public partial class MaxAppendTransactionSizeExceededException {
    public int TransactionActualSize { get; private set; }
    public int TransactionMaxSize    { get; private set; }

    public void AfterParseRpcStatus(Status status, MaxAppendTransactionSizeExceededException self) {
        var details = status.GetDetail<Contracts.Errors.AppendTransactionSizeExceededErrorDetails>()!;
        self.TransactionActualSize = details.Size;
        self.TransactionMaxSize    = details.MaxSize;
    }
}

[KurrentOperationError(Contracts.Errors.StreamsError.StreamAlreadyInAppendSession)]
public partial class StreamAlreadyInAppendSessionException;

[KurrentOperationError(Contracts.Errors.StreamsError.AppendSessionNoRequests)]
public partial class AppendSessionNoRequestsException;

[KurrentOperationError(Contracts.Errors.StreamsError.LogPositionNotFound)]
public partial class LogPositionNotFoundException {
    public LogPositionNotFoundException(ulong position)
        : this($"The provided log position does not exist: {position}.") { }

    public LogPositionNotFoundException(ulong position, string details)
        : this($"The provided log position does not exist: {position}. Details: {details}") { }
}
