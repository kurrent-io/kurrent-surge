// ORIGINAL SURGE IMPLEMENTATION — preserved for reference during refactor to IRecordProcessor.
// Disabled from compilation; re-enable by removing the #if false / #endif wrapping below.
#if false

using Kurrent.Client;
using Kurrent.Surge.Consumers.Checkpoints;
using Kurrent.Surge.Consumers.Configuration;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Surge.Projectors;

public interface IProjector {
    /// <summary>
    /// Executes the projector's main logic until a stop signal is received.
    /// This method runs the <see cref="Projector{TClient}.ExecuteAsync"/> method to perform
    /// the core functionality of the projector and waits for the <see cref="Projector{TClient}.Stopped"/> task
    /// to ensure the proper halting of the background service.
    /// </summary>
    /// <param name="stoppingToken">
    /// A <see cref="CancellationToken"/> that is triggered to signal the stopping of the service.
    /// </param>
    Task RunUntilStopped(CancellationToken stoppingToken);
}

[PublicAPI]
public abstract partial class Projector<TClient> : BackgroundService, IProjector {
    protected Projector(ProjectorOptions options, Projection<TClient> projection, KurrentClientOptions clientOptions) {
        Options       = options;
        Projection    = projection;
        KurrentDB     = KurrentClient.Create(clientOptions);
        LoggerFactory = clientOptions.LoggerFactory;
        Logger        = LoggerFactory.CreateLogger(GetType().Name);

        Finisher = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Stopped  = Finisher.Task;
    }

    ProjectorOptions     Options       { get; }
    KurrentClient        KurrentDB     { get; }
    ILoggerFactory       LoggerFactory { get; }
    TaskCompletionSource Finisher      { get; }
    Task                 Stopped       { get; }

    protected Projection<TClient> Projection { get; }
    protected ILogger             Logger     { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            await Execute(stoppingToken).ConfigureAwait(false);
            Finisher.SetResult();
        }
        catch (OperationCanceledException) {
            Finisher.SetCanceled(stoppingToken);
        }
        catch (Exception ex) {
            Finisher.SetException(ex);
        }
    }

    async Task Execute(CancellationToken stoppingToken) {
        var checkpointStore = await GetCheckpointStore(stoppingToken).ConfigureAwait(false);

        await checkpointStore
            .EnsureStoreExists(stoppingToken)
            .ConfigureAwait(false);

        var client = await GetClient(stoppingToken).ConfigureAwait(false);

        await Projection
            .Setup(client, stoppingToken)
            .ConfigureAwait(false);

        await using var checkpointController = new AsyncCheckpointController(
            checkpointStore.CommitPositions,
            new() {
                Enabled          = Options.AutoCommit.Enabled,
                Interval         = Options.AutoCommit.Interval,
                RecordsThreshold = Options.AutoCommit.RecordsThreshold
            },
            Projection.ProjectionName
        );

        // Note: AsyncCheckpointController starts automatically in constructor, no Activate() needed

        var checkpointPosition = await checkpointStore
            .LoadCheckpoint(stoppingToken)
            .ConfigureAwait(false);

        var consumer = ClientOptions
            .ConsumerId(Projection.ProjectionName)
            .Filter(Options.Filter)
            .InitialPosition(Options.InitialPosition)
            .StartPosition(checkpointPosition)
            .DisableAutoCommit()
            .DisableResiliencePipeline()
            .LoggerFactory(LoggerFactory)
            .Create();

        try {
            await foreach (var record in consumer.Records(stoppingToken).ConfigureAwait(false)) {
                if (Projection.CanProjectRecord(record)) {
                    await Projection
                        .ProjectRecord(new ProjectionContext(GetClient, record, stoppingToken))
                        .ConfigureAwait(false);
                }

                // if batching things complicate
                await checkpointController
                    .Track(record)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) {
            // if the operation was cancelled, we need to commit all tracked records
            // disposing of the checkpoint controller will do this for us
        }
        catch (Exception ex) {
            Logger.LogError(ex, "An error occurred while processing records");
        }

        Logger.LogInformation("Stopping consumer: {Position}" , checkpointController.NextReadyPositions.Select(x => x.LogPosition.CommitPosition!.Value).LastOrDefault());

        var commited = await checkpointController
            .CommitAll()
            .ConfigureAwait(false);

        Logger.LogInformation("Last Committed Position: {Position}", commited.Select(x => x.LogPosition.CommitPosition!.Value).LastOrDefault());
    }

    /// <summary>
    /// Executes the projector's main logic until a stop signal is received.
    /// This method runs the <see cref="ExecuteAsync"/> method to perform
    /// the core functionality of the projector and waits for the <see cref="Stopped"/> task
    /// to ensure the proper halting of the background service.
    /// </summary>
    /// <param name="stoppingToken">
    /// A <see cref="CancellationToken"/> that is triggered to signal the stopping of the service.
    /// </param>
    public async Task RunUntilStopped(CancellationToken stoppingToken) {
        await ExecuteAsync(stoppingToken).ConfigureAwait(false);
        await Stopped.ConfigureAwait(false);
    }

    protected abstract ValueTask<TClient> GetClient(CancellationToken ct);

    protected abstract ValueTask<IProjectionCheckpointStore> GetCheckpointStore(CancellationToken ct);

    #region Logging

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProjectionName}] Initializing...")]
    static partial void LogInitializing(ILogger logger, string projectionName);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{ProjectionName}] Initialized")]
    static partial void LogInitialized(ILogger logger, string projectionName);

    [LoggerMessage(Level = LogLevel.Error, Message = "[{ProjectionName}] Failed to initialize")]
    static partial void LogInitializationFailed(ILogger logger, string projectionName, Exception ex);

    #endregion
}

#endif
