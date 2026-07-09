// ReSharper disable CheckNamespace
// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using Grpc.Core;
using Kurrent.Surge.Client.Streams;
using static System.String;

using Contracts = KurrentDB.Protocol.V2.Streams;
using Status = Google.Rpc.Status;

namespace Kurrent.Surge.Client.Exceptions;

[KurrentOperationError(Contracts.Errors.StreamsError.StreamNotFound)]
public partial class StreamNotFoundException {
    public StreamNotFoundException(StreamName stream) : this(Format("Stream '{0}' was not found.", stream)) =>
        Stream = stream;

    public StreamName Stream { get; }
}

[KurrentOperationError(Contracts.Errors.StreamsError.StreamAlreadyExists)]
public partial class StreamAlreadyExistsException {
    public Revision   ActualRevision { get; private set; } = Revision.Unset;
    public StreamName Stream         { get; private set; } = StreamName.Unset;

    public StreamAlreadyExistsException(StreamName stream, Revision actualRevision)
        : this(Format("Operation failed due to a revision conflict on stream '{0}'. Expected revision: {1}. Actual revision: {2}.", "", stream, ExpectedStreamState.NoStream, actualRevision)) {
        ActualRevision = actualRevision;
        Stream         = stream;
    }
}

[KurrentOperationError(Contracts.Errors.StreamsError.StreamDeleted)]
public partial class StreamDeletedException {
    public StreamDeletedException(StreamName stream) : this(Format("Stream '{0}' has been soft deleted. It will not be visible in the stream list, until it is restored by appending to it again.", stream)) =>
        Stream = stream;

    public StreamName Stream { get; }
}

[KurrentOperationError(Contracts.Errors.StreamsError.StreamTombstoned)]
public partial class StreamTombstonedException {
    public StreamTombstonedException(StreamName stream) : this(Format("Stream '{0}' has been tombstoned. It has been permanently removed from the system and cannot be restored.", stream)) =>
        Stream = stream;

    public StreamName Stream { get; }
}

[KurrentOperationError(Contracts.Errors.StreamsError.StreamRevisionConflict)]
public partial class StreamRevisionConflictException {
    public Revision   ExpectedRevision { get; private set; } = Revision.Unset;
    public Revision   ActualRevision   { get; private set; } = Revision.Unset;
    public StreamName Stream           { get; private set; } = StreamName.Unset;

    public StreamRevisionConflictException(StreamName stream, Revision expectedRevision, Revision actualRevision)
        : this(Format(format: "Operation failed due to a revision conflict on stream '{0}'. Expected revision: {1}. Actual revision: {2}.", args: [stream, expectedRevision, actualRevision])) {
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

[KurrentOperationError(Contracts.Errors.StreamsError.AppendConsistencyViolation)]
public partial class AppendConsistencyViolationException {
    public IReadOnlyList<ConsistencyViolation> Violations { get; private set; } = [];

    public AppendConsistencyViolationException(IReadOnlyList<ConsistencyViolation> violations)
        : this(Format("Operation failed due to a revision conflict on stream '{0}'. Expected revision: {1}. Actual revision: {2}.", violations)) =>
        Violations = violations;

    public AppendConsistencyViolationException(StreamStateViolation violation) : this([violation]) { }

    public AppendConsistencyViolationException(QueryPredicateViolation violation) : this([violation]) { }

    public void AfterParseRpcStatus(Status status, AppendConsistencyViolationException self) {
        var details = status.GetDetail<Contracts.Errors.AppendConsistencyViolationErrorDetails>();

        if (details is null) return;

        self.Violations = details.Violations.Select(v => {
            ConsistencyViolation violation = v.TypeCase switch {
                Contracts.Errors.ConsistencyViolation.TypeOneofCase.StreamState => new StreamStateViolation {
                    Stream        = v.StreamState.Stream,
                    ExpectedState = ExpectedStreamState.FromWireValue(v.StreamState.ExpectedState),
                    ActualState   = ActualStreamState.FromWireValue(v.StreamState.ActualState)
                },
                // Contracts.Errors.ConsistencyViolation.TypeOneofCase.QueryPredicate => new QueryPredicateViolation {
                //     Message = v.QueryPredicate.Message
                // },
                _ => throw new ArgumentOutOfRangeException($"Unknown consistency check error type: {v.TypeCase}")
            };

            return violation with { CheckIndex = v.CheckIndex };
        }).ToList();
    }
}

[KurrentOperationError(Contracts.Errors.StreamsError.MaxAppendRecordSizeExceeded)]
public partial class MaxAppendRecordSizeExceededException {
    public Guid RecordId         { get; internal set; }
    public int  RecordActualSize { get; internal set; }
    public int  RecordMaxSize    { get; internal set; }

    public void AfterParseRpcStatus(Status status, MaxAppendRecordSizeExceededException self) {
        var details = status.GetDetail<Contracts.Errors.AppendRecordSizeExceededErrorDetails>()!;
        self.RecordId         = Guid.Parse(details.RecordId);
        self.RecordActualSize = details.Size;
        self.RecordMaxSize    = details.MaxSize;
    }
}

[KurrentOperationError(Contracts.Errors.StreamsError.MaxAppendTransactionSizeExceeded)]
public partial class MaxAppendTransactionSizeExceededException {
    public int TransactionActualSize { get; internal set; }
    public int TransactionMaxSize    { get; internal set; }

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
        : this($"Failed to find log position: {position}") { }

    public LogPositionNotFoundException(ulong position, RpcException originalException)
        : this($"Failed to find log position: {position} Details: {originalException.Message.Split(">>").Last().Trim()}") { }

    public static bool IsMatch(RpcException rex) =>
        rex.StatusCode == StatusCode.Unknown &&
        rex.Message.Contains("InvalidPosition >>", StringComparison.OrdinalIgnoreCase);

    // StatusCode="Unknown", Detail="
    //   Unexpected ReadAllResult: InvalidPosition >>
    //   There is not enough space to read full record (length prefix: 143170). Actual pre-position: 144798632.
    //   Something is seriously wrong in chunk #1-1 (chunk-000001.000000).
    // "
}
