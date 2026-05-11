using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Commands = KurrentDB.Connectors.Management.Contracts.Commands;

namespace Kurrent.Client.Connectors;

public partial class ConnectorsClient {
    #region ->> Create <<-

    /// <summary>
    /// Creates a new connector with the specified settings. The server uses the connector id as the display name.
    /// </summary>
    /// <param name="connectorId">Unique identifier for the connector.</param>
    /// <param name="settings">Configuration settings for the connector.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was created successfully.</returns>
    public ValueTask<Success> Create(
        ConnectorId connectorId,
        Dictionary<string, string> settings,
        CancellationToken ct
    ) => CreateCore(connectorId, name: null, settings, ct);

    /// <summary>
    /// Creates a new connector with the specified display name and settings.
    /// </summary>
    /// <param name="connectorId">Unique identifier for the connector.</param>
    /// <param name="name">Human-readable display name for the connector.</param>
    /// <param name="settings">Configuration settings for the connector.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was created successfully.</returns>
    public ValueTask<Success> Create(
        ConnectorId connectorId,
        string name,
        Dictionary<string, string> settings,
        CancellationToken ct
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return CreateCore(connectorId, name, settings, ct);
    }

    async ValueTask<Success> CreateCore(
        ConnectorId connectorId,
        string? name,
        Dictionary<string, string> settings,
        CancellationToken ct
    ) {
        if (connectorId.IsUnset)
            throw new ArgumentException("Connector id cannot be empty.", nameof(connectorId));

        var request = new Commands.CreateConnector {
            ConnectorId = connectorId
        };

        if (name is not null)
            request.Name = name;

        foreach (var (key, value) in settings)
            request.Settings[key] = value;

        try {
            await CommandServiceClient
                .CreateAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (Exception ex) {
            throw ex.ToException(
                ConnectorAlreadyExistsException.From,
                // StatusCode fallback
                StatusCode.AlreadyExists.MapTo(new ConnectorAlreadyExistsException(connectorId)));
        }
    }

    #endregion

    #region ->> Reconfigure <<-

    /// <summary>
    /// Reconfigures an existing connector with new settings.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to reconfigure.</param>
    /// <param name="settings">Updated configuration settings.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was reconfigured successfully.</returns>
    public async ValueTask<Success> Reconfigure(
        ConnectorId connectorId,
        Dictionary<string, string> settings,
        CancellationToken ct
    ) {
        if (connectorId.IsUnset)
            throw new ArgumentException("Connector id cannot be empty.", nameof(connectorId));

        var request = new Commands.ReconfigureConnector {
            ConnectorId = connectorId
        };

        foreach (var (key, value) in settings)
            request.Settings[key] = value;

        try {
            await CommandServiceClient
                .ReconfigureAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return Results.Success;
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

    #endregion

    #region ->> Delete <<-

    /// <summary>
    /// Deletes an existing connector.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to delete.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was deleted successfully.</returns>
    public async ValueTask<Success> Delete(
        ConnectorId connectorId,
        CancellationToken ct
    ) {
        if (connectorId.IsUnset)
            throw new ArgumentException("Connector id cannot be empty.", nameof(connectorId));

        var request = new Commands.DeleteConnector {
            ConnectorId = connectorId
        };

        try {
            await CommandServiceClient
                .DeleteAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return Results.Success;
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

    #endregion

    #region ->> Start <<-

    /// <summary>
    /// Starts a connector from its last known position.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to start.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was started successfully.</returns>
    public ValueTask<Success> Start(
        ConnectorId connectorId,
        CancellationToken ct
    ) => StartCore(connectorId, logPosition: null, ct);

    /// <summary>
    /// Starts a connector from a specific log position.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to start.</param>
    /// <param name="logPosition">Log position to start processing from.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was started successfully.</returns>
    public ValueTask<Success> Start(
        ConnectorId connectorId,
        LogPosition logPosition,
        CancellationToken ct
    ) => StartCore(connectorId, logPosition, ct);

    async ValueTask<Success> StartCore(
        ConnectorId connectorId,
        LogPosition? logPosition,
        CancellationToken ct
    ) {
        if (connectorId.IsUnset)
            throw new ArgumentException("Connector id cannot be empty.", nameof(connectorId));

        var request = new Commands.StartConnector {
            ConnectorId = connectorId
        };

        if (logPosition.HasValue) {
            request.StartFrom = new() {
                LogPosition = logPosition.Value
            };
        }

        try {
            await CommandServiceClient
                .StartAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return Results.Success;
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

    #endregion

    #region ->> Reset <<-

    /// <summary>
    /// Resets a connector to the beginning of the stream.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to reset.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was reset successfully.</returns>
    public ValueTask<Success> Reset(
        ConnectorId connectorId,
        CancellationToken ct
    ) => ResetCore(connectorId, logPosition: null, ct);

    /// <summary>
    /// Resets a connector to a specific log position.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to reset.</param>
    /// <param name="logPosition">Log position to reset processing to.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was reset successfully.</returns>
    public ValueTask<Success> Reset(
        ConnectorId connectorId,
        LogPosition logPosition,
        CancellationToken ct
    ) => ResetCore(connectorId, logPosition, ct);

    async ValueTask<Success> ResetCore(
        ConnectorId connectorId,
        LogPosition? logPosition,
        CancellationToken ct
    ) {
        if (connectorId.IsUnset)
            throw new ArgumentException("Connector id cannot be empty.", nameof(connectorId));

        var request = new Commands.ResetConnector {
            ConnectorId = connectorId
        };

        if (logPosition.HasValue) {
            request.StartFrom = new() {
                LogPosition = logPosition.Value
            };
        }

        try {
            await CommandServiceClient
                .ResetAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return Results.Success;
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

    #endregion

    #region ->> Stop <<-

    /// <summary>
    /// Stops a running connector.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to stop.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was stopped successfully.</returns>
    public async ValueTask<Success> Stop(
        ConnectorId connectorId,
        CancellationToken ct
    ) {
        if (connectorId.IsUnset)
            throw new ArgumentException("Connector id cannot be empty.", nameof(connectorId));

        var request = new Commands.StopConnector {
            ConnectorId = connectorId
        };

        try {
            await CommandServiceClient
                .StopAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return Results.Success;
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

    #endregion

    #region ->> Rename <<-

    /// <summary>
    /// Renames an existing connector.
    /// </summary>
    /// <param name="connectorId">Unique identifier of the connector to rename.</param>
    /// <param name="name">New display name for the connector.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the connector was renamed successfully.</returns>
    public async ValueTask<Success> Rename(
        ConnectorId connectorId,
        string name,
        CancellationToken ct
    ) {
        if (connectorId.IsUnset)
            throw new ArgumentException("Connector id cannot be empty.", nameof(connectorId));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var request = new Commands.RenameConnector {
            ConnectorId = connectorId,
            Name        = name
        };

        try {
            await CommandServiceClient
                .RenameAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return Results.Success;
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

    #endregion
}
