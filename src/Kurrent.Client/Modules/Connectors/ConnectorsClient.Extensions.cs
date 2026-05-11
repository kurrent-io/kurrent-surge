using Kurrent.Client.Streams;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Client.Connectors;

public static partial class ConnectorsClientExtensions {
    extension(ConnectorsClient client) {
        #region ->> Create <<-

        public ValueTask<Success> Create(
            ConnectorId connectorId
        ) => client.Create(connectorId, [], CancellationToken.None);

        public ValueTask<Success> Create(
            ConnectorId connectorId,
            CancellationToken cancellationToken
        ) => client.Create(connectorId, [], cancellationToken);

        public ValueTask<Success> Create(
            ConnectorId connectorId,
            Dictionary<string, string> settings
        ) => client.Create(connectorId, settings, CancellationToken.None);

        public ValueTask<Success> Create(
            ConnectorId connectorId,
            IConfiguration configuration
        ) => client.Create(connectorId, configuration.ToFlatDictionary(), CancellationToken.None);

        public ValueTask<Success> Create(
            ConnectorId connectorId,
            IConfiguration configuration,
            CancellationToken cancellationToken
        ) => client.Create(connectorId, configuration.ToFlatDictionary(), cancellationToken);

        public ValueTask<Success> Create(
            ConnectorId connectorId,
            string json
        ) => client.Create(connectorId, json.ToFlatDictionary(), CancellationToken.None);

        public ValueTask<Success> Create(
            ConnectorId connectorId,
            string json,
            CancellationToken cancellationToken
        ) => client.Create(connectorId, json.ToFlatDictionary(), cancellationToken);

        public ValueTask<Success> Create(
            ConnectorId connectorId,
            string name,
            Dictionary<string, string> settings
        ) => client.Create(connectorId, name, settings, CancellationToken.None);

        public ValueTask<Success> Create(
            ConnectorId connectorId,
            string name,
            IConfiguration configuration
        ) => client.Create(connectorId, name, configuration.ToFlatDictionary(), CancellationToken.None);

        public ValueTask<Success> Create(
            ConnectorId connectorId,
            string name,
            IConfiguration configuration,
            CancellationToken cancellationToken
        ) => client.Create(connectorId, name, configuration.ToFlatDictionary(), cancellationToken);

        public ValueTask<Success> Create(
            ConnectorId connectorId,
            string name,
            string json
        ) => client.Create(connectorId, name, json.ToFlatDictionary(), CancellationToken.None);

        public ValueTask<Success> Create(
            ConnectorId connectorId,
            string name,
            string json,
            CancellationToken cancellationToken
        ) => client.Create(connectorId, name, json.ToFlatDictionary(), cancellationToken);

        #endregion

        #region ->> Reconfigure <<-

        public ValueTask<Success> Reconfigure(
            ConnectorId connectorId,
            Dictionary<string, string> settings
        ) => client.Reconfigure(connectorId, settings, CancellationToken.None);

        public ValueTask<Success> Reconfigure(
            ConnectorId connectorId,
            IConfiguration configuration
        ) => client.Reconfigure(connectorId, configuration.ToFlatDictionary(), CancellationToken.None);

        public ValueTask<Success> Reconfigure(
            ConnectorId connectorId,
            IConfiguration configuration,
            CancellationToken cancellationToken
        ) => client.Reconfigure(connectorId, configuration.ToFlatDictionary(), cancellationToken);

        public ValueTask<Success> Reconfigure(
            ConnectorId connectorId,
            string json
        ) => client.Reconfigure(connectorId, json.ToFlatDictionary(), CancellationToken.None);

        public ValueTask<Success> Reconfigure(
            ConnectorId connectorId,
            string json,
            CancellationToken cancellationToken
        ) => client.Reconfigure(connectorId, json.ToFlatDictionary(), cancellationToken);

        #endregion

        #region ->> Delete <<-

        public ValueTask<Success> Delete(
            ConnectorId connectorId
        ) => client.Delete(connectorId, CancellationToken.None);

        #endregion

        #region ->> Start <<-

        public ValueTask<Success> Start(
            ConnectorId connectorId
        ) => client.Start(connectorId, CancellationToken.None);

        public ValueTask<Success> Start(
            ConnectorId connectorId,
            LogPosition logPosition
        ) => client.Start(connectorId, logPosition, CancellationToken.None);

        #endregion

        #region ->> Reset <<-

        public ValueTask<Success> Reset(
            ConnectorId connectorId
        ) => client.Reset(connectorId, CancellationToken.None);

        public ValueTask<Success> Reset(
            ConnectorId connectorId,
            ulong logPosition
        ) => client.Reset(connectorId, logPosition, CancellationToken.None);

        #endregion

        #region ->> Stop <<-

        public ValueTask<Success> Stop(
            ConnectorId connectorId
        ) => client.Stop(connectorId, CancellationToken.None);

        #endregion

        #region ->> Rename <<-

        public ValueTask<Success> Rename(
            ConnectorId connectorId,
            string name
        ) => client.Rename(connectorId, name, CancellationToken.None);

        #endregion

        #region ->> List <<-

        public ValueTask<ConnectorListResult> List(
        ) => client.List(new ConnectorListOptions(), CancellationToken.None);

        public ValueTask<ConnectorListResult> List(
            CancellationToken cancellationToken
        ) => client.List(new ConnectorListOptions(), cancellationToken);

        public ValueTask<ConnectorListResult> List(
            ConnectorListOptions options
        ) => client.List(options, CancellationToken.None);

        #endregion

        #region ->> GetSettings <<-

        public ValueTask<ConnectorSettingsResult> GetSettings(
            ConnectorId connectorId
        ) => client.GetSettings(connectorId, CancellationToken.None);

        #endregion

        #region ->> GetConfiguration <<-

        public ValueTask<ConnectorConfigurationResult> GetConfiguration(
            ConnectorId connectorId
        ) => client.GetConfiguration(connectorId, CancellationToken.None);

        #endregion
    }
}
