using Kurrent.Surge;
using Microsoft.Extensions.Logging;

namespace Kurrent.Client.Streams.Checkpoints;

public delegate Task<IReadOnlyList<RecordPosition>> CommitRecordPositions(IReadOnlyList<RecordPosition> positions, CancellationToken cancellationToken);

/// <summary>
/// Keeps track of the latest processed record positions in-memory and commits them to the server at a given interval or by request.
/// Checkpoints store how far along a consumer is in the streams it is processing from.
/// </summary>
public class CheckpointController : IAsyncDisposable {
    public CheckpointController(CommitRecordPositions commitRecordPositions, AutoCommitOptions options, ILogger logger, string consumerId) {
        CommitRecordPositions = commitRecordPositions;
        AutoCommit            = options;
        Logger                = logger;
        ConsumerId            = consumerId;

        Cancellator     = new();
        Gatekeeper      = new(true);
        PositionTracker = new();

        Timer = new(
            options.Interval,
            async (isLastTick, _) => {
                if (isLastTick)
                    Logger.LogTrace("{ConsumerId} Auto commit timer last tick.", consumerId);

                await CommitReadyPositions(CancellationToken.None);

                if (isLastTick) {
                    var gapsCount = PositionTracker.Gaps.Count;
                    if (gapsCount > 0)
                        Logger.LogWarning("{ConsumerId} Position gaps discarded: {Gaps}", consumerId, gapsCount);
                }
            }
        );
    }

    CommitRecordPositions   CommitRecordPositions { get; }
    AutoCommitOptions       AutoCommit            { get; }
    ILogger                 Logger                { get; }
    string                  ConsumerId            { get; }
    AsyncPeriodicTimer      Timer                 { get; }
    PositionTracker         PositionTracker       { get; }
    CancellationTokenSource Cancellator           { get; }
    ManualResetEventSlim    Gatekeeper            { get; }

    bool Active { get; set; }

    /// <summary>
    /// Gets the currently tracked record positions.
    /// </summary>
    public IReadOnlyList<RecordPosition> TrackedPositions =>
        PositionTracker.TrackedPositions.Select(x => x.Position).ToList();

    /// <summary>
    /// Gets the currently tracked record positions ready to be committed.
    /// </summary>
    public IReadOnlyList<RecordPosition> NextReadyPositions =>
        PositionTracker.NextReadyPositions.Select(x => x.Position).ToList();

    /// <summary>
    /// Gets the last committed record position.
    /// </summary>
    public RecordPosition LastCommittedPosition =>
        PositionTracker.LastFlushedPosition;

    /// <summary>
    /// Activates the checkpoint controller, starting the auto-commit timer if enabled.
    /// </summary>
    /// <returns>A task that represents the asynchronous activation operation.</returns>
    public async ValueTask Activate() {
        if (Active)
            return;

        if (AutoCommit.Enabled) {
            await Timer.Start(Cancellator.Token);
            Logger.LogTrace("[{ConsumerId}] Auto commit timer activated", ConsumerId);
        }

        Active = true;

        Logger.LogTrace("[{ConsumerId}] Activated", ConsumerId);
    }

    /// <summary>
    /// Tracks the specified event store record.
    /// If the auto commit is enabled and the threshold is reached, it will trigger the commit of the next ready positions.
    /// </summary>
    /// <param name="record"> The event store record to track.</param>
    /// <param name="callback">The callback to execute after the track operation is completed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    void Track(Record record, Func<TrackResult, Task> callback, CancellationToken cancellationToken = default) {
        if (record == Record.None || record.Position == RecordPosition.Unset)
            return;

        PositionTracker.Track(record);

        _ = Task.Run(async () => {
            IReadOnlyList<RecordPosition> commitedPositions = [];
            Exception?                    error             = null;

            if (AutoCommit.Enabled && PositionTracker.Count >= AutoCommit.RecordsThreshold) {
                try {
                    commitedPositions = await CommitReadyPositions(cancellationToken);
                }
                catch (Exception ex) {
                    error = ex;
                }
            }

            try {
                await callback(new TrackResult(commitedPositions, NextReadyPositions, error));
            }
            catch (Exception cex) {
                Logger.LogError(cex, "[{ConsumerId}] Error detected while handling track callback", ConsumerId);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Tracks the specified event store record.
    /// </summary>
    /// <param name="record">The event store record to track.</param>
    /// <param name="forceCommit">Whether to force the commit of the next ready positions (complete sequences) immediately.</param>
    public async Task<IReadOnlyList<RecordPosition>> Track(Record record, bool forceCommit = false) {
        if (record == Record.None || record.Position == RecordPosition.Unset)
            return [];

        PositionTracker.Track(record);

        if (forceCommit) {
            Logger.LogTrace("[{ConsumerId}] {RecordPosition} record tracked, triggering commit (forced)", ConsumerId, record.Position);
            return await CommitReadyPositions(Cancellator.Token);
        }

        if (AutoCommit.Enabled && PositionTracker.Count >= AutoCommit.RecordsThreshold) {
            Logger.LogTrace("[{ConsumerId}] {RecordPosition} record tracked, triggering commit (threshold reached)", ConsumerId, record.Position);
            return await CommitReadyPositions(Cancellator.Token);
        }

        Logger.LogTrace("[{ConsumerId}] {RecordPosition} record tracked", ConsumerId, record.Position);
        return [];
    }

    /// <summary>
    /// Tracks the specified event store record.
    /// </summary>
    /// <param name="record">The event store record to commit.</param>
    public Task<IReadOnlyList<RecordPosition>> Commit(Record record) => Track(record, true);

    /// <summary>
    /// Commits all tracked positions that are ready to be committed (complete sequences).
    /// </summary>
    public Task<IReadOnlyList<RecordPosition>> CommitAll() => CommitReadyPositions(Cancellator.Token);

    public async ValueTask DisposeAsync() {
        if (!Active)
            return;

        Active = false;

        if (!Cancellator.IsCancellationRequested) {
            Logger.LogTrace("{ConsumerId} Canceling auto commit timer...", ConsumerId);
            await Cancellator.CancelAsync().ConfigureAwait(false);
            Cancellator.Dispose();
        }

        await Timer.DisposeAsync().ConfigureAwait(false);
        Logger.LogTrace("{ConsumerId} Auto commit timer cancelled", ConsumerId);

        Logger.LogTrace("{ConsumerId} Disposing of position tracker...", ConsumerId);
        PositionTracker.Dispose();

        Logger.LogTrace("{ConsumerId} Disposing of commit gatekeeper...", ConsumerId);
        Gatekeeper.Dispose();
    }

    Task<IReadOnlyList<RecordPosition>> CommitReadyPositions(CancellationToken token) => Gatekeeper.Execute(
        (state, innerToken) => state.PositionTracker.ProcessReadyPositions(pos => state.CommitRecordPositions(pos, innerToken)),
        (PositionTracker, CommitRecordPositions), token
    );
}

public record TrackResult(IReadOnlyList<RecordPosition> CommitedPositions, IReadOnlyList<RecordPosition> NextReadyPositions, Exception? Error) {
    public bool Success => Error is null;
}
