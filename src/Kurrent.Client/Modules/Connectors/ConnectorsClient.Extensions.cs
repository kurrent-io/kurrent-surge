using Kurrent.Client.Streams;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Client.Connectors;

public static partial class ConnectorsClientExtensions {
    extension(ConnectorsClient client) {
        #region ->> Create <<-

        public ValueTask<Success> Create(ConnectorId id) =>
            client.Create(id, [], CancellationToken.None);

        public ValueTask<Success> Create(ConnectorId id, CancellationToken ct) =>
            client.Create(id, [], ct);

        public ValueTask<Success> Create(ConnectorId id, Dictionary<string, string> configuration) =>
            client.Create(id, configuration, CancellationToken.None);

        public ValueTask<Success> Create(ConnectorId id, IConfiguration configuration) =>
            client.Create(id, configuration.ToFlatDictionary(), CancellationToken.None);

        public ValueTask<Success> Create(ConnectorId id, IConfiguration configuration, CancellationToken ct) =>
            client.Create(id, configuration.ToFlatDictionary(), ct);

        public ValueTask<Success> Create(ConnectorId id, string configuration) =>
            client.Create(id, configuration.ToFlatDictionary(), CancellationToken.None);

        public ValueTask<Success> Create(ConnectorId id, string configuration, CancellationToken ct) =>
            client.Create(id, configuration.ToFlatDictionary(), ct);

        public ValueTask<Success> Create(ConnectorId id, string name, Dictionary<string, string> configuration) =>
            client.Create(id, name, configuration, CancellationToken.None);

        public ValueTask<Success> Create(ConnectorId id, string name, IConfiguration configuration) =>
            client.Create(id, name, configuration.ToFlatDictionary(), CancellationToken.None);

        public ValueTask<Success> Create(
            ConnectorId id, string name, IConfiguration configuration, CancellationToken ct
        ) => client.Create(id, name, configuration.ToFlatDictionary(), ct);

        public ValueTask<Success> Create(ConnectorId id, string name, string configuration) =>
            client.Create(id, name, configuration.ToFlatDictionary(), CancellationToken.None);

        public ValueTask<Success> Create(ConnectorId id, string name, string configuration, CancellationToken ct)
            => client.Create(id, name, configuration.ToFlatDictionary(), ct);

        #endregion

        #region ->> Reconfigure <<-

        public ValueTask<Success> Reconfigure(ConnectorId id, Dictionary<string, string> configuration)
            => client.Reconfigure(id, configuration, CancellationToken.None);

        public ValueTask<Success> Reconfigure(ConnectorId id, IConfiguration configuration)
            => client.Reconfigure(id, configuration.ToFlatDictionary(), CancellationToken.None);

        public ValueTask<Success> Reconfigure(ConnectorId id, IConfiguration configuration, CancellationToken ct)
            => client.Reconfigure(id, configuration.ToFlatDictionary(), ct);

        public ValueTask<Success> Reconfigure(ConnectorId id, string configuration)
            => client.Reconfigure(id, configuration.ToFlatDictionary(), CancellationToken.None);

        public ValueTask<Success> Reconfigure(ConnectorId id, string configuration, CancellationToken ct)
            => client.Reconfigure(id, configuration.ToFlatDictionary(), ct);

        #endregion

        #region ->> Delete <<-

        public ValueTask<Success> Delete(ConnectorId id) => client.Delete(id, CancellationToken.None);

        #endregion

        #region ->> Start <<-

        public ValueTask<Success> Start(ConnectorId id) => client.Start(id, CancellationToken.None);

        public ValueTask<Success> Start(ConnectorId id, LogPosition logPosition)
            => client.Start(id, logPosition, CancellationToken.None);

        #endregion

        #region ->> Reset <<-

        public ValueTask<Success> Reset(ConnectorId id) => client.Reset(id, CancellationToken.None);

        public ValueTask<Success> Reset(ConnectorId id, LogPosition logPosition) =>
            client.Reset(id, logPosition, CancellationToken.None);

        #endregion

        #region ->> Stop <<-

        public ValueTask<Success> Stop(ConnectorId id) => client.Stop(id, CancellationToken.None);

        #endregion

        #region ->> Rename <<-

        public ValueTask<Success> Rename(ConnectorId id, string name) =>
            client.Rename(id, name, CancellationToken.None);

        #endregion

        #region ->> List <<-

        public ValueTask<ConnectorListResult> List() => client.List(new ConnectorListOptions(), CancellationToken.None);

        public ValueTask<ConnectorListResult> List(CancellationToken ct) => client.List(new ConnectorListOptions(), ct);

        public ValueTask<ConnectorListResult> List(ConnectorListOptions options) =>
            client.List(options, CancellationToken.None);

        #endregion

        #region ->> GetSettings <<-

        public ValueTask<ConnectorSettingsResult> GetSettings(ConnectorId id) =>
            client.GetSettings(id, CancellationToken.None);

        #endregion

        #region ->> GetConfiguration <<-

        public ValueTask<ConnectorConfigurationResult> GetConfiguration(ConnectorId id) =>
            client.GetConfiguration(id, CancellationToken.None);

        #endregion
    }
}
