// ReSharper disable CheckNamespace

using Kurrent.Client.Connectors;
using static System.String;

namespace Kurrent.Client.Exceptions;

[KurrentOperationError("ConnectorNotFound")]
public partial class ConnectorNotFoundException {
    public ConnectorNotFoundException(ConnectorId connectorId)
        : this(Format("Connector '{0}' was not found.", connectorId)) { }
}

[KurrentOperationError("ConnectorAlreadyExists")]
public partial class ConnectorAlreadyExistsException {
    public ConnectorAlreadyExistsException(ConnectorId connectorId)
        : this(Format("Connector '{0}' already exists.", connectorId)) { }
}

[KurrentOperationError("ConnectorDeleted")]
public partial class ConnectorDeletedException {
    public ConnectorDeletedException(ConnectorId connectorId)
        : this(Format("Connector '{0}' has been deleted.", connectorId)) { }
}
