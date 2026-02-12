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
    /// <returns>Result indicating success or a specific error.</returns>
    public async ValueTask<Result<Success, CreateConnectorError>> Create(
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
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.AlreadyExists => CreateConnectorError.FromEx(new ConnectorAlreadyExistsException(connectorId)),
                _                        => CreateConnectorError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return CreateConnectorError.FromEx(ex);
        }
    }

    /// <summary>
    /// Creates a new connector with the specified configuration.
    /// </summary>
    /// <param name="connectorId">Unique identifier for the connector.</param>
    /// <param name="name">Optional human-readable name for the connector.</param>
    /// <param name="configuration">Hierarchical configuration for the connector as a Protobuf Struct.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Result indicating success or a specific error.</returns>
    public async ValueTask<Result<Success, CreateConnectorError>> Create(
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
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.AlreadyExists => CreateConnectorError.FromEx(new ConnectorAlreadyExistsException(connectorId)),
                _                        => CreateConnectorError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return CreateConnectorError.FromEx(ex);
        }
    }

    /// <summary>
    /// Reconfigures an existing connector with new settings.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to reconfigure.</param>
    /// <param name="settings">Updated configuration settings. Null values are preserved as empty strings.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Result indicating success or a specific error.</returns>
    public async ValueTask<Result<Success, ReconfigureConnectorError>> Reconfigure(
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
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.NotFound           => ReconfigureConnectorError.FromEx(new ConnectorNotFoundException(connectorId)),
                StatusCode.FailedPrecondition => ReconfigureConnectorError.FromEx(new ConnectorDeletedException(connectorId)),
                _                             => ReconfigureConnectorError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return ReconfigureConnectorError.FromEx(ex);
        }
    }

    /// <summary>
    /// Reconfigures an existing connector with a new hierarchical configuration.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to reconfigure.</param>
    /// <param name="configuration">Updated hierarchical configuration as a Protobuf Struct.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Result indicating success or a specific error.</returns>
    public async ValueTask<Result<Success, ReconfigureConnectorError>> Reconfigure(
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
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.NotFound           => ReconfigureConnectorError.FromEx(new ConnectorNotFoundException(connectorId)),
                StatusCode.FailedPrecondition => ReconfigureConnectorError.FromEx(new ConnectorDeletedException(connectorId)),
                _                             => ReconfigureConnectorError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return ReconfigureConnectorError.FromEx(ex);
        }
    }

    /// <summary>
    /// Deletes an existing connector.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Result indicating success or a specific error.</returns>
    public async ValueTask<Result<Success, DeleteConnectorError>> Delete(
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
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.NotFound           => DeleteConnectorError.FromEx(new ConnectorNotFoundException(connectorId)),
                StatusCode.FailedPrecondition => DeleteConnectorError.FromEx(new ConnectorDeletedException(connectorId)),
                _                             => DeleteConnectorError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return DeleteConnectorError.FromEx(ex);
        }
    }

    /// <summary>
    /// Starts a connector, optionally from a specific log position.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to start.</param>
    /// <param name="logPosition">Optional log position to start processing from.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Result indicating success or a specific error.</returns>
    public async ValueTask<Result<Success, StartConnectorError>> Start(
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
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.NotFound           => StartConnectorError.FromEx(new ConnectorNotFoundException(connectorId)),
                StatusCode.FailedPrecondition => StartConnectorError.FromEx(new ConnectorDeletedException(connectorId)),
                _                             => StartConnectorError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return StartConnectorError.FromEx(ex);
        }
    }

    /// <summary>
    /// Resets a connector, optionally to a specific log position.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to reset.</param>
    /// <param name="logPosition">Optional log position to reset processing to.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Result indicating success or a specific error.</returns>
    public async ValueTask<Result<Success, ResetConnectorError>> Reset(
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
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.NotFound           => ResetConnectorError.FromEx(new ConnectorNotFoundException(connectorId)),
                StatusCode.FailedPrecondition => ResetConnectorError.FromEx(new ConnectorDeletedException(connectorId)),
                _                             => ResetConnectorError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return ResetConnectorError.FromEx(ex);
        }
    }

    /// <summary>
    /// Stops a running connector.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to stop.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Result indicating success or a specific error.</returns>
    public async ValueTask<Result<Success, StopConnectorError>> Stop(
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
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.NotFound           => StopConnectorError.FromEx(new ConnectorNotFoundException(connectorId)),
                StatusCode.FailedPrecondition => StopConnectorError.FromEx(new ConnectorDeletedException(connectorId)),
                _                             => StopConnectorError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return StopConnectorError.FromEx(ex);
        }
    }

    /// <summary>
    /// Renames an existing connector.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to rename.</param>
    /// <param name="name">New display name for the connector.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Result indicating success or a specific error.</returns>
    public async ValueTask<Result<Success, RenameConnectorError>> Rename(
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
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.NotFound           => RenameConnectorError.FromEx(new ConnectorNotFoundException(connectorId)),
                StatusCode.FailedPrecondition => RenameConnectorError.FromEx(new ConnectorDeletedException(connectorId)),
                _                             => RenameConnectorError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return RenameConnectorError.FromEx(ex);
        }
    }
}
