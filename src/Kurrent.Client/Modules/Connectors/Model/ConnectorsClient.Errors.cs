using Kurrent.Client.Exceptions;
using Kurrent.Variant;

namespace Kurrent.Client.Connectors;

[PublicAPI]
public partial class CreateConnectorError : IVariantException<
    ConnectorAlreadyExistsException,
    Exception> {
    public static CreateConnectorError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<CreateConnectorError>(
            ConnectorAlreadyExistsException.From);
}

[PublicAPI]
public partial class ReconfigureConnectorError : IVariantException<
    ConnectorNotFoundException,
    ConnectorDeletedException,
    Exception> {
    public static ReconfigureConnectorError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<ReconfigureConnectorError>(
            ConnectorNotFoundException.From,
            ConnectorDeletedException.From);
}

[PublicAPI]
public partial class DeleteConnectorError : IVariantException<
    ConnectorNotFoundException,
    ConnectorDeletedException,
    Exception> {
    public static DeleteConnectorError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<DeleteConnectorError>(
            ConnectorNotFoundException.From,
            ConnectorDeletedException.From);
}

[PublicAPI]
public partial class StartConnectorError : IVariantException<
    ConnectorNotFoundException,
    ConnectorDeletedException,
    Exception> {
    public static StartConnectorError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<StartConnectorError>(
            ConnectorNotFoundException.From,
            ConnectorDeletedException.From);
}

[PublicAPI]
public partial class ResetConnectorError : IVariantException<
    ConnectorNotFoundException,
    ConnectorDeletedException,
    Exception> {
    public static ResetConnectorError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<ResetConnectorError>(
            ConnectorNotFoundException.From,
            ConnectorDeletedException.From);
}

[PublicAPI]
public partial class StopConnectorError : IVariantException<
    ConnectorNotFoundException,
    ConnectorDeletedException,
    Exception> {
    public static StopConnectorError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<StopConnectorError>(
            ConnectorNotFoundException.From,
            ConnectorDeletedException.From);
}

[PublicAPI]
public partial class RenameConnectorError : IVariantException<
    ConnectorNotFoundException,
    ConnectorDeletedException,
    Exception> {
    public static RenameConnectorError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<RenameConnectorError>(
            ConnectorNotFoundException.From,
            ConnectorDeletedException.From);
}

[PublicAPI]
public partial class ListConnectorsError : IVariantException<Exception> {
    public static ListConnectorsError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<ListConnectorsError>();
}

[PublicAPI]
public partial class GetConnectorSettingsError : IVariantException<
    ConnectorNotFoundException,
    ConnectorDeletedException,
    Exception> {
    public static GetConnectorSettingsError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<GetConnectorSettingsError>(
            ConnectorNotFoundException.From,
            ConnectorDeletedException.From);
}

[PublicAPI]
public partial class GetConnectorConfigurationError : IVariantException<
    ConnectorNotFoundException,
    ConnectorDeletedException,
    Exception> {
    public static GetConnectorConfigurationError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<GetConnectorConfigurationError>(
            ConnectorNotFoundException.From,
            ConnectorDeletedException.From);
}
