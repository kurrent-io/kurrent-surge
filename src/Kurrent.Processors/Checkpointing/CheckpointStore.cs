using System.Collections.Immutable;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Client.Processors.Checkpointing;

/// <summary>
/// Checkpoint store bound to a specific processor, using document-sourcing pattern.
/// </summary>
/// <remarks>
/// <para>
/// Uses a single checkpoint stream per processor where all workers merge their
/// partition checkpoints. Handles concurrent updates with optimistic concurrency
/// and automatic retry.
/// </para>
/// <para>
/// Stream naming: <c>$processors/{processorId}/checkpoints</c>
/// </para>
/// </remarks>
[PublicAPI]
public sealed partial class CheckpointStore : ICheckpointStore {
    readonly string        _processorId;
    readonly StreamsClient _client;
    readonly ILogger       _logger;
    readonly int           _maxCount;
    readonly StreamName    _stream;
    readonly TimeProvider  _time;

    volatile bool _isReady;

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckpointStore"/> class.
    /// </summary>
    /// <param name="processorId">The processor group identifier this store is bound to.</param>
    /// <param name="maxCount">Maximum number of checkpoint records to retain in the stream.</param>
    /// <param name="client">The streams client for KurrentDB operations.</param>
    /// <param name="time">Optional time provider for timestamps.</param>
    /// <param name="logger">Optional logger instance.</param>
    public CheckpointStore(
        string processorId,
        int maxCount,
        StreamsClient client,
        TimeProvider? time = null,
        ILogger<CheckpointStore>? logger = null
    ) {
        _processorId = processorId;
        _maxCount    = maxCount;
        _client      = client;
        _time        = time ?? TimeProvider.System;
        _logger      = logger ?? NullLogger<CheckpointStore>.Instance;
        _stream      = $"$processors/{processorId}/checkpoints";
    }

    /// <summary>
    /// Gets the processor ID this store is bound to.
    /// </summary>
    public string ProcessorId => _processorId;

    /// <summary>
    /// Loads the checkpoint for this processor.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The checkpoint containing all partition checkpoints, barrier ID, and resolution logic.
    /// Returns <see cref="ProcessorCheckpoint.Empty"/> if no checkpoint exists.
    /// </returns>
    public async ValueTask<ProcessorCheckpoint> LoadLatestAsync(CancellationToken ct = default) {
        await EnsureReadyAsync(ct).ConfigureAwait(false);

        try {
            var record = await _client
                .ReadLastStreamRecord(_stream, skipDecoding: false, ct)
                .UnwrapAsync()
                .ConfigureAwait(false);

            if (record.Value is not ProcessorCheckpointState state || state.Partitions.IsEmpty) {
                LogNoCheckpointFound(_processorId);
                return ProcessorCheckpoint.Empty;
            }

            var partitions = state.Partitions
                .ToImmutableDictionary(
                    kvp => Partition.From(kvp.Key),
                    kvp => new PartitionCheckpoint(
                        RecordPosition.FromLogPosition(new LogPosition(kvp.Value.Position)),
                        kvp.Value.WorkerId));

            LogLoaded(_processorId, partitions.Count, state.BarrierId);

            return new ProcessorCheckpoint {
                Partitions = partitions,
                BarrierId  = new BarrierId(state.BarrierId),
                Timestamp  = state.Timestamp
            };
        }
        catch (StreamNotFoundException) {
            LogNoCheckpointFound(_processorId);
            return ProcessorCheckpoint.Empty;
        }
    }

    /// <summary>
    /// Stores checkpoint positions for the specified partitions, merging with existing positions.
    /// </summary>
    /// <param name="workerId">The worker storing the checkpoint (for attribution).</param>
    /// <param name="barrierId">The barrier that triggered this checkpoint.</param>
    /// <param name="positions">Partition positions to checkpoint.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when positions dictionary is empty.</exception>
    /// <remarks>
    /// <para>
    /// Internally loads the current checkpoint state and merges the provided positions
    /// with existing ones. Uses optimistic concurrency with automatic retry on conflicts.
    /// </para>
    /// <para>
    /// Only the specified partitions are updated; other partitions retain their positions.
    /// </para>
    /// </remarks>
    public async ValueTask StoreAsync(
        string workerId,
        BarrierId barrierId,
        ImmutableDictionary<Partition, RecordPosition> positions,
        CancellationToken ct = default
    ) {
        if (positions.IsEmpty)
            throw new InvalidOperationException("Cannot store empty positions dictionary.");

        await EnsureReadyAsync(ct).ConfigureAwait(false);

        // Retry loop for optimistic concurrency conflicts
        while (true) {
            ct.ThrowIfCancellationRequested();

            // 1. Load current checkpoint state
            var (currentState, expectedRevision) = await LoadCurrentStateAsync(ct).ConfigureAwait(false);

            // 2. Merge new positions with existing
            var merged = MergePositions(currentState, workerId, barrierId, positions);

            // 3. Attempt to store with expected revision
            var request = AppendStreamRequest.New()
                .ForStream(_stream)
                .ExpectingRevision(expectedRevision)
                .WithRecord(rec => rec
                    .WithValue(merged)
                    .WithMetadata("processor_id", _processorId)
                    .WithMetadata("worker_id", workerId))
                .Build();

            var result = await _client.Append(request, ct).ConfigureAwait(false);

            if (result.IsSuccess) {
                LogStored(_processorId, workerId, positions.Count, result.Value.StreamRevision);

                // Configure retention after first successful append
                await ConfigureRetentionIfNeededAsync(ct).ConfigureAwait(false);

                return;
            }

            // Check if it's a concurrency conflict
            if (result.Error.Value is StreamRevisionConflictException) {
                LogConcurrencyConflict(_processorId, workerId);
                continue; // Retry
            }

            // Other errors - propagate
            result.Unwrap();
        }
    }

