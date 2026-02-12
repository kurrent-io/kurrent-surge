using Google.Protobuf.WellKnownTypes;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Connectors;

public static partial class ConnectorsClientExtensions {
    extension(ConnectorsClient client) {
        #region Create

        public ValueTask<Result<Success, CreateConnectorError>> Create(
            ConnectorId connectorId,
            Dictionary<string, string?> settings,
            CancellationToken cancellationToken = default
        ) => client.Create(connectorId, null, settings, cancellationToken);

        public ValueTask<Result<Success, CreateConnectorError>> Create(
            ConnectorId connectorId,
            Struct configuration,
            CancellationToken cancellationToken = default
        ) => client.Create(connectorId, null, configuration, cancellationToken);

        public ValueTask<Result<Success, CreateConnectorError>> Create(
            ConnectorId connectorId,
            CancellationToken cancellationToken = default
        ) => client.Create(connectorId, null, [], cancellationToken);

        #endregion

        #region Start

        public ValueTask<Result<Success, StartConnectorError>> Start(
            ConnectorId connectorId,
            CancellationToken cancellationToken = default
        ) => client.Start(connectorId, null, cancellationToken);

        #endregion

        #region Reset

        public ValueTask<Result<Success, ResetConnectorError>> Reset(
            ConnectorId connectorId,
            CancellationToken cancellationToken = default
        ) => client.Reset(connectorId, null, cancellationToken);

        #endregion
    }
}
