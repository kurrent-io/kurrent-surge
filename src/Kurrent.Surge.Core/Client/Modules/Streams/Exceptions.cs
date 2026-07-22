// ReSharper disable CheckNamespace

using Kurrent.Surge.Client.Errors;
using Kurrent.Surge.Client.Streams;
using static System.String;

namespace Kurrent.Surge.Client.Exceptions;

/// <remarks>
/// Plain contract types: they carry error data and nothing else. Each exposes its
/// <see cref="ErrorCode"/> so callers and retry predicates can name the error, a constructor for
/// raising it directly, and a constructor the client's transport mapper fills from a wire status.
/// Nothing here knows what gRPC is.
/// </remarks>
[PublicAPI]
public class StreamNotFoundException : KurrentException {
    public static ErrorCode Code => KurrentErrorCodes.Streams.StreamNotFound;

    public StreamNotFoundException(StreamName stream)
        : this(Format("Stream '{0}' was not found.", stream)) => Stream = stream;

    public StreamNotFoundException(string message) : base(Code, message) { }

    public StreamNotFoundException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public StreamName Stream { get; init; } = StreamName.Unset;
}

[PublicAPI]
public class StreamAlreadyExistsException : KurrentException {
    public static ErrorCode Code => KurrentErrorCodes.Streams.StreamAlreadyExists;

    // The leading "" and the resulting argument shift are carried over verbatim from before this type
    // was moved. The rendered message is wrong — the stream lands in the "Expected revision" slot and
    // actualRevision is dropped — but fixing it changes user-visible text, so it is reported, not fixed.
    public StreamAlreadyExistsException(StreamName stream, Revision actualRevision)
        : this(Format("Operation failed due to a revision conflict on stream '{0}'. Expected revision: {1}. Actual revision: {2}.", "", stream, ExpectedStreamState.NoStream, actualRevision)) {
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
    public static ErrorCode Code => KurrentErrorCodes.Streams.StreamDeleted;

    public StreamDeletedException(StreamName stream)
        : this(Format("Stream '{0}' has been soft deleted. It will not be visible in the stream list, until it is restored by appending to it again.", stream)) =>
        Stream = stream;

    public StreamDeletedException(string message) : base(Code, message) { }

    public StreamDeletedException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public StreamName Stream { get; init; } = StreamName.Unset;
}

[PublicAPI]
public class StreamTombstonedException : KurrentException {
    public static ErrorCode Code => KurrentErrorCodes.Streams.StreamTombstoned;

    public StreamTombstonedException(StreamName stream)
        : this(Format("Stream '{0}' has been tombstoned. It has been permanently removed from the system and cannot be restored.", stream)) =>
        Stream = stream;

    public StreamTombstonedException(string message) : base(Code, message) { }

    public StreamTombstonedException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public StreamName Stream { get; init; } = StreamName.Unset;
}

[PublicAPI]
public class StreamRevisionConflictException : KurrentException {
    public static ErrorCode Code => KurrentErrorCodes.Streams.StreamRevisionConflict;

    public StreamRevisionConflictException(StreamName stream, Revision expectedRevision, Revision actualRevision)
        : this(Format("Operation failed due to a revision conflict on stream '{0}'. Expected revision: {1}. Actual revision: {2}.", stream, expectedRevision, actualRevision)) {
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
    public static ErrorCode Code => KurrentErrorCodes.Streams.AppendConsistencyViolation;

    // Carried over verbatim. This throws FormatException: violations binds to Format(string, object?),
    // not the params overload, so {1} and {2} have no argument. Reported, not fixed — the mapper never
    // reaches it, so the break is latent.
    public AppendConsistencyViolationException(IReadOnlyList<ConsistencyViolation> violations)
        : this(Format("Operation failed due to a revision conflict on stream '{0}'. Expected revision: {1}. Actual revision: {2}.", violations)) =>
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
    public static ErrorCode Code => KurrentErrorCodes.Streams.MaxAppendRecordSizeExceeded;

    public MaxAppendRecordSizeExceededException(string message) : base(Code, message) { }

    public MaxAppendRecordSizeExceededException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public Guid RecordId         { get; init; }
    public int  RecordActualSize { get; init; }
    public int  RecordMaxSize    { get; init; }
}

[PublicAPI]
public class MaxAppendTransactionSizeExceededException : KurrentException {
    public static ErrorCode Code => KurrentErrorCodes.Streams.MaxAppendTransactionSizeExceeded;

    public MaxAppendTransactionSizeExceededException(string message) : base(Code, message) { }

    public MaxAppendTransactionSizeExceededException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    public int TransactionActualSize { get; init; }
    public int TransactionMaxSize    { get; init; }
}

[PublicAPI]
public class StreamAlreadyInAppendSessionException : KurrentException {
    public static ErrorCode Code => KurrentErrorCodes.Streams.StreamAlreadyInAppendSession;

    public StreamAlreadyInAppendSessionException(string message) : base(Code, message) { }

    public StreamAlreadyInAppendSessionException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }
}

[PublicAPI]
public class AppendSessionNoRequestsException : KurrentException {
    public static ErrorCode Code => KurrentErrorCodes.Streams.AppendSessionNoRequests;

    public AppendSessionNoRequestsException(string message) : base(Code, message) { }

    public AppendSessionNoRequestsException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }
}

[PublicAPI]
public class LogPositionNotFoundException : KurrentException {
    public static ErrorCode Code => KurrentErrorCodes.Streams.LogPositionNotFound;

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
