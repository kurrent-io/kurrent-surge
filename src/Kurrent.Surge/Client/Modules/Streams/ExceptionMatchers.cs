// ReSharper disable CheckNamespace

using Google.Rpc;
using Kurrent.Surge.Client.Streams;

using StreamsErrors = KurrentDB.Protocol.V2.Streams.Errors;
using Status = Google.Rpc.Status;

using static Kurrent.Surge.Client.Exceptions.MatcherHelpers;

namespace Kurrent.Surge.Client.Exceptions;

static class StreamNotFoundMatcher {
    extension(StreamNotFoundException) {
        public static StreamNotFoundException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, StreamNotFoundException.Code, (d, m, i) => new StreamNotFoundException(d, m, i));
    }
}

static class StreamAlreadyExistsMatcher {
    extension(StreamAlreadyExistsException) {
        public static StreamAlreadyExistsException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, StreamAlreadyExistsException.Code, (d, m, i) => new StreamAlreadyExistsException(d, m, i));
    }
}

static class StreamDeletedMatcher {
    extension(StreamDeletedException) {
        public static StreamDeletedException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, StreamDeletedException.Code, (d, m, i) => new StreamDeletedException(d, m, i));
    }
}

static class StreamTombstonedMatcher {
    extension(StreamTombstonedException) {
        public static StreamTombstonedException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, StreamTombstonedException.Code, (d, m, i) => new StreamTombstonedException(d, m, i));
    }
}

static class StreamAlreadyInAppendSessionMatcher {
    extension(StreamAlreadyInAppendSessionException) {
        public static StreamAlreadyInAppendSessionException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, StreamAlreadyInAppendSessionException.Code, (d, m, i) => new StreamAlreadyInAppendSessionException(d, m, i));
    }
}

static class AppendSessionNoRequestsMatcher {
    extension(AppendSessionNoRequestsException) {
        public static AppendSessionNoRequestsException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, AppendSessionNoRequestsException.Code, (d, m, i) => new AppendSessionNoRequestsException(d, m, i));
    }
}

static class LogPositionNotFoundMatcher {
    extension(LogPositionNotFoundException) {
        public static LogPositionNotFoundException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, LogPositionNotFoundException.Code, (d, m, i) => new LogPositionNotFoundException(d, m, i));
    }
}

static class StreamRevisionConflictMatcher {
    extension(StreamRevisionConflictException) {
        public static StreamRevisionConflictException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, StreamRevisionConflictException.Code, (d, m, i) => {
                var detail = status.GetDetail<StreamsErrors.StreamRevisionConflictErrorDetails>();

                return new StreamRevisionConflictException(d, m, i) {
                    Stream           = detail?.Stream ?? StreamName.Unset,
                    ExpectedRevision = ExpectedStreamState.FromWireValue(detail?.ExpectedRevision ?? -2),
                    ActualRevision   = ActualStreamState.FromWireValue(detail?.ActualRevision ?? -1)
                };
            });
    }
}

static class AppendConsistencyViolationMatcher {
    extension(AppendConsistencyViolationException) {
        public static AppendConsistencyViolationException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, AppendConsistencyViolationException.Code, (d, m, i) => new AppendConsistencyViolationException(d, m, i) {
                Violations = ToViolations(status.GetDetail<StreamsErrors.AppendConsistencyViolationErrorDetails>())
            });
    }

    static IReadOnlyList<ConsistencyViolation> ToViolations(StreamsErrors.AppendConsistencyViolationErrorDetails? details) =>
        details is null
            ? []
            : [
                .. details.Violations.Select(v => {
                    ConsistencyViolation violation = v.TypeCase switch {
                        StreamsErrors.ConsistencyViolation.TypeOneofCase.StreamState => new StreamStateViolation {
                            Stream        = v.StreamState.Stream,
                            ExpectedState = ExpectedStreamState.FromWireValue(v.StreamState.ExpectedState),
                            ActualState   = ActualStreamState.FromWireValue(v.StreamState.ActualState)
                        },
                        // StreamsErrors.ConsistencyViolation.TypeOneofCase.QueryPredicate => new QueryPredicateViolation {
                        //     Message = v.QueryPredicate.Message
                        // },
                        _ => throw new ArgumentOutOfRangeException($"Unknown consistency check error type: {v.TypeCase}")
                    };

                    return violation with { CheckIndex = v.CheckIndex };
                })
            ];
}

static class MaxAppendRecordSizeExceededMatcher {
    extension(MaxAppendRecordSizeExceededException) {
        public static MaxAppendRecordSizeExceededException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, MaxAppendRecordSizeExceededException.Code, (d, m, i) => {
                var detail = status.GetDetail<StreamsErrors.AppendRecordSizeExceededErrorDetails>();

                return new MaxAppendRecordSizeExceededException(d, m, i) {
                    RecordId         = Guid.TryParse(detail?.RecordId, out var recordId) ? recordId : Guid.Empty,
                    RecordActualSize = detail?.Size ?? 0,
                    RecordMaxSize    = detail?.MaxSize ?? 0
                };
            });
    }
}

static class MaxAppendTransactionSizeExceededMatcher {
    extension(MaxAppendTransactionSizeExceededException) {
        public static MaxAppendTransactionSizeExceededException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, MaxAppendTransactionSizeExceededException.Code, (d, m, i) => {
                var detail = status.GetDetail<StreamsErrors.AppendTransactionSizeExceededErrorDetails>();

                return new MaxAppendTransactionSizeExceededException(d, m, i) {
                    TransactionActualSize = detail?.Size ?? 0,
                    TransactionMaxSize    = detail?.MaxSize ?? 0
                };
            });
    }
}
