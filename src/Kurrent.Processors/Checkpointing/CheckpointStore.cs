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

            var positions = checkpoint.Partitions
                .Where(x => x.Value.Position.LogPosition > LogPosition.Earliest)
                .Select(x => $"[{x.Key}] {x.Value.Position}") // For logging purposes, include worker attribution
                .ToArray();

            LogLoaded(ProcessorId, checkpoint.Partitions.Count, state.CheckpointId, string.Join(", ", positions));

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
                    var position = RecordPosition.FromLogPosition(kvp.Value.Position, Partition.From(kvp.Key));
                    if (low is null || position < low) low = position;
                    if (high is null || position > high) high = position;
                    return new PartitionCheckpoint(position, kvp.Value.WorkerId);
                });

        return new ProcessorCheckpoint {
            Partitions    = partitions,
            CheckpointId  = new CheckpointId(state.CheckpointId),
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
            if (low is null || position < low) low = position;

            if (high is null || position > high) high = position;
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
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);

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

                    var positions = checkpoint.Partitions
                        .Where(x => x.Value.Position.LogPosition > LogPosition.Earliest)
                        .Select(x => $"[{x.Key}] {x.Value.Position}") // For logging purposes, include worker attribution
                        .ToArray();

                    LogLoaded(ProcessorId, checkpoint.Partitions.Count, state.CheckpointId, string.Join(", ", positions));
                    return checkpoint;
                }
            }

            LogCheckpointNotFound(ProcessorId, checkpointId);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);

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
            if (low is null || position < low) low = position;
            if (high is null || position > high) high = position;
        }

        return new ProcessorCheckpoint {
            Partitions    = builder.ToImmutable(),
            CheckpointId  = checkpoint.CheckpointId,
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
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
        ArgumentOutOfRangeException.ThrowIfZero(positions.Count, nameof(positions));

        await EnsureReadyAsync(ct).ConfigureAwait(false);

        // Retry loop for optimistic concurrency conflicts
        while (true) {
            ct.ThrowIfCancellationRequested();

            // 1. Load current checkpoint state
            var (currentState, expectedState) = await LoadCurrentStateAsync(ct).ConfigureAwait(false);

            // 2. Merge new positions with existing
            var mergedState = MergePositions(currentState, workerId, checkpointId, positions);

            // 3. Attempt to store with expected revision
            var request = AppendStreamRequest.For(_stream)
                .ExpectingState(expectedState)
                .WithRecord(rec => rec
                    .WithValue(mergedState)
                    .WithMetadata("processor_id", ProcessorId)
                    .WithMetadata("worker_id", workerId))
                .Build();

            try {
                var result = await _client.AppendStream(request, ct).ConfigureAwait(false);

                var storedPositions = mergedState.Partitions
                    .Where(x => x.Value.Position > LogPosition.Earliest)
                    .Select(x => $"[{x.Key}] {x.Value.Position}") // For logging purposes, include worker attribution
                    .ToArray();

                LogStored(ProcessorId, workerId, positions.Count, string.Join(", ", storedPositions));
                return;
            }
            catch (AppendConsistencyViolationException) {
                LogConcurrencyConflict(ProcessorId, workerId);
                continue; // Retry
            }
            catch (StreamAlreadyExistsException) {
                // NoStream expectation failed — stream was created by another worker
                LogConcurrencyConflict(ProcessorId, workerId);
                continue; // Retry
            }
            catch (StreamRevisionConflictException) {
                // Expected revision stale — another worker wrote a newer checkpoint
                LogConcurrencyConflict(ProcessorId, workerId);
                continue; // Retry
            }
        }
    }

    /// <summary>
    /// position earliest for initial, latest for end and any other position for any other
    /// </summary>
    /// <param name="workerId"></param>
    /// <param name="checkpointId"></param>
    /// <param name="position"></param>
    /// <param name="ct"></param>
    /// <exception cref="ArgumentException"></exception>
    public async ValueTask ResetAsync(
        string            workerId,
        CheckpointId      checkpointId,
        LogPosition    position,
        CancellationToken ct = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);

        if (position == LogPosition.Unset)
            throw new ArgumentException("Invalid log position", nameof(position));

        await EnsureReadyAsync(ct).ConfigureAwait(false);

        var builder = ImmutableDictionary.CreateBuilder<int, StoredPartitionCheckpoint>();
        builder.Add(0, new StoredPartitionCheckpoint(position, workerId));
        var partitions = builder.ToImmutable();

        // Retry loop for optimistic concurrency conflicts
        while (true) {
            ct.ThrowIfCancellationRequested();

            // 1. Load current checkpoint state
            var (_, expectedStreamState) = await LoadCurrentStateAsync(ct).ConfigureAwait(false);

            // 2. Build new checkpoint state with single partition 0 for the reset position
            var resetState = new ProcessorCheckpointState {
                Partitions   = partitions,
                CheckpointId = checkpointId,
                ProcessorId  = ProcessorId,
                LastWorkerId = workerId,
                Timestamp    = _time.GetUtcNow()
            };

            // 3. Attempt to store with expected revision
            var request = AppendStreamRequest.For(_stream)
                .ExpectingState(expectedStreamState)
                .WithRecord(rec => rec
                    .WithValue(resetState)
                    .WithMetadata("processor_id", ProcessorId)
                    .WithMetadata("worker_id", workerId))
                .Build();

            try {
                var result = await _client.AppendStream(request, ct).ConfigureAwait(false);

                var storedPositions = resetState.Partitions
                    .Where(x => x.Value.Position > LogPosition.Earliest)
                    .Select(x => $"[{x.Key}] {x.Value.Position}") // For logging purposes, include worker attribution
                    .ToArray();

                LogStored(
                    ProcessorId,
                    workerId,
                    partitions.Count,
                    string.Join(", ", storedPositions));

                return;
            } catch (AppendConsistencyViolationException) {
                LogConcurrencyConflict(ProcessorId, workerId);

                continue; // Retry
            } catch (StreamAlreadyExistsException) {
                // NoStream expectation failed — stream was created by another worker
                LogConcurrencyConflict(ProcessorId, workerId);

                continue; // Retry
            } catch (StreamRevisionConflictException) {
                // Expected revision stale — another worker wrote a newer checkpoint
                LogConcurrencyConflict(ProcessorId, workerId);

                continue; // Retry
            }
        }
    }

    async ValueTask<(ProcessorCheckpointState State, ExpectedStreamState ExpectedStreamState)> LoadCurrentStateAsync(
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
            partitionsBuilder[partition.Value] = new StoredPartitionCheckpoint(position.LogPosition, workerId);

        return new ProcessorCheckpointState {
            Partitions   = partitionsBuilder.ToImmutable(),
            CheckpointId = checkpointId,
            ProcessorId  = ProcessorId,
            LastWorkerId = workerId,
            Timestamp    = _time.GetUtcNow()
        };
    }

    async ValueTask EnsureReadyAsync(CancellationToken ct) {
        if (_isReady) return;

        if (_maxCount > 0) {
            var info = await _client.GetStreamInfo(_stream, ct).ConfigureAwait(false);

            if (!info.Retention.HasMaxCount) {
                await _client
                    .ConfigureDataRetention(_stream, _maxCount, ct)
                    .ConfigureAwait(false);

                LogRetentionConfigured(ProcessorId, _maxCount);
            }
        }
        else LogRetentionNotConfigured(ProcessorId);

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
        Message = "[{ProcessorId}] Loaded {PartitionCount} partitions @ checkpoint {CheckpointId} :: {Positions}"
    )]
    partial void LogLoaded(string processorId, int partitionCount, string checkpointId, string positions);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[{ProcessorId}/{WorkerId}] Stored {PartitionCount} partitions: {Positions}"
    )]
    partial void LogStored(string processorId, string workerId, int partitionCount, string positions);

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
