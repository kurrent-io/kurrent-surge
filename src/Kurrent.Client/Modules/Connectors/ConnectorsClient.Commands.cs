using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Contracts = KurrentDB.Connectors.Management.Contracts;
using Commands = KurrentDB.Connectors.Management.Contracts.Commands;

namespace Kurrent.Client.Connectors;

public partial class ConnectorsClient {
    /// <summary>
    /// Creates a new connector with the specified configuration.
    /// </summary>
    /// <param name="connectorId">Unique identifier for the connector.</param>
    /// <param name="name">Optional human-readable name for the connector.</param>
    /// <param name="settings">Configuration settings for the connector. Null values are preserved as empty strings.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was created successfully.</returns>
    public async ValueTask<Success> Create(
        ConnectorId connectorId,
        string? name,
        Dictionary<string, string?> settings,
        CancellationToken cancellationToken = default
    ) {
        var request = new Commands.CreateConnector {
            ConnectorId = connectorId
        };

        if (name is not null)
            request.Name = name;

        foreach (var (key, value) in settings)
            request.Settings[key] = value ?? "";

        try {
            await CommandServiceClient
                .CreateAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (Exception ex) {
            var mex = ex.AsWellKnownException(ConnectorAlreadyExistsException.From);

            if (mex is not ConnectorAlreadyExistsException && ex is RpcException { StatusCode: StatusCode.AlreadyExists })
                throw new ConnectorAlreadyExistsException(connectorId);

            throw mex;
        }
    }

    /// <summary>
    /// Creates a new connector with the specified configuration.
    /// </summary>
    /// <param name="connectorId">Unique identifier for the connector.</param>
    /// <param name="name">Optional human-readable name for the connector.</param>
    /// <param name="configuration">Hierarchical configuration for the connector as a Protobuf Struct.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was created successfully.</returns>
    public async ValueTask<Success> Create(
        ConnectorId connectorId,
        string? name,
        Struct configuration,
        CancellationToken cancellationToken = default
    ) {
        var request = new Commands.CreateConnector {
            ConnectorId   = connectorId,
            Configuration = configuration
        };

        if (name is not null)
            request.Name = name;

        try {
            await CommandServiceClient
                .CreateAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (Exception ex) {
            var mex = ex.AsWellKnownException(ConnectorAlreadyExistsException.From);

            if (mex is not ConnectorAlreadyExistsException && ex is RpcException { StatusCode: StatusCode.AlreadyExists })
                throw new ConnectorAlreadyExistsException(connectorId);

            throw mex;
        }
    }

    /// <summary>
    /// Reconfigures an existing connector with new settings.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to reconfigure.</param>
    /// <param name="settings">Updated configuration settings. Null values are preserved as empty strings.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was reconfigured successfully.</returns>
    public async ValueTask<Success> Reconfigure(
        ConnectorId connectorId,
        Dictionary<string, string?> settings,
        CancellationToken cancellationToken = default
    ) {
        var request = new Commands.ReconfigureConnector {
            ConnectorId = connectorId
        };

        foreach (var (key, value) in settings)
            request.Settings[key] = value ?? "";

        try {
            await CommandServiceClient
                .ReconfigureAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (Exception ex) {
            throw ToConnectorException(ex,
                new ConnectorNotFoundException(connectorId),
                new ConnectorDeletedException(connectorId));
        }
    }

    /// <summary>
    /// Reconfigures an existing connector with a new hierarchical configuration.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to reconfigure.</param>
    /// <param name="configuration">Updated hierarchical configuration as a Protobuf Struct.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was reconfigured successfully.</returns>
    public async ValueTask<Success> Reconfigure(
        ConnectorId connectorId,
        Struct configuration,
        CancellationToken cancellationToken = default
    ) {
        var request = new Commands.ReconfigureConnector {
            ConnectorId   = connectorId,
            Configuration = configuration
        };

        try {
            await CommandServiceClient
                .ReconfigureAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (Exception ex) {
            throw ToConnectorException(ex,
                new ConnectorNotFoundException(connectorId),
                new ConnectorDeletedException(connectorId));
        }
    }

    /// <summary>
    /// Deletes an existing connector.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was deleted successfully.</returns>
    public async ValueTask<Success> Delete(
        ConnectorId connectorId,
        CancellationToken cancellationToken = default
    ) {
        var request = new Commands.DeleteConnector {
            ConnectorId = connectorId
        };

        try {
            await CommandServiceClient
                .DeleteAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (Exception ex) {
            throw ToConnectorException(ex,
                new ConnectorNotFoundException(connectorId),
                new ConnectorDeletedException(connectorId));
        }
    }

    /// <summary>
    /// Starts a connector, optionally from a specific log position.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to start.</param>
    /// <param name="logPosition">Optional log position to start processing from.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was started successfully.</returns>
    public async ValueTask<Success> Start(
        ConnectorId connectorId,
        ulong? logPosition = null,
        CancellationToken cancellationToken = default
    ) {
        var request = new Commands.StartConnector {
            ConnectorId = connectorId
        };

        if (logPosition.HasValue) {
            request.StartFrom = new Contracts.StartFromPosition {
                LogPosition = logPosition.Value
            };
        }

        try {
            await CommandServiceClient
                .StartAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (Exception ex) {
            throw ToConnectorException(ex,
                new ConnectorNotFoundException(connectorId),
                new ConnectorDeletedException(connectorId));
        }
    }

    /// <summary>
    /// Resets a connector, optionally to a specific log position.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to reset.</param>
    /// <param name="logPosition">Optional log position to reset processing to.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was reset successfully.</returns>
    public async ValueTask<Success> Reset(
        ConnectorId connectorId,
        ulong? logPosition = null,
        CancellationToken cancellationToken = default
    ) {
        var request = new Commands.ResetConnector {
            ConnectorId = connectorId
        };

        if (logPosition.HasValue) {
            request.StartFrom = new Contracts.StartFromPosition {
                LogPosition = logPosition.Value
            };
        }

        try {
            await CommandServiceClient
                .ResetAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (Exception ex) {
            throw ToConnectorException(ex,
                new ConnectorNotFoundException(connectorId),
                new ConnectorDeletedException(connectorId));
        }
    }

    /// <summary>
    /// Stops a running connector.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to stop.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was stopped successfully.</returns>
    public async ValueTask<Success> Stop(
        ConnectorId connectorId,
        CancellationToken cancellationToken = default
    ) {
        var request = new Commands.StopConnector {
            ConnectorId = connectorId
        };

        try {
            await CommandServiceClient
                .StopAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (Exception ex) {
            throw ToConnectorException(ex,
                new ConnectorNotFoundException(connectorId),
                new ConnectorDeletedException(connectorId));
        }
    }

    /// <summary>
    /// Renames an existing connector.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to rename.</param>
    /// <param name="name">New display name for the connector.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was renamed successfully.</returns>
    public async ValueTask<Success> Rename(
        ConnectorId connectorId,
        string name,
        CancellationToken cancellationToken = default
    ) {
        var request = new Commands.RenameConnector {
            ConnectorId = connectorId,
            Name        = name
        };

        try {
            await CommandServiceClient
                .RenameAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (Exception ex) {
            throw ToConnectorException(ex,
                new ConnectorNotFoundException(connectorId),
                new ConnectorDeletedException(connectorId));
        }
    }
}
