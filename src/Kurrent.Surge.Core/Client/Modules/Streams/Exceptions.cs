// ReSharper disable CheckNamespace

using Kurrent.Surge.Client.Errors;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Client.Exceptions;

/// <remarks>
/// Plain contract types: they carry error data and nothing else. Each exposes its
/// <see cref="ErrorCode"/> so callers and retry predicates can name the error, a constructor for
/// raising it directly, and a constructor the client's transport mapper fills from a wire status.
/// Nothing here knows what gRPC is.
/// </remarks>
[PublicAPI]
public class StreamNotFoundException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("STREAM_NOT_FOUND");

    public StreamNotFoundException(StreamName stream)
        : this($"Stream '{stream}' was not found.") => Stream = stream;

    public StreamNotFoundException(string message) : base(Code, message) { }

    public StreamNotFoundException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public StreamName Stream { get; init; } = StreamName.Unset;
}

[PublicAPI]
public class StreamAlreadyExistsException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("STREAM_ALREADY_EXISTS");

    public StreamAlreadyExistsException(StreamName stream, Revision actualRevision)
        : this($"Operation failed due to a revision conflict on stream '{stream}'. Expected revision: {ExpectedStreamState.NoStream}. Actual revision: {actualRevision}.") {
        Stream         = stream;
        ActualRevision = actualRevision;
    }

    public StreamAlreadyExistsException(string message) : base(Code, message) { }

    public StreamAlreadyExistsException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public StreamName Stream         { get; init; } = StreamName.Unset;
    public Revision   ActualRevision { get; init; } = Revision.Unset;
}

[PublicAPI]
public class StreamDeletedException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("STREAM_DELETED");

    public StreamDeletedException(StreamName stream)
        : this($"Stream '{stream}' has been soft deleted. It will not be visible in the stream list, until it is restored by appending to it again.") =>
        Stream = stream;

    public StreamDeletedException(string message) : base(Code, message) { }

    public StreamDeletedException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public StreamName Stream { get; init; } = StreamName.Unset;
}

[PublicAPI]
public class StreamTombstonedException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("STREAM_TOMBSTONED");

    public StreamTombstonedException(StreamName stream)
        : this($"Stream '{stream}' has been tombstoned. It has been permanently removed from the system and cannot be restored.") =>
        Stream = stream;

    public StreamTombstonedException(string message) : base(Code, message) { }

    public StreamTombstonedException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public StreamName Stream { get; init; } = StreamName.Unset;
}

[PublicAPI]
public class StreamRevisionConflictException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("STREAM_REVISION_CONFLICT");

    public StreamRevisionConflictException(StreamName stream, Revision expectedRevision, Revision actualRevision)
        : this($"Operation failed due to a revision conflict on stream '{stream}'. Expected revision: {expectedRevision}. Actual revision: {actualRevision}.") {
        Stream           = stream;
        ExpectedRevision = expectedRevision;
        ActualRevision   = actualRevision;
    }

    public StreamRevisionConflictException(string message) : base(Code, message) { }

    public StreamRevisionConflictException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public StreamName Stream           { get; init; } = StreamName.Unset;
    public Revision   ExpectedRevision { get; init; } = Revision.Unset;
    public Revision   ActualRevision   { get; init; } = Revision.Unset;
}

[PublicAPI]
public class AppendConsistencyViolationException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("APPEND_CONSISTENCY_VIOLATION");

    public AppendConsistencyViolationException(IReadOnlyList<ConsistencyViolation> violations)
        : this($"Append failed: {violations.Count} consistency check(s) violated.") =>
        Violations = violations;

    public AppendConsistencyViolationException(StreamStateViolation violation) : this([violation]) { }

    public AppendConsistencyViolationException(QueryPredicateViolation violation) : this([violation]) { }

    public AppendConsistencyViolationException(string message) : base(Code, message) { }

    public AppendConsistencyViolationException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public IReadOnlyList<ConsistencyViolation> Violations { get; init; } = [];
}

[PublicAPI]
public class MaxAppendRecordSizeExceededException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("MAX_APPEND_RECORD_SIZE_EXCEEDED");

    public MaxAppendRecordSizeExceededException(string message) : base(Code, message) { }

    public MaxAppendRecordSizeExceededException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public Guid RecordId         { get; init; }
    public int  RecordActualSize { get; init; }
    public int  RecordMaxSize    { get; init; }
}

[PublicAPI]
public class MaxAppendTransactionSizeExceededException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("MAX_APPEND_TRANSACTION_SIZE_EXCEEDED");

    public MaxAppendTransactionSizeExceededException(string message) : base(Code, message) { }

    public MaxAppendTransactionSizeExceededException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public int TransactionActualSize { get; init; }
    public int TransactionMaxSize    { get; init; }
}

[PublicAPI]
public class StreamAlreadyInAppendSessionException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("STREAM_ALREADY_IN_APPEND_SESSION");

    public StreamAlreadyInAppendSessionException(string message) : base(Code, message) { }

    public StreamAlreadyInAppendSessionException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }
}

[PublicAPI]
public class AppendSessionNoRequestsException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("APPEND_SESSION_NO_REQUESTS");

    public AppendSessionNoRequestsException(string message) : base(Code, message) { }

    public AppendSessionNoRequestsException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }
}

[PublicAPI]
public class LogPositionNotFoundException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("LOG_POSITION_NOT_FOUND");

    public LogPositionNotFoundException(ulong position)
        : this($"Failed to find log position: {position}") { }

    // The originating failure is taken as a plain Exception, not an RpcException: the detail we want
    // is its message tail, and typing the parameter to the transport would drag gRPC into the contract.
    //
    // The message it carries looks like:
    //   StatusCode="Unknown", Detail="
    //     Unexpected ReadAllResult: InvalidPosition >>
    //     There is not enough space to read full record (length prefix: 143170). Actual pre-position: 144798632.
    //     Something is seriously wrong in chunk #1-1 (chunk-000001.000000).
    //   "
    public LogPositionNotFoundException(ulong position, Exception originalException)
        : this($"Failed to find log position: {position} Details: {originalException.Message.Split(">>").Last().Trim()}") { }

    public LogPositionNotFoundException(string message) : base(Code, message) { }

    public LogPositionNotFoundException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }
}
