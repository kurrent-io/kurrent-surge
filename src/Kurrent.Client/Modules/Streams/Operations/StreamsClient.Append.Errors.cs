using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Kurrent.Client.Exceptions;
using Kurrent.Variant;

namespace Kurrent.Client.Streams;


[PublicAPI]
public partial class AppendRecordsError : IVariantException<
    ConsistencyViolationException,
    MaxAppendRecordSizeExceededException,
    MaxAppendTransactionSizeExceededException,
    Exception> {
    public static AppendRecordsError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<AppendRecordsError>(
            static (s, e) => ConsistencyViolationException.From(s, e),
            static (s, e) => MaxAppendRecordSizeExceededException.From(s, e),
            static (s, e) => MaxAppendTransactionSizeExceededException.From(s, e));
}

[PublicAPI]
public partial class AppendSessionError : IVariantException<
    StreamNotFoundException,
    // StreamAlreadyExistsException,
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
            // static (s, e) => StreamAlreadyExistsException.From(s, e),
            static (s, e) => StreamTombstonedException.From(s, e),
            static (s, e) => StreamRevisionConflictException.From(s, e),
            static (s, e) => StreamAlreadyInAppendSessionException.From(s, e),
            static (s, e) => MaxAppendRecordSizeExceededException.From(s, e),
            static (s, e) => MaxAppendTransactionSizeExceededException.From(s, e),
            static (s, e) => AppendSessionNoRequestsException.From(s, e));
}
