using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Queries = KurrentDB.Connectors.Management.Contracts.Queries;

namespace Kurrent.Client.Connectors;

public partial class ConnectorsClient {
    /// <summary>
    /// Lists connectors based on the specified filtering criteria.
    /// </summary>
    /// <param name="options">Options for filtering and pagination of connectors.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="ConnectorListResult"/> containing the matching connectors.</returns>
    public async ValueTask<ConnectorListResult> List(
        ConnectorListOptions? options = null,
        CancellationToken cancellationToken = default
    ) {
        var request = new Queries.ListConnectors();

        if (options is not null) {
            foreach (var state in options.States)
                request.State.Add((KurrentDB.Connectors.Management.Contracts.ConnectorState)(int)state);

            foreach (var instanceType in options.InstanceTypeNames)
                request.InstanceTypeName.Add(instanceType);

            foreach (var connectorId in options.ConnectorIds)
                request.ConnectorId.Add(connectorId);

            request.IncludeSettings      = options.IncludeSettings;
            request.IncludeConfiguration = options.IncludeConfiguration;
            request.ShowDeleted          = options.ShowDeleted;

            request.Paging = new() {
                Page     = options.Page,
                PageSize = options.PageSize
            };
        }

        try {
            var response = await QueryServiceClient
                .ListAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new() {
                Items     = response.Items.Select(MapConnector).ToList(),
                TotalSize = response.TotalSize
            };
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }
    }

    /// <summary>
    /// Retrieves the configuration settings for a specific connector.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to get settings for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="ConnectorSettingsResult"/> containing the connector settings.</returns>
    public async ValueTask<ConnectorSettingsResult> GetSettings(
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

            return new() {
                Settings           = MapSettings(response.Settings),
                SettingsUpdateTime = response.SettingsUpdateTime?.ToDateTimeOffset() ?? default
            };
        }
        catch (Exception ex) {
            throw ex.ToException(
                ConnectorNotFoundException.From,
                ConnectorDeletedException.From,

                // StatusCode fallbacks
                StatusCode.NotFound.MapTo(new ConnectorNotFoundException(connectorId)),
                StatusCode.FailedPrecondition.MapTo(new ConnectorDeletedException(connectorId)));
        }
    }

    /// <summary>
    /// Retrieves the hierarchical configuration for a specific connector.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to get configuration for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="ConnectorConfigurationResult"/> containing the connector configuration.</returns>
    public async ValueTask<ConnectorConfigurationResult> GetConfiguration(
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

            return new() {
                Configuration           = response.Configuration,
                ConfigurationUpdateTime = response.ConfigurationUpdateTime?.ToDateTimeOffset() ?? default
            };
        }
        catch (Exception ex) {
            throw ex.ToException(
                ConnectorNotFoundException.From,
                ConnectorDeletedException.From,

                // StatusCode fallbacks
                StatusCode.NotFound.MapTo(new ConnectorNotFoundException(connectorId)),
                StatusCode.FailedPrecondition.MapTo(new ConnectorDeletedException(connectorId)));
        }
    }

    static ConnectorDetails MapConnector(Queries.Connector connector) =>
        new() {
            ConnectorId             = connector.ConnectorId,
            InstanceTypeName        = connector.InstanceTypeName,
            Name                    = connector.Name,
            State                   = (ConnectorState)(int)connector.State,
            Position                = LogPosition.From(connector.Position),
            StateUpdateTime         = connector.StateUpdateTime?.ToDateTimeOffset() ?? default,
            Settings                = MapSettings(connector.Settings),
            SettingsUpdateTime      = connector.SettingsUpdateTime?.ToDateTimeOffset() ?? default,
            Configuration           = connector.Configuration,
            ConfigurationUpdateTime = connector.ConfigurationUpdateTime?.ToDateTimeOffset() ?? default,
            PositionUpdateTime      = connector.PositionUpdateTime?.ToDateTimeOffset() ?? default,
            CreateTime              = connector.CreateTime?.ToDateTimeOffset() ?? default,
            UpdateTime              = connector.UpdateTime?.ToDateTimeOffset() ?? default,
            DeleteTime              = connector.DeleteTime?.ToDateTimeOffset() ?? default,
            ErrorDetails            = connector.ErrorDetails?.Message,
        };

    static IReadOnlyDictionary<string, string> MapSettings(
        Google.Protobuf.Collections.MapField<string, string> settings
    ) => new Dictionary<string, string>(settings);
}
