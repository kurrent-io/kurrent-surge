using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Client.Connectors;

public interface IConnectorCommands {
    ValueTask<Success> Create(ConnectorId id, string name, Dictionary<string, string> settings, CancellationToken ct = default);

    ValueTask<Success> Reconfigure(ConnectorId id, Dictionary<string, string> settings, CancellationToken ct = default);

    ValueTask<Success> Delete(ConnectorId id, CancellationToken ct = default);

    ValueTask<Success> Start(ConnectorId id, CancellationToken ct = default);

    ValueTask<Success> Start(ConnectorId id, LogPosition position, CancellationToken ct = default);

    ValueTask<Success> Reset(ConnectorId id, CancellationToken ct = default);

    ValueTask<Success> Reset(ConnectorId id, LogPosition position, CancellationToken ct = default);

    ValueTask<Success> Stop(ConnectorId id, CancellationToken ct = default);

    ValueTask<Success> Rename(ConnectorId id, string name, CancellationToken ct = default);
}

public interface IConnectorQueries {
    ValueTask<ConnectorListResult> List(ConnectorListOptions options, CancellationToken ct = default);

    ValueTask<ConnectorSettingsResult> GetSettings(ConnectorId connectorId, CancellationToken ct = default);

    ValueTask<ConnectorConfigurationResult> GetConfiguration(ConnectorId connectorId, CancellationToken ct = default);
}

public interface IConnectorsClient : IConnectorCommands, IConnectorQueries;
