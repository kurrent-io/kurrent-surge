using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Models;

namespace Kurrent.Surge.Processors.Checkpointing;

readonly struct DefferedLogValue<T>(T[] items, Func<T, string?> formatter, string separator = ", ") {
    public override string ToString() => string.Join(separator, items.Select(formatter).Where(x => x is { }));
}

static class FormattedLogValueExtensions {
    extension<T>(T[] source) {
        public DefferedLogValue<T> AsLogValue(Func<T, string?> formatter, string separator = ", ") =>
            new(source, formatter, separator);
    }
}

/// <summary>
/// Base class for <see cref="ICheckpointStore"/> implementations. Handles lazy initialization,
/// cancellation, exception translation to <see cref="CheckpointStoreException"/>, and logging.
/// Subclasses implement <see cref="OnInitializeAsync"/>, <see cref="OnLoadLatestAsync"/>,
/// <see cref="OnStoreAsync"/>, <see cref="OnResetAsync"/>.
/// </summary>
[PublicAPI]
public abstract partial class CheckpointStoreBase : ICheckpointStore {
    volatile bool _isReady;

    protected CheckpointStoreBase(
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
            throw new CheckpointStoreException(StoreName, $"Failed to initialize checkpoint store. {ex.Message}", ex);
        }

        _isReady = true;
        
        LogReady(ProcessorId, ReadyTarget);
    }
    
    /// <inheritdoc/>
    public async ValueTask<ProcessorCheckpoint> LoadLatestAsync(string droneId, CancellationToken ct = default) {
        await EnsureReadyAsync(ct).ConfigureAwait(false);

        LogLoading(ProcessorId, droneId);

        try {
            var checkpoint = await OnLoadLatestAsync(ct).ConfigureAwait(false);

            if (checkpoint.Positions.Length == 0) {
                LogNotFound(ProcessorId, droneId);
                return ProcessorCheckpoint.Empty;
            }

            LogLoaded(ProcessorId, droneId, checkpoint.Positions.Length, checkpoint.Positions.AsLogValue(FormatPosition));
            return checkpoint;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            throw new CheckpointStoreException(StoreName, "Failed to load checkpoint.", ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<ProcessorCheckpoint> LoadLatestAsync(
        string droneId,
        Partition[] partitions,
        SubscriptionInitialPosition initialPosition,
        CancellationToken ct = default
    ) {
        var checkpoint = await LoadLatestAsync(droneId, ct).ConfigureAwait(false);

        return checkpoint.ResolveStartPositions(initialPosition, partitions);
    }

    /// <inheritdoc/>
    public async ValueTask StoreAsync(
        string droneId,
        string checkpointId,
        RecordPosition[] positions,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(droneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
        ArgumentOutOfRangeException.ThrowIfZero(positions.Length, nameof(positions));

        await EnsureReadyAsync(ct).ConfigureAwait(false);

        LogStoring(ProcessorId, droneId, positions.AsLogValue(FormatPosition));

        try {
            await OnStoreAsync(droneId, checkpointId, positions, ct).ConfigureAwait(false);
            LogStored(ProcessorId, droneId, positions.Length, positions.AsLogValue(FormatPosition));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            throw new CheckpointStoreException(StoreName, "Failed to store checkpoint.", ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<ProcessorCheckpoint> ResetAsync(
        string droneId,
        string checkpointId,
        LogPosition position,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(droneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);

        await EnsureReadyAsync(ct).ConfigureAwait(false);

        var previous = await LoadLatestAsync(droneId, ct).ConfigureAwait(false);

        LogResetting(ProcessorId, droneId, position);

        try {
            await OnResetAsync(droneId, checkpointId, position, ct).ConfigureAwait(false);
            LogReset(ProcessorId, droneId, previous.Positions.AsLogValue(FormatPosition));
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            throw new CheckpointStoreException(StoreName, "Failed to reset checkpoint.", ex);
        }

        return previous;
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
        RecordPosition[] positions,
        CancellationToken ct
    );

    /// <summary>
    /// Resets to a single <paramref name="position"/> — the whole group's barrier. Implementations
    /// materialise it across their partitions (fan on load for an event-sourced store, on write for a
    /// table store). <see cref="LogPosition.Unset"/> is a full wipe.
    /// </summary>
    protected abstract ValueTask OnResetAsync(
        string droneId,
        string checkpointId,
        LogPosition position,
        CancellationToken ct
    );
    
    #region ->> Logging <<-
    
    static string? FormatPosition(RecordPosition p) =>
        p.LogPosition > LogPosition.Earliest ? $"[{p.Partition}] {p}" : null;
    
    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}] Ready on {Target}")]
    partial void LogReady(string processorId, string target);

    [LoggerMessage(Level = LogLevel.Trace, Message = "[{DroneId}] Loading checkpoint...")]
    partial void LogLoading(string processorId, string droneId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DroneId}] Loaded {PartitionCount} partitions :: {Positions}")]
    partial void LogLoaded(string processorId, string droneId, int partitionCount, DefferedLogValue<RecordPosition> positions);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DroneId}] No checkpoint found")]
    partial void LogNotFound(string processorId, string droneId);

    [LoggerMessage(Level = LogLevel.Trace, Message = "[{DroneId}] Storing checkpoint :: {Positions}")]
    partial void LogStoring(string processorId, string droneId, DefferedLogValue<RecordPosition> positions);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DroneId}] Stored {PartitionCount} partitions: {Positions}")]
    partial void LogStored(string processorId, string droneId, int partitionCount, DefferedLogValue<RecordPosition> positions);

    [LoggerMessage(Level = LogLevel.Trace, Message = "[{DroneId}] Resetting to :: {Position}")]
    partial void LogResetting(string processorId, string droneId, LogPosition position);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{DroneId}] Reset from :: {PreviousPositions}")]
    partial void LogReset(string processorId, string droneId, DefferedLogValue<RecordPosition> previousPositions);

    #endregion
}

/// <summary>
/// Thrown by a <see cref="CheckpointStoreBase"/> implementation when an underlying operation
/// (initialize / load / store / reset) fails. The originating store is identified in the
/// message prefix; the original exception is attached as <see cref="Exception.InnerException"/>.
/// </summary>
[PublicAPI]
public sealed class CheckpointStoreException(string storeName, string message, Exception? innerException = null) 
    : KurrentException(ErrorCode.FromType<CheckpointStoreException>(), $"[{storeName}] {message}", innerException);

