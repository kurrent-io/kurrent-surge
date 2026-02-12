using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Contracts = KurrentDB.Connectors.Management.Contracts;
using Queries = KurrentDB.Connectors.Management.Contracts.Queries;

namespace Kurrent.Client.Connectors;

public partial class ConnectorsClient {
    /// <summary>
    /// Lists connectors based on the specified filtering criteria.
    /// </summary>
    /// <param name="options">Options for filtering and pagination of connectors.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Result containing the list of connectors or a specific error.</returns>
    public async ValueTask<Result<ConnectorListResult, ListConnectorsError>> List(
        ConnectorListOptions? options = null,
        CancellationToken cancellationToken = default
    ) {
        var request = new Queries.ListConnectors();

        if (options is not null) {
            foreach (var state in options.States)
                request.State.Add((Contracts.ConnectorState)(int)state);

            foreach (var instanceType in options.InstanceTypeNames)
                request.InstanceTypeName.Add(instanceType);

            foreach (var connectorId in options.ConnectorIds)
                request.ConnectorId.Add(connectorId);

            request.IncludeSettings      = options.IncludeSettings;
            request.IncludeConfiguration = options.IncludeConfiguration;
            request.ShowDeleted          = options.ShowDeleted;

            request.Paging = new Queries.Paging {
                Page     = options.Page,
                PageSize = options.PageSize
            };
        }

        try {
            var response = await QueryServiceClient
                .ListAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new ConnectorListResult {
                Items     = response.Items.Select(MapConnector).ToList(),
                TotalSize = response.TotalSize
            };
        }
        catch (RpcException rex) {
            return ListConnectorsError.FromEx(rex);
        }
        catch (Exception ex) {
            return ListConnectorsError.FromEx(ex);
        }
    }

    /// <summary>
    /// Retrieves the configuration settings for a specific connector.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to get settings for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Result containing the connector settings or a specific error.</returns>
    public async ValueTask<Result<ConnectorSettingsResult, GetConnectorSettingsError>> GetSettings(
        ConnectorId connectorId,
        CancellationToken cancellationToken = default
    ) {
        var request = new Queries.GetConnectorSettings {
            ConnectorId = connectorId
        };

        try {
            var response = await QueryServiceClient
                .GetSettingsAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new ConnectorSettingsResult {
                Settings           = MapSettings(response.Settings),
                SettingsUpdateTime = response.SettingsUpdateTime?.ToDateTimeOffset() ?? default
            };
        }
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.NotFound           => GetConnectorSettingsError.FromEx(new ConnectorNotFoundException(connectorId)),
                StatusCode.FailedPrecondition => GetConnectorSettingsError.FromEx(new ConnectorDeletedException(connectorId)),
                _                             => GetConnectorSettingsError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return GetConnectorSettingsError.FromEx(ex);
        }
    }

    /// <summary>
    /// Retrieves the hierarchical configuration for a specific connector.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to get configuration for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Result containing the connector configuration or a specific error.</returns>
    public async ValueTask<Result<ConnectorConfigurationResult, GetConnectorConfigurationError>> GetConfiguration(
        ConnectorId connectorId,
        CancellationToken cancellationToken = default
    ) {
        var request = new Queries.GetConnectorConfiguration {
            ConnectorId = connectorId
        };

        try {
            var response = await QueryServiceClient
                .GetConfigurationAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new ConnectorConfigurationResult {
                Configuration           = response.Configuration,
                ConfigurationUpdateTime = response.ConfigurationUpdateTime?.ToDateTimeOffset() ?? default
            };
        }
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.NotFound           => GetConnectorConfigurationError.FromEx(new ConnectorNotFoundException(connectorId)),
                StatusCode.FailedPrecondition => GetConnectorConfigurationError.FromEx(new ConnectorDeletedException(connectorId)),
                _                             => GetConnectorConfigurationError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return GetConnectorConfigurationError.FromEx(ex);
        }
    }

    static ConnectorDetails MapConnector(Queries.Connector connector) =>
        new() {
            ConnectorId             = connector.ConnectorId,
            InstanceTypeName        = connector.InstanceTypeName,
            Name                    = connector.Name,
            State                   = (ConnectorState)(int)connector.State,
            StateUpdateTime         = connector.StateUpdateTime?.ToDateTimeOffset() ?? default,
            Settings                = MapSettings(connector.Settings),
            SettingsUpdateTime      = connector.SettingsUpdateTime?.ToDateTimeOffset() ?? default,
            Configuration           = connector.Configuration,
            ConfigurationUpdateTime = connector.ConfigurationUpdateTime?.ToDateTimeOffset() ?? default,
            Position = connector.Position is not null
                ? LogPosition.From((long)connector.Position.Value)
                : LogPosition.Unset,
            PositionUpdateTime = connector.PositionUpdateTime?.ToDateTimeOffset() ?? default,
            CreateTime         = connector.CreateTime?.ToDateTimeOffset() ?? default,
            UpdateTime         = connector.UpdateTime?.ToDateTimeOffset() ?? default,
            DeleteTime         = connector.DeleteTime?.ToDateTimeOffset() ?? default,
            ErrorDetails       = connector.ErrorDetails?.Message
        };

    static IReadOnlyDictionary<string, string> MapSettings(
        Google.Protobuf.Collections.MapField<string, string> settings
    ) => new Dictionary<string, string>(settings);
}
