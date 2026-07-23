// ReSharper disable CheckNamespace

using Google.Rpc;
using Kurrent.Surge.Client.Connectors;

using Status = Google.Rpc.Status;

using static Kurrent.Surge.Client.Exceptions.MatcherHelpers;

namespace Kurrent.Surge.Client.Exceptions;

// The connectors module sends no ErrorInfo and no typed detail, only a status message. The raw shapes
// (KurrentDB.Connectors RpcExceptions) are classified by substring:
//   "Connector {id} not found"      -> ConnectorNotFound      (query NotFound; command FailedPrecondition)
//   "Connector {id} deleted {when}" -> ConnectorDeleted       (command NotFound)
//   "Connector {id} already exists" -> ConnectorAlreadyExists  (create AlreadyExists)
// A command FailedPrecondition that is a state error ("must be stopped", "already running") matches none
// and stays a raw RpcException.

static class ConnectorNotFoundMatcher {
    extension(ConnectorNotFoundException) {
        public static ConnectorNotFoundException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, ConnectorNotFoundException.Code, (d, m, i) => new ConnectorNotFoundException(d, m, i));

        public static OperationExceptionParser Match(ConnectorId connectorId) =>
            OnMessage("not found", () => new ConnectorNotFoundException(connectorId));
    }
}

static class ConnectorAlreadyExistsMatcher {
    extension(ConnectorAlreadyExistsException) {
        public static ConnectorAlreadyExistsException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, ConnectorAlreadyExistsException.Code, (d, m, i) => new ConnectorAlreadyExistsException(d, m, i));

        public static OperationExceptionParser Match(ConnectorId connectorId) =>
            OnMessage("already exists", () => new ConnectorAlreadyExistsException(connectorId));
    }
}

static class ConnectorDeletedMatcher {
    extension(ConnectorDeletedException) {
        public static ConnectorDeletedException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, ConnectorDeletedException.Code, (d, m, i) => new ConnectorDeletedException(d, m, i));

        public static OperationExceptionParser Match(ConnectorId connectorId) =>
            OnMessage("deleted", () => new ConnectorDeletedException(connectorId));
    }
}
