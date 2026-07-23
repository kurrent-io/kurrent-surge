// ReSharper disable CheckNamespace

using Kurrent.Surge.Client.Connectors;
using Kurrent.Surge.Client.Errors;

namespace Kurrent.Surge.Client.Exceptions;

[PublicAPI]
public class ConnectorNotFoundException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("CONNECTOR_NOT_FOUND");

    public ConnectorNotFoundException(ConnectorId connectorId)
        : this($"Connector '{connectorId}' was not found.") { }

    public ConnectorNotFoundException(string message) : base(Code, message) { }

    public ConnectorNotFoundException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }
}

[PublicAPI]
public class ConnectorAlreadyExistsException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("CONNECTOR_ALREADY_EXISTS");

    public ConnectorAlreadyExistsException(ConnectorId connectorId)
        : this($"Connector '{connectorId}' already exists.") { }

    public ConnectorAlreadyExistsException(string message) : base(Code, message) { }

    public ConnectorAlreadyExistsException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }
}

[PublicAPI]
public class ConnectorDeletedException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("CONNECTOR_DELETED");

    public ConnectorDeletedException(ConnectorId connectorId)
        : this($"Connector '{connectorId}' has been deleted.") { }

    public ConnectorDeletedException(string message) : base(Code, message) { }

    public ConnectorDeletedException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }
}
