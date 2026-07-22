// ReSharper disable CheckNamespace

using Kurrent.Surge.Client.Connectors;
using Kurrent.Surge.Client.Errors;
using static System.String;

namespace Kurrent.Surge.Client.Exceptions;

[PublicAPI]
public class ConnectorNotFoundException : KurrentException {
    public static ErrorCode Code => KurrentErrorCodes.Connectors.ConnectorNotFound;

    public ConnectorNotFoundException(ConnectorId connectorId)
        : this(Format("Connector '{0}' was not found.", connectorId)) { }

    public ConnectorNotFoundException(string message) : base(Code, message) { }

    public ConnectorNotFoundException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }
}

[PublicAPI]
public class ConnectorAlreadyExistsException : KurrentException {
    public static ErrorCode Code => KurrentErrorCodes.Connectors.ConnectorAlreadyExists;

    public ConnectorAlreadyExistsException(ConnectorId connectorId)
        : this(Format("Connector '{0}' already exists.", connectorId)) { }

    public ConnectorAlreadyExistsException(string message) : base(Code, message) { }

    public ConnectorAlreadyExistsException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }
}

[PublicAPI]
public class ConnectorDeletedException : KurrentException {
    public static ErrorCode Code => KurrentErrorCodes.Connectors.ConnectorDeleted;

    public ConnectorDeletedException(ConnectorId connectorId)
        : this(Format("Connector '{0}' has been deleted.", connectorId)) { }

    public ConnectorDeletedException(string message) : base(Code, message) { }

    public ConnectorDeletedException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }
}
