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
                LogNoCheckpointFound(ProcessorId);
                return ProcessorCheckpoint.Empty;
            }

            var checkpoint = MapToCheckpoint(state);
            LogLoaded(ProcessorId, checkpoint.Partitions.Count, state.BarrierId);
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
            BarrierId     = new BarrierId(state.BarrierId),
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
            BarrierId     = checkpoint.BarrierId,
            Timestamp     = checkpoint.Timestamp,
            LowWatermark  = low ?? defaultPos,
            HighWatermark = high ?? defaultPos
        };
    }

    /// <inheritdoc />
    public async ValueTask<ProcessorCheckpoint> LoadAsync(BarrierId barrierId, CancellationToken ct = default) {
        await EnsureReadyAsync(ct).ConfigureAwait(false);

        if (barrierId.IsEmpty)
            return ProcessorCheckpoint.Empty;

        try {
            var scanLimit = _maxCount > 0 ? _maxCount : 1000;

            await using var recordStream = await _client
                .ReadStreamRecords(options => options with {
                    Stream            = _stream,
                    Direction         = ReadDirection.Backwards,
                    Start             = StreamRevision.Max,
                    MaxRecords        = scanLimit,
                    CancellationToken = ct
                })
                .UnwrapAsync()
                .ConfigureAwait(false);

            await foreach (var message in recordStream.WithCancellation(ct).ConfigureAwait(false)) {
                if (!message.IsRecord)
                    continue;

                if (message.AsRecord.Value is ProcessorCheckpointState state && state.BarrierId == barrierId.Value) {
                    var checkpoint = MapToCheckpoint(state);
                    LogLoaded(ProcessorId, checkpoint.Partitions.Count, state.BarrierId);
                    return checkpoint;
                }
            }

            LogBarrierNotFound(ProcessorId, barrierId.Value);
            return ProcessorCheckpoint.Empty;
        }
        catch (StreamNotFoundException) {
            LogNoCheckpointFound(ProcessorId);
            return ProcessorCheckpoint.Empty;
        }
    }

    /// <inheritdoc />
    public async ValueTask<ProcessorCheckpoint> LoadAsync(
        BarrierId barrierId,
        Partition[] partitions,
        SubscriptionInitialPosition initialPosition,
        CancellationToken ct = default
    ) {
        if (partitions.Length == 0)
            return ProcessorCheckpoint.Empty;

        var checkpoint = await LoadAsync(barrierId, ct).ConfigureAwait(false);

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
            BarrierId     = checkpoint.BarrierId,
            Timestamp     = checkpoint.Timestamp,
            LowWatermark  = low ?? defaultPos,
            HighWatermark = high ?? defaultPos
        };
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
                    .WithMetadata("processor_id", ProcessorId)
                    .WithMetadata("worker_id", workerId))
                .Build();

            var result = await _client.Append(request, ct).ConfigureAwait(false);

            if (result.IsSuccess) {
                LogStored(ProcessorId, workerId, positions.Count, result.Value.StreamRevision);
                return;
            }

            // Check if it's a concurrency conflict
            if (result.Error.Value is StreamRevisionConflictException) {
                LogConcurrencyConflict(ProcessorId, workerId);
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
            var info = await _client.GetStreamInfo(_stream, ct).UnwrapAsync().ConfigureAwait(false);

            if (!info.Metadata.HasMaxCount) {
                await _client
                    .ConfigureStreamDataRetention(_stream, new() { MaxCount = _maxCount }, ct)
                    .UnwrapAsync()
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
        Message = "[{ProcessorId}] Barrier not found: {BarrierId}"
    )]
    partial void LogBarrierNotFound(string processorId, string barrierId);

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
