using System.Collections.Immutable;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Models;

namespace Kurrent.Processors.Checkpointing;

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
        ProcessorId = processorId;
        _maxCount    = maxCount;
        _client      = client;
        _time        = time ?? TimeProvider.System;
        _logger      = logger ?? NullLogger<CheckpointStore>.Instance;
        _stream      = $"$processors/{processorId}/checkpoints";
    }

    /// <summary>
    /// Gets the processor ID this store is bound to.
    /// </summary>
    public string ProcessorId { get; }

    /// <summary>
    /// Loads the checkpoint for this processor.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The checkpoint containing all partition checkpoints, checkpoint ID, and resolution logic.
    /// Returns <see cref="ProcessorCheckpoint.Empty"/> if no checkpoint exists.
    /// </returns>
    public async ValueTask<ProcessorCheckpoint> LoadLatestAsync(CancellationToken ct = default) {
        await EnsureReadyAsync(ct).ConfigureAwait(false);

        try {
            var record = await _client
                .ReadLastStreamRecord(_stream, skipDecoding: false, ct)
                .ConfigureAwait(false);

            if (record.Value is not ProcessorCheckpointState state || state.Partitions.IsEmpty) {
                LogNoCheckpointFound(ProcessorId);
                return ProcessorCheckpoint.Empty;
            }

            var checkpoint = MapToCheckpoint(state);
            LogLoaded(ProcessorId, checkpoint.Partitions.Count, state.CheckpointId);
            return checkpoint;
        }
        catch (StreamNotFoundException) {
            LogNoCheckpointFound(ProcessorId);
            return ProcessorCheckpoint.Empty;
        }
    }

    ProcessorCheckpoint MapToCheckpoint(ProcessorCheckpointState state) {
        if (state.Partitions.IsEmpty)
            return ProcessorCheckpoint.Empty;

        RecordPosition? low = null;
        RecordPosition? high = null;

        var partitions = state.Partitions
            .ToImmutableDictionary(
                kvp => Partition.From(kvp.Key),
                kvp => {
                    var position = RecordPosition.FromLogPosition(new LogPosition(kvp.Value.Position));

                    if (low is null || position < low)
                        low = position;

                    if (high is null || position > high)
                        high = position;

                    return new PartitionCheckpoint(position, kvp.Value.WorkerId);
                });

        return new ProcessorCheckpoint {
            Partitions    = partitions,
            CheckpointId     = new CheckpointId(state.CheckpointId),
            Timestamp     = state.Timestamp,
            LowWatermark  = low ?? RecordPosition.Earliest,
            HighWatermark = high ?? RecordPosition.Earliest
        };
    }

    /// <inheritdoc />
    public async ValueTask<ProcessorCheckpoint> LoadLatestAsync(
        Partition[] partitions,
        SubscriptionInitialPosition initialPosition,
        CancellationToken ct = default
    ) {
        if (partitions.Length == 0)
            return ProcessorCheckpoint.Empty;

        var checkpoint = await LoadLatestAsync(ct).ConfigureAwait(false);

        var defaultPos = initialPosition == SubscriptionInitialPosition.Earliest
            ? RecordPosition.Earliest
            : RecordPosition.Latest;

        // Build resolved positions for all requested partitions
        var builder = ImmutableDictionary.CreateBuilder<Partition, PartitionCheckpoint>();
        RecordPosition? low = null;
        RecordPosition? high = null;

        foreach (var partition in partitions) {
            RecordPosition position;

            if (checkpoint.Partitions.TryGetValue(partition, out var existing)) {
                position = existing.Position;
                builder.Add(partition, existing);
            }
            else {
                position = defaultPos with { Partition = partition };
                builder.Add(partition, new PartitionCheckpoint(position, string.Empty));
            }

            // Track watermarks
            if (low is null || position < low)
                low = position;

            if (high is null || position > high)
                high = position;
        }

        return new ProcessorCheckpoint {
            Partitions    = builder.ToImmutable(),
            CheckpointId     = checkpoint.CheckpointId,
            Timestamp     = checkpoint.Timestamp,
            LowWatermark  = low ?? defaultPos,
            HighWatermark = high ?? defaultPos
        };
    }

    /// <inheritdoc />
    public async ValueTask<ProcessorCheckpoint> LoadAsync(CheckpointId checkpointId, CancellationToken ct = default) {
        await EnsureReadyAsync(ct).ConfigureAwait(false);

        if (checkpointId.IsUnset)
            return ProcessorCheckpoint.Empty;

        try {
            var scanLimit = _maxCount > 0 ? _maxCount : 1000;

            var readOptions = ReadStream.From(_stream)
                .FromEnd()
                .WithMaxRecords(scanLimit)
                .WithCancellationToken(ct)
                .Build();

            await using var recordStream = await _client
                .ReadRecords(readOptions)
                .ConfigureAwait(false);

            await foreach (var message in recordStream.WithCancellation(ct).ConfigureAwait(false)) {
                if (!message.IsRecord)
                    continue;

                if (message.AsRecord.Value is ProcessorCheckpointState state && state.CheckpointId == checkpointId.Value) {
                    var checkpoint = MapToCheckpoint(state);
                    LogLoaded(ProcessorId, checkpoint.Partitions.Count, state.CheckpointId);
                    return checkpoint;
                }
            }

            LogCheckpointNotFound(ProcessorId, checkpointId.Value);
            return ProcessorCheckpoint.Empty;
        }
        catch (StreamNotFoundException) {
            LogNoCheckpointFound(ProcessorId);
            return ProcessorCheckpoint.Empty;
        }
    }

    /// <inheritdoc />
    public async ValueTask<ProcessorCheckpoint> LoadAsync(
        CheckpointId checkpointId,
        Partition[] partitions,
        SubscriptionInitialPosition initialPosition,
        CancellationToken ct = default
    ) {
        if (partitions.Length == 0)
            return ProcessorCheckpoint.Empty;

        var checkpoint = await LoadAsync(checkpointId, ct).ConfigureAwait(false);

        var defaultPos = initialPosition == SubscriptionInitialPosition.Earliest
            ? RecordPosition.Earliest
            : RecordPosition.Latest;

        // Build resolved positions for all requested partitions
        var builder = ImmutableDictionary.CreateBuilder<Partition, PartitionCheckpoint>();

        RecordPosition? low = null;
        RecordPosition? high = null;

        foreach (var partition in partitions) {
            RecordPosition position;

            if (checkpoint.Partitions.TryGetValue(partition, out var existing)) {
                position = existing.Position;
                builder.Add(partition, existing);
            }
            else {
                position = defaultPos with { Partition = partition };
                builder.Add(partition, new PartitionCheckpoint(position, string.Empty));
            }

            // Track watermarks
            if (low is null || position < low)
                low = position;

            if (high is null || position > high)
                high = position;
        }

        return new ProcessorCheckpoint {
            Partitions    = builder.ToImmutable(),
            CheckpointId     = checkpoint.CheckpointId,
            Timestamp     = checkpoint.Timestamp,
            LowWatermark  = low ?? defaultPos,
            HighWatermark = high ?? defaultPos
        };
    }

    /// <summary>
    /// Stores checkpoint positions for the specified partitions, merging with existing positions.
    /// </summary>
    /// <param name="workerId">The worker storing the checkpoint (for attribution).</param>
    /// <param name="checkpointId">The checkpoint that triggered this checkpoint.</param>
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
        CheckpointId checkpointId,
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
            var merged = MergePositions(currentState, workerId, checkpointId, positions);

            // 3. Attempt to store with expected revision
            var request = AppendToStreamRequest.For(_stream)
                .ExpectingState(expectedRevision)
                .WithRecord(rec => rec
                    .WithValue(merged)
                    .WithMetadata("processor_id", ProcessorId)
                    .WithMetadata("worker_id", workerId))
                .Build();

            try {
                var result = await _client.AppendToStream(request, ct).ConfigureAwait(false);
                LogStored(ProcessorId, workerId, positions.Count, result.Revision);
                return;
            }
            catch (AppendConsistencyViolationException) {
                LogConcurrencyConflict(ProcessorId, workerId);
                continue; // Retry
            }
        }
    }

    async ValueTask<(ProcessorCheckpointState State, ExpectedStreamState Revision)> LoadCurrentStateAsync(
        CancellationToken ct
    ) {
        try {
            var record = await _client
                .ReadLastStreamRecord(_stream, skipDecoding: false, ct)
                .ConfigureAwait(false);

            if (record.Value is ProcessorCheckpointState state)
                return (state, record.Position.Revision);

            // Stream exists but has no valid checkpoint - stream is empty, expect it to exist
            return (new ProcessorCheckpointState(), ExpectedStreamCondition.Exists);
        }
        catch (StreamNotFoundException) {
            // Stream doesn't exist yet - use NoStream to create it
            return (new ProcessorCheckpointState(), ExpectedStreamCondition.NoStream);
        }
    }

    ProcessorCheckpointState MergePositions(
        ProcessorCheckpointState current,
        string workerId,
        CheckpointId checkpointId,
        ImmutableDictionary<Partition, RecordPosition> newPositions
    ) {
        var partitionsBuilder = current.Partitions.ToBuilder();

        foreach (var (partition, position) in newPositions)
            partitionsBuilder[partition.Value] = new StoredPartitionCheckpoint(position.LogPosition.Value, workerId);

        return new ProcessorCheckpointState {
            Partitions   = partitionsBuilder.ToImmutable(),
            CheckpointId    = checkpointId.Value,
            ProcessorId  = ProcessorId,
            LastWorkerId = workerId,
            Timestamp    = _time.GetUtcNow()
        };
    }

    async ValueTask EnsureReadyAsync(CancellationToken ct) {
        if (_isReady)
            return;

        if (_maxCount <= 0) {
            LogRetentionNotConfigured(ProcessorId);
        }
        else {
            var info = await _client.GetStreamInfo(_stream, ct).ConfigureAwait(false);

            if (!info.Metadata.HasMaxCount) {
                await _client
                    .ConfigureStreamDataRetention(_stream, new() { MaxCount = _maxCount }, ct)
                    .ConfigureAwait(false);

                LogRetentionConfigured(ProcessorId, _maxCount);
            }
        }

        _isReady = true;
        LogReady(ProcessorId, _stream);
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
        Message = "[{ProcessorId}] Loaded {PartitionCount} partitions @ checkpoint {CheckpointId}"
    )]
    partial void LogLoaded(string processorId, int partitionCount, string checkpointId);

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
        Message = "[{ProcessorId}] Checkpoint not found: {CheckpointId}"
    )]
    partial void LogCheckpointNotFound(string processorId, string checkpointId);

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

        public string CheckpointId { get; init; } = "";

        public string ProcessorId { get; init; } = "";

        public string LastWorkerId { get; init; } = "";
    }
}
