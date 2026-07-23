// ReSharper disable CheckNamespace

using Google.Rpc;
using Kurrent.Surge.Client.Indexes;
using Kurrent.Surge.Client.Streams;

using IndexesErrors = KurrentDB.Protocol.V2.Indexes.Errors;
using Status = Google.Rpc.Status;

using static Kurrent.Surge.Client.Exceptions.MatcherHelpers;
using static Kurrent.Surge.Client.Exceptions.IndexMatcherHelpers;

namespace Kurrent.Surge.Client.Exceptions;

static class IndexNotFoundMatcher {
    extension(IndexNotFoundException) {
        public static IndexNotFoundException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, IndexNotFoundException.Code, (d, m, i) => new IndexNotFoundException(d, m, i) {
                Index = ToIndexName(status.GetDetail<IndexesErrors.IndexNotFoundErrorDetails>()?.Name)
            });
    }
}

static class IndexAlreadyExistsMatcher {
    extension(IndexAlreadyExistsException) {
        public static IndexAlreadyExistsException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, IndexAlreadyExistsException.Code, (d, m, i) => new IndexAlreadyExistsException(d, m, i) {
                Index = ToIndexName(status.GetDetail<IndexesErrors.IndexAlreadyExistsErrorDetails>()?.Name)
            });
    }
}

static class IndexesNotReadyMatcher {
    extension(IndexesNotReadyException) {
        public static IndexesNotReadyException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, IndexesNotReadyException.Code, (d, m, i) => {
                var detail = status.GetDetail<IndexesErrors.IndexesNotReadyErrorDetails>();

                return new IndexesNotReadyException(d, m, i) {
                    CurrentPosition = LogPosition.From(detail?.CurrentPosition),
                    TargetPosition  = LogPosition.From(detail?.TargetPosition)
                };
            });
    }
}

static class SecondaryIndexingDisabledMatcher {
    extension(SecondaryIndexingDisabledException) {
        public static SecondaryIndexingDisabledException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, SecondaryIndexingDisabledException.Code, (d, m, i) => new SecondaryIndexingDisabledException(d, m, i));
    }
}

static class IndexMatcherHelpers {
    internal static IndexName ToIndexName(string? name) =>
        name is { Length: > 0 } ? IndexName.From(name) : IndexName.Unset;
}
