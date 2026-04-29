using Kurrent.Client.Streams;
using Kurrent.Processors.Models;

namespace Kurrent.Processors.Checkpointing;

/// <summary>
/// Base class for <see cref="ICheckpointStore"/> implementations. Owns the lifecycle
/// scaffolding (<see cref="Initialize"/> / <see cref="EnsureReadyAsync"/>), cancellation
/// checks, exception translation to <see cref="CheckpointStoreException"/>, shared log
/// vocabulary, and the <see cref="ResolvePartitions"/> / format helpers.
/// </summary>
/// <remarks>
/// Subclasses implement the IO hooks — <see cref="OnInitializeAsync"/>,
/// <see cref="OnLoadLatestAsync"/>, <see cref="OnStoreAsync"/>, <see cref="OnResetAsync"/> —
/// plus <see cref="StoreName"/> (used in exception messages) and <see cref="ReadyTarget"/>
/// (used in the "Ready on {Target}" log line).
/// </remarks>
[PublicAPI]
public abstract partial class CheckpointStore : ICheckpointStore {
    volatile bool _isReady;

    public CheckpointStore(
        string processorId,
        TimeProvider? time = null,
        ILogger? logger = null
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(processorId);

        ProcessorId = processorId;
        Time        = time ?? TimeProvider.System;
        Logger      = logger ?? NullLogger.Instance;
        StoreName   = GetType().Name.Replace("CheckpointStore", "");
    }

    /// <summary>The processor group identifier this store is bound to.</summary>
    protected string ProcessorId { get; }

    protected ILogger Logger { get; }
    
    /// <summary>Time provider used for timestamps.</summary>
    protected TimeProvider Time { get; }

    /// <summary>Short name of the concrete store (e.g. "KurrentDB", "DuckDB"). Used in exception messages.</summary>
    protected string StoreName { get; }

    /// <summary>Target description included in the <c>Ready on {Target}</c> log line (stream name, table name, etc.).</summary>
    protected abstract string ReadyTarget { get; }
    
    /// <summary>
    /// Warms the store: runs <see cref="OnInitializeAsync"/> once, latches readiness, logs
    /// the <c>Ready on {Target}</c> line. Call once upfront if you want eager initialization;
    /// every public op also calls this internally, so the per-op path short-circuits after
    /// the first successful run.
    /// </summary>
    public async ValueTask EnsureReadyAsync(CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (_isReady) return;

        try {
            await OnInitializeAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            throw new CheckpointStoreException(StoreName, "Failed to initialize checkpoint store.", ex);
        }

        _isReady = true;
        LogReady(ProcessorId, ReadyTarget);
    }
    
    public async ValueTask<ProcessorCheckpoint> LoadLatestAsync(CancellationToken ct = default) {
        await EnsureReadyAsync(ct).ConfigureAwait(false);

        LogLoading(ProcessorId);

        try {
            var checkpoint = await OnLoadLatestAsync(ct).ConfigureAwait(false);

            if (checkpoint.Positions.Length == 0) {
                LogNotFound(ProcessorId);
                return ProcessorCheckpoint.Empty;
            }

            LogLoaded(ProcessorId, checkpoint.Positions.Length, FormatPositions(checkpoint));
            return checkpoint;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            throw new CheckpointStoreException(StoreName, "Failed to load checkpoint.", ex);
        }
    }

    public async ValueTask<ProcessorCheckpoint> LoadLatestAsync(
        Partition[] partitions,
        SubscriptionInitialPosition initialPosition,
        CancellationToken ct = default
    ) {
        // if (partitions.Length == 0)
        //     return ProcessorCheckpoint.Empty;

        var checkpoint = await LoadLatestAsync(ct).ConfigureAwait(false);
        return ResolvePartitions(checkpoint, partitions, initialPosition);
    }

