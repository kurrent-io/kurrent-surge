using Google.Protobuf.WellKnownTypes;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Connectors;

public static partial class ConnectorsClientIdiomaticExtensions {
    extension(ConnectorsClient client) {
        #region Create

        public async ValueTask<Success> CreateAsync(
            ConnectorId connectorId,
            string? name,
            Dictionary<string, string?> settings,
            CancellationToken cancellationToken = default
        ) => await client.Create(connectorId, name, settings, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<Success> CreateAsync(
            ConnectorId connectorId,
            Dictionary<string, string?> settings,
            CancellationToken cancellationToken = default
        ) => await client.Create(connectorId, null, settings, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<Success> CreateAsync(
            ConnectorId connectorId,
            string? name,
            Struct configuration,
            CancellationToken cancellationToken = default
        ) => await client.Create(connectorId, name, configuration, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<Success> CreateAsync(
            ConnectorId connectorId,
            Struct configuration,
            CancellationToken cancellationToken = default
        ) => await client.Create(connectorId, null, configuration, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<Success> CreateAsync(
            ConnectorId connectorId,
            CancellationToken cancellationToken = default
        ) => await client.Create(connectorId, null, [], cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region Reconfigure

        public async ValueTask<Success> ReconfigureAsync(
            ConnectorId connectorId,
            Dictionary<string, string?> settings,
            CancellationToken cancellationToken = default
        ) => await client.Reconfigure(connectorId, settings, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<Success> ReconfigureAsync(
            ConnectorId connectorId,
            Struct configuration,
            CancellationToken cancellationToken = default
        ) => await client.Reconfigure(connectorId, configuration, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region Delete

        public async ValueTask<Success> DeleteAsync(
            ConnectorId connectorId,
            CancellationToken cancellationToken = default
        ) => await client.Delete(connectorId, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region Start

        public async ValueTask<Success> StartAsync(
            ConnectorId connectorId,
            ulong? logPosition = null,
            CancellationToken cancellationToken = default
        ) => await client.Start(connectorId, logPosition, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<Success> StartAsync(
            ConnectorId connectorId,
            CancellationToken cancellationToken = default
        ) => await client.Start(connectorId, null, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region Reset

        public async ValueTask<Success> ResetAsync(
            ConnectorId connectorId,
            ulong? logPosition = null,
            CancellationToken cancellationToken = default
        ) => await client.Reset(connectorId, logPosition, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<Success> ResetAsync(
            ConnectorId connectorId,
            CancellationToken cancellationToken = default
        ) => await client.Reset(connectorId, null, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region Stop

        public async ValueTask<Success> StopAsync(
            ConnectorId connectorId,
            CancellationToken cancellationToken = default
        ) => await client.Stop(connectorId, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region Rename

        public async ValueTask<Success> RenameAsync(
            ConnectorId connectorId,
            string name,
            CancellationToken cancellationToken = default
        ) => await client.Rename(connectorId, name, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region List

        public async ValueTask<ConnectorListResult> ListAsync(
            ConnectorListOptions? options = null,
            CancellationToken cancellationToken = default
        ) => await client.List(options, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region GetSettings

        public async ValueTask<ConnectorSettingsResult> GetSettingsAsync(
            ConnectorId connectorId,
            CancellationToken cancellationToken = default
        ) => await client.GetSettings(connectorId, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region GetConfiguration

        public async ValueTask<ConnectorConfigurationResult> GetConfigurationAsync(
            ConnectorId connectorId,
            CancellationToken cancellationToken = default
        ) => await client.GetConfiguration(connectorId, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion
    }
}
