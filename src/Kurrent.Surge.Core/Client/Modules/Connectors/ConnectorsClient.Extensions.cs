using Microsoft.Extensions.Configuration;

namespace Kurrent.Surge.Client.Connectors;

public static partial class ConnectorsClientExtensions {
    extension(IConnectorsClient client) {
        #region ->> Create <<-

        public ValueTask<Success> Create(ConnectorId id, CancellationToken ct = default) =>
            client.Create(id, id.Value, [], ct);

        public ValueTask<Success> Create(ConnectorId id, Dictionary<string, string> configuration, CancellationToken ct = default) =>
            client.Create(id, id.Value, configuration, ct);

        public ValueTask<Success> Create(ConnectorId id, IConfiguration configuration, CancellationToken ct = default) =>
            client.Create(id, id.Value, configuration.ToFlatDictionary(), ct);

        public ValueTask<Success> Create(ConnectorId id, string configuration, CancellationToken ct = default) =>
            client.Create(id, id.Value, configuration.ToFlatDictionary(), ct);

        public ValueTask<Success> Create(ConnectorId id, string name, IConfiguration configuration, CancellationToken ct = default) =>
            client.Create(id, name, configuration.ToFlatDictionary(), ct);

        public ValueTask<Success> Create(ConnectorId id, string name, string configuration, CancellationToken ct = default) =>
            client.Create(id, name, configuration.ToFlatDictionary(), ct);

        #endregion

        #region ->> Reconfigure <<-

        public ValueTask<Success> Reconfigure(ConnectorId id, IConfiguration configuration, CancellationToken ct = default) =>
            client.Reconfigure(id, configuration.ToFlatDictionary(), ct);

        public ValueTask<Success> Reconfigure(ConnectorId id, string configuration, CancellationToken ct = default) =>
            client.Reconfigure(id, configuration.ToFlatDictionary(), ct);

        #endregion

        #region ->> List <<-

        public ValueTask<ConnectorListResult> List(CancellationToken ct = default) =>
            client.List(new ConnectorListOptions(), ct);

        #endregion
    }
}
