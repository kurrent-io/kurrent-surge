// ReSharper disable CheckNamespace

using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Google.Rpc;
using Humanizer;
using Kurrent.Client.Errors;
using Kurrent.Client.Streams;
using Kurrent.GoogleRpc;
using KurrentDB.Protocol.V2.Streams.Errors;
using Resources;
using static System.String;

namespace Kurrent.Client.Exceptions;

[PublicAPI, CompilerGenerated, GeneratedCode("KurrentOperationExceptionGenerator", "1.0.0")]
public partial class StreamNotFoundException : KurrentException, IKurrentOperationException<StreamNotFoundException> {
    // public static ErrorCode Code { get; } = typeof(StreamNotFoundException).GetCustomAttribute<KurrentOperationErrorAttribute>()!.ErrorCode;

    public static ErrorCode Code { get; } = ErrorCode.FromEnum(StreamsError.StreamNotFound);

    public StreamNotFoundException(string message) : base(Code, message, innerException: null) { }

    public StreamNotFoundException(Status status) : base(Code, status) {
        try {
            ((IKurrentOperationException<StreamNotFoundException>)this).AfterParseRpcStatus(status, this);
        }
        catch (Exception ex) {
            throw new UnreachableException("EVERYTHING IS BROKEN! The StreamNotFoundException::AfterParseRpcStatus hook threw an exception.", ex);
        }
    }

    public static bool TryParseRpcStatus(Status status, ErrorInfo? errorInfo, [MaybeNullWhen(false)] out StreamNotFoundException ex) {

        var omg =    Code.Value.Equals(errorInfo?.Reason ?? "", StringComparison.OrdinalIgnoreCase) ||
                     status.GetGrpcStatusCode().ToString().Underscore().ToUpperInvariant().Equals(Code.Value, StringComparison.OrdinalIgnoreCase);

        //var fodax = Code.MatchesRpcStatusReason(status, errorInfo);

        if (omg) {
            ex = new StreamNotFoundException(status);
            return true;
        }

        ex = null;
        return false;

        //return (ex = Code.MatchesRpcStatusReason(status, errorInfo) ? new StreamNotFoundException(status) : null) is not null;
    }

    public static StreamNotFoundException? From(Status status, ErrorInfo? errorInfo = null) =>
        TryParseRpcStatus(status, errorInfo, out var ex)
            ? ex
            : null;
}

// [KurrentOperationError(StreamsError.StreamNotFound)]
public partial class StreamNotFoundException {
    public StreamNotFoundException(StreamName stream)
        : this(Format(ErrorMessages.StreamNotFoundException, stream)) { }
}

[KurrentOperationError(StreamsError.StreamAlreadyExists)]
public partial class StreamAlreadyExistsException;

[KurrentOperationError(StreamsError.StreamDeleted)]
public partial class StreamDeletedException {
    public StreamDeletedException(StreamName stream)
        : this(Format(ErrorMessages.StreamDeletedException, stream)) { }
}

[KurrentOperationError(StreamsError.StreamTombstoned)]
public partial class StreamTombstonedException {
    public StreamTombstonedException(StreamName stream)
        : this(Format(ErrorMessages.StreamTombstonedException, stream)) { }
}

[KurrentOperationError(StreamsError.StreamRevisionConflict)]
public partial class StreamRevisionConflictException {
    public StreamRevision ExpectedRevision { get; private set; } = StreamRevision.Unset;
    public StreamRevision ActualRevision   { get; private set; } = StreamRevision.Unset;

    public StreamRevisionConflictException(string operationName, StreamName stream, StreamRevision expectedRevision, StreamRevision actualRevision)
        : this(Format(ErrorMessages.StreamRevisionConflictException, operationName, stream, expectedRevision, actualRevision)) {
        ExpectedRevision = expectedRevision;
        ActualRevision   = actualRevision;
    }

    public void AfterParseRpcStatus(Status status, StreamRevisionConflictException self) {
        var details = status.GetDetail<StreamRevisionConflictErrorDetails>()!;
        self.ExpectedRevision = details.ExpectedRevision;
        self.ActualRevision   = details.ActualRevision;
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