    async ValueTask<(ProcessorCheckpointState State, ExpectedStreamState Revision)> LoadCurrentStateAsync(
        CancellationToken ct
    ) {
        try {
            var record = await _client
                .ReadLastStreamRecord(_stream, skipDecoding: false, ct)
                .UnwrapAsync()
                .ConfigureAwait(false);

            if (record.Value is ProcessorCheckpointState state)
                return (state, record.Position.StreamRevision);

            // Stream exists but has no valid checkpoint - stream is empty, expect it to exist
            return (new ProcessorCheckpointState(), ExpectedStreamState.StreamExists);
        }
        catch (StreamNotFoundException) {
            // Stream doesn't exist yet - use NoStream to create it
            return (new ProcessorCheckpointState(), ExpectedStreamState.NoStream);
        }
    }

    ProcessorCheckpointState MergePositions(
        ProcessorCheckpointState current,
        string workerId,
        BarrierId barrierId,
        ImmutableDictionary<Partition, RecordPosition> newPositions
    ) {
        var partitionsBuilder = current.Partitions.ToBuilder();

        foreach (var (partition, position) in newPositions)
            partitionsBuilder[partition.Value] = new StoredPartitionCheckpoint(position.LogPosition.Value, workerId);

        return new ProcessorCheckpointState {
            Partitions   = partitionsBuilder.ToImmutable(),
            BarrierId    = barrierId.Value,
            ProcessorId  = _processorId,
            LastWorkerId = workerId,
            Timestamp    = _time.GetUtcNow()
        };
    }

    async ValueTask EnsureReadyAsync(CancellationToken ct) {
        if (_isReady)
            return;

        // Don't configure retention here - it will be done after first successful append
        // This avoids issues with stream state when appending to non-existent streams

        if (_maxCount <= 0)
            LogRetentionNotConfigured(_processorId);

        _isReady = true;
        LogReady(_processorId, _stream);
    }

    async ValueTask ConfigureRetentionIfNeededAsync(CancellationToken ct) {
        if (_maxCount <= 0)
            return;

        var info = await _client.GetStreamInfo(_stream, ct).UnwrapAsync().ConfigureAwait(false);

        if (info != StreamInfo.None && !info.Metadata.HasMaxCount) {
            await _client
                .ConfigureStreamDataRetention(_stream, new() { MaxCount = _maxCount }, ct)
                .UnwrapAsync()
                .ConfigureAwait(false);

            LogRetentionConfigured(_processorId, _maxCount);
        }
    }

    #region Logging

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "[{ProcessorId}] Retention not configured, checkpoint stream will grow unbounded"
    )]
    partial void LogRetentionNotConfigured(string processorId);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[{ProcessorId}] Retention configured with MaxCount={MaxCount}"
    )]
    partial void LogRetentionConfigured(string processorId, int maxCount);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[{ProcessorId}] Ready on {Stream}"
    )]
    partial void LogReady(string processorId, string stream);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[{ProcessorId}] Loaded {PartitionCount} partitions @ barrier {BarrierId}"
    )]
    partial void LogLoaded(string processorId, int partitionCount, string barrierId);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[{ProcessorId}/{WorkerId}] Stored {PartitionCount} partitions @ revision {Revision}"
    )]
    partial void LogStored(string processorId, string workerId, int partitionCount, long revision);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[{ProcessorId}] No checkpoint found"
    )]
    partial void LogNoCheckpointFound(string processorId);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[{ProcessorId}/{WorkerId}] Concurrency conflict, retrying..."
    )]
    partial void LogConcurrencyConflict(string processorId, string workerId);

    #endregion

    /// <summary>
    /// Internal checkpoint data for a single partition (stored in KurrentDB).
    /// </summary>
    sealed record StoredPartitionCheckpoint(long Position, string WorkerId);

    /// <summary>
    /// The checkpoint document stored in the processor's checkpoint stream.
    /// </summary>
    sealed record ProcessorCheckpointState {
        /// <summary>
        /// Per-partition checkpoint data.
        /// Key: Partition value (int)
        /// Value: Checkpoint containing position and worker attribution
        /// </summary>
        public ImmutableDictionary<int, StoredPartitionCheckpoint> Partitions { get; init; } =
            ImmutableDictionary<int, StoredPartitionCheckpoint>.Empty;

        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.MinValue;

        public string BarrierId { get; init; } = "";

        public string ProcessorId { get; init; } = "";

        public string LastWorkerId { get; init; } = "";
    }
}
