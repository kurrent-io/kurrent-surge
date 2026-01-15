using System.Collections.Concurrent;
using System.Collections.Immutable;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Client.Processors.Checkpointing;

/// <summary>
/// Shared checkpoint store for processor groups using document-sourcing pattern.
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
public sealed partial class ProcessorCheckpointStore : IProcessorCheckpointStore {
    readonly StreamsClient _client;
    readonly ILogger       _logger;
    readonly int           _maxCount;
    readonly TimeProvider  _time;

    /// <summary>
    /// Tracks which processor streams have been initialized.
    /// </summary>
    readonly ConcurrentDictionary<string, bool> _readyStreams = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessorCheckpointStore"/> class.
    /// </summary>
    /// <param name="maxCount">Maximum number of checkpoint records to retain per processor stream.</param>
    /// <param name="client">The streams client for KurrentDB operations.</param>
    /// <param name="time">Optional time provider for timestamps.</param>
    /// <param name="logger">Optional logger instance.</param>
    public ProcessorCheckpointStore(
        int maxCount,
        StreamsClient client,
        TimeProvider? time = null,
        ILogger<ProcessorCheckpointStore>? logger = null
    ) {
        _maxCount = maxCount;
        _client   = client;
        _time     = time ?? TimeProvider.System;
        _logger   = logger ?? NullLogger<ProcessorCheckpointStore>.Instance;
    }

    /// <inheritdoc />
    public async ValueTask<ProcessorCheckpoint> LoadAsync(
        string processorId,
        CancellationToken ct = default
    ) {
        var stream = GetStream(processorId);
        await EnsureStreamReadyAsync(processorId, stream, ct).ConfigureAwait(false);

        var record = await _client
            .ReadLastStreamRecord(stream, skipDecoding: false, ct)
            .UnwrapAsync()
            .ConfigureAwait(false);

        if (record.Value is not ProcessorCheckpointState state || state.Partitions.IsEmpty) {
            LogNoCheckpointFound(processorId);
            return ProcessorCheckpoint.Empty;
        }

        var partitions = state.Partitions
            .ToImmutableDictionary(
                kvp => Partition.From(kvp.Key),
                kvp => new PartitionCheckpoint(kvp.Value.Position, kvp.Value.WorkerId));

        LogLoaded(processorId, partitions.Count, state.BarrierId);

        return new ProcessorCheckpoint {
            Partitions = partitions,
            BarrierId  = state.BarrierId,
            Timestamp  = state.Timestamp
        };
    }

    /// <inheritdoc />
    public async ValueTask StoreAsync(
        string processorId,
        string workerId,
        LogPosition barrierId,
        ImmutableDictionary<Partition, LogPosition> positions,
        CancellationToken ct = default
    ) {
        if (positions.IsEmpty)
            throw new InvalidOperationException("Cannot store empty positions dictionary.");

        var stream = GetStream(processorId);
        await EnsureStreamReadyAsync(processorId, stream, ct).ConfigureAwait(false);

        // Retry loop for optimistic concurrency conflicts
        while (true) {
            ct.ThrowIfCancellationRequested();

            // 1. Load current checkpoint state
            var (currentState, expectedRevision) = await LoadCurrentStateAsync(stream, ct)
                .ConfigureAwait(false);

            // 2. Merge new positions with existing
            var merged = MergePositions(currentState, processorId, workerId, barrierId, positions);

            // 3. Attempt to store with expected revision
            var request = AppendStreamRequest.New()
                .ForStream(stream)
                .ExpectingRevision(expectedRevision)
                .WithRecord(rec => rec
                    .WithValue(merged)
                    .WithMetadata("processor_id", processorId)
                    .WithMetadata("worker_id", workerId))
                .Build();

            var result = await _client.Append(request, ct).ConfigureAwait(false);

            if (result.IsSuccess) {
                LogStored(processorId, workerId, positions.Count, result.Value.StreamRevision);
                return;
            }

            // Check if it's a concurrency conflict
            if (result.Error.Value is StreamRevisionConflictException) {
                LogConcurrencyConflict(processorId, workerId);
                continue; // Retry
            }

            // Other errors - propagate
            result.Unwrap();
        }
    }

    static StreamName GetStream(string processorId) =>
        $"$processors/{processorId}/checkpoints";

    async ValueTask<(ProcessorCheckpointState State, ExpectedStreamState Revision)> LoadCurrentStateAsync(
        StreamName stream,
        CancellationToken ct
    ) {
        var record = await _client
            .ReadLastStreamRecord(stream, skipDecoding: false, ct)
            .UnwrapAsync()
            .ConfigureAwait(false);

        if (record.Value is ProcessorCheckpointState state)
            return (state, record.FullPosition.StreamRevision);

        return (new ProcessorCheckpointState(), ExpectedStreamState.NoStream);
    }

    ProcessorCheckpointState MergePositions(
        ProcessorCheckpointState current,
        string processorId,
        string workerId,
        LogPosition barrierId,
        ImmutableDictionary<Partition, LogPosition> newPositions
    ) {
        var partitionsBuilder = current.Partitions.ToBuilder();

        foreach (var (partition, position) in newPositions)
            partitionsBuilder[partition.Value] = new StoredPartitionCheckpoint(position, workerId);

        return new ProcessorCheckpointState {
            Partitions   = partitionsBuilder.ToImmutable(),
            BarrierId    = barrierId,
            ProcessorId  = processorId,
            LastWorkerId = workerId,
            Timestamp    = _time.GetUtcNow()
        };
    }

    async ValueTask EnsureStreamReadyAsync(string processorId, StreamName stream, CancellationToken ct) {
        if (_readyStreams.ContainsKey(processorId))
            return;

        var info = await _client.GetStreamInfo(stream, ct).UnwrapAsync().ConfigureAwait(false);

        if (info == StreamInfo.None || !info.Metadata.HasMaxCount) {
            if (_maxCount <= 0) {
                LogRetentionNotConfigured(processorId);
            } else {
                await _client
                    .ConfigureStreamDataRetention(stream, new() { MaxCount = _maxCount }, ct)
                    .UnwrapAsync()
                    .ConfigureAwait(false);

                LogRetentionConfigured(processorId, _maxCount);
            }
        }

        _readyStreams.TryAdd(processorId, true);
        LogReady(processorId, stream);
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
    partial void LogLoaded(string processorId, int partitionCount, long barrierId);

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
    sealed record StoredPartitionCheckpoint(LogPosition Position, string WorkerId);

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

        /// <summary>
        /// UTC timestamp when this checkpoint was created.
        /// </summary>
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.MinValue;

        /// <summary>
        /// The barrier ID that triggered this checkpoint.
        /// </summary>
        public LogPosition BarrierId { get; init; } = LogPosition.Unset;

        /// <summary>
        /// The processor this checkpoint belongs to.
        /// </summary>
        public string ProcessorId { get; init; } = "";

        /// <summary>
        /// The last worker that updated this checkpoint.
        /// </summary>
        public string LastWorkerId { get; init; } = "";
    }
}
