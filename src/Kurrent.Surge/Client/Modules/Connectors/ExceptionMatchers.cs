// ReSharper disable CheckNamespace

using Google.Rpc;
using Grpc.Core;
using Kurrent.Surge.Client.Connectors;

using Status = Google.Rpc.Status;

using static Kurrent.Surge.Client.Exceptions.MatcherHelpers;

namespace Kurrent.Surge.Client.Exceptions;

static class ConnectorNotFoundMatcher {
    extension(ConnectorNotFoundException) {
        public static ConnectorNotFoundException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, ConnectorNotFoundException.Code, (d, m, i) => new ConnectorNotFoundException(d, m, i));

        public static OperationExceptionParser Match(ConnectorId connectorId) =>
            OnStatusCode(StatusCode.NotFound, () => new ConnectorNotFoundException(connectorId));
    }
}

static class ConnectorAlreadyExistsMatcher {
    extension(ConnectorAlreadyExistsException) {
        public static ConnectorAlreadyExistsException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, ConnectorAlreadyExistsException.Code, (d, m, i) => new ConnectorAlreadyExistsException(d, m, i));

        public static OperationExceptionParser Match(ConnectorId connectorId) =>
            OnStatusCode(StatusCode.AlreadyExists, () => new ConnectorAlreadyExistsException(connectorId));
    }
}

static class ConnectorDeletedMatcher {
    extension(ConnectorDeletedException) {
        public static ConnectorDeletedException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, ConnectorDeletedException.Code, (d, m, i) => new ConnectorDeletedException(d, m, i));

        public static OperationExceptionParser Match(ConnectorId connectorId) =>
            OnStatusCode(StatusCode.FailedPrecondition, () => new ConnectorDeletedException(connectorId));
    }
}