    public async ValueTask StoreAsync(
        string droneId,
        string checkpointId,
        PartitionPosition[] positions,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(droneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
        ArgumentOutOfRangeException.ThrowIfZero(positions.Length, nameof(positions));

        await EnsureReadyAsync(ct).ConfigureAwait(false);

        LogStoring(ProcessorId, droneId, FormatInputPositions(positions));

        try {
            await OnStoreAsync(droneId, checkpointId, positions, ct).ConfigureAwait(false);
            LogStored(ProcessorId, droneId, positions.Length, FormatInputPositions(positions));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            throw new CheckpointStoreException(StoreName, "Failed to store checkpoint.", ex);
        }
    }

    public async ValueTask ResetAsync(
        string droneId,
        string checkpointId,
        PartitionPosition[] positions,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(droneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);

        await EnsureReadyAsync(ct).ConfigureAwait(false);

        LogResetting(ProcessorId, droneId, positions.Length);

        try {
            await OnResetAsync(droneId, checkpointId, positions, ct).ConfigureAwait(false);
            LogReset(ProcessorId, droneId, positions.Length);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            throw new CheckpointStoreException(StoreName, "Failed to reset checkpoint.", ex);
        }
    }

    public async ValueTask ResetAsync(
        string droneId,
        string checkpointId,
        RecordPosition position,
        CancellationToken ct = default
    ) {
        if (position.IsUnset) {
            await ResetAsync(droneId, checkpointId, [], ct).ConfigureAwait(false);
            return;
        }

        var current = await LoadLatestAsync(ct).ConfigureAwait(false);

        var positions = current.Positions
            .Select(p => p with { Position = position })
            .ToArray();

        await ResetAsync(droneId, checkpointId, positions, ct).ConfigureAwait(false);
    }
    
    /// <summary>
    /// Performs any one-time initialization (table creation, retention configuration, etc.).
    /// Called at most once per store instance; the result is cached via <c>_isReady</c>.
    /// </summary>
    protected abstract ValueTask OnInitializeAsync(CancellationToken ct);

    /// <summary>
    /// Returns the current checkpoint state. Return <see cref="ProcessorCheckpoint.Empty"/>
    /// (or a checkpoint with zero positions) when no checkpoint has been persisted yet —
    /// the base will log <c>No checkpoint found</c> and surface the same.
    /// </summary>
    protected abstract ValueTask<ProcessorCheckpoint> OnLoadLatestAsync(CancellationToken ct);

    /// <summary>Persists <paramref name="positions"/> for the given drone and checkpoint id.</summary>
    protected abstract ValueTask OnStoreAsync(
        string droneId,
        string checkpointId,
        PartitionPosition[] positions,
        CancellationToken ct
    );

    /// <summary>
    /// Resets partition positions. Empty <paramref name="positions"/> = full wipe;
    /// non-empty = selective reset (those partitions repositioned).
    /// </summary>
    protected abstract ValueTask OnResetAsync(
        string droneId,
        string checkpointId,
        PartitionPosition[] positions,
        CancellationToken ct
    );
    
    #region ->> Helpers <<-

    protected static ProcessorCheckpoint ResolvePartitions(
        ProcessorCheckpoint checkpoint,
        Partition[] partitions,
        SubscriptionInitialPosition initialPosition
    ) {
        var defaultPos = initialPosition == SubscriptionInitialPosition.Earliest
            ? RecordPosition.Earliest
            : RecordPosition.Latest;

        var existing = new Dictionary<Partition, PartitionPosition>(checkpoint.Positions.Length);
        foreach (var p in checkpoint.Positions)
            existing[p.Partition] = p;

        var resolved = new PartitionPosition[partitions.Length];

        for (var i = 0; i < partitions.Length; i++) {
            var partition = partitions[i];

            resolved[i] = existing.TryGetValue(partition, out var found)
                ? found
                : new PartitionPosition(partition, defaultPos with { Partition = partition });
        }

        return new ProcessorCheckpoint {
            Positions = resolved,
            Timestamp = checkpoint.Timestamp
        };
    }

    protected static string FormatPositions(ProcessorCheckpoint checkpoint) =>
        FormatInputPositions(checkpoint.Positions);

    protected static string FormatInputPositions(PartitionPosition[] positions) {
        var formatted = positions
            .Where(x => x.Position.LogPosition > LogPosition.Earliest)
            .Select(x => $"[{x.Partition}] {x.Position}")
            .ToArray();

        return string.Join(", ", formatted);
    }

    #endregion

    #region ->> Logging <<-

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}] Ready on {Target}")]
    partial void LogReady(string processorId, string target);

    [LoggerMessage(Level = LogLevel.Trace, Message = "[{ProcessorId}] Loading checkpoint...")]
    partial void LogLoading(string processorId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}] Loaded {PartitionCount} partitions :: {Positions}")]
    partial void LogLoaded(string processorId, int partitionCount, string positions);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}] No checkpoint found")]
    partial void LogNotFound(string processorId);

    [LoggerMessage(Level = LogLevel.Trace, Message = "[{ProcessorId}/{DroneId}] Storing checkpoint :: {Positions}")]
    partial void LogStoring(string processorId, string droneId, string positions);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{DroneId}] Stored {PartitionCount} partitions: {Positions}")]
    partial void LogStored(string processorId, string droneId, int partitionCount, string positions);

    [LoggerMessage(Level = LogLevel.Trace, Message = "[{ProcessorId}/{DroneId}] Resetting {PartitionCount} partitions...")]
    partial void LogResetting(string processorId, string droneId, int partitionCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{ProcessorId}/{DroneId}] Reset {PartitionCount} partitions")]
    partial void LogReset(string processorId, string droneId, int partitionCount);

    #endregion
}
