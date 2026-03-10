// ReSharper disable CheckNamespace

using Kurrent.Client.Connectors;
using Resources;
using static System.String;

namespace Kurrent.Client.Exceptions;

[KurrentOperationError("ConnectorNotFound")]
public partial class ConnectorNotFoundException {
    public ConnectorNotFoundException(ConnectorId connectorId)
        : this(Format(ErrorMessages.ConnectorNotFoundException, connectorId)) { }
}

[KurrentOperationError("ConnectorAlreadyExists")]
public partial class ConnectorAlreadyExistsException {
    public ConnectorAlreadyExistsException(ConnectorId connectorId)
        : this(Format(ErrorMessages.ConnectorAlreadyExistsException, connectorId)) { }
}

[KurrentOperationError("ConnectorDeleted")]
public partial class ConnectorDeletedException {
    public ConnectorDeletedException(ConnectorId connectorId)
        : this(Format(ErrorMessages.ConnectorDeletedException, connectorId)) { }
}
