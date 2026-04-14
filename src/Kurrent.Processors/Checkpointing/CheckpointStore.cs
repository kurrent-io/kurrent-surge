using System.Collections.Immutable;
using Kurrent.Client.Exceptions;
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

    public ValueTask Initialize(CancellationToken ct) => EnsureReadyAsync(ct);

    async ValueTask EnsureReadyAsync(CancellationToken ct) {
        if (_isReady) return;

        if (_maxCount <= 0)
            LogRetentionNotConfigured(ProcessorId);
        else {
            var info = await _client.GetStreamInfo(_stream, ct).ConfigureAwait(false);

            if (_maxCount != info.Retention.MaxCount) {
                await _client
                    .ConfigureDataRetention(_stream, _maxCount, ct)
                    .ConfigureAwait(false);

                LogRetentionConfigured(ProcessorId, _maxCount);
            }
        }

        _isReady = true;
        LogReady(ProcessorId, _stream);
    }

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

            LogLoaded(ProcessorId, checkpoint.Positions.Length, state.CheckpointId, FormatPositions(checkpoint));

            return checkpoint;
        }
        catch (StreamNotFoundException) {
            LogNoCheckpointFound(ProcessorId);
            return ProcessorCheckpoint.Empty;
        }
    }

    static ProcessorCheckpoint MapToCheckpoint(ProcessorCheckpointState state) {
        if (state.Partitions.IsEmpty)
            return ProcessorCheckpoint.Empty;

        var positions = state.Partitions
            .Select(kvp => {
                var partition = Partition.From(kvp.Key);
                var position  = RecordPosition.FromLogPosition(kvp.Value.Position, partition);
                return new PartitionPosition(partition, position);
            })
            .ToArray();

        return new ProcessorCheckpoint {
            Positions = positions,
            Timestamp = state.Timestamp
        };
    }

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

        // Index existing positions for lookup
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

    public async ValueTask StoreAsync(
        string workerId,
        string checkpointId,
        PartitionPosition[] positions,
        CancellationToken ct = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
        ArgumentOutOfRangeException.ThrowIfZero(positions.Length, nameof(positions));

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
                await _client.AppendStream(request, ct).ConfigureAwait(false);

                LogStored(ProcessorId, workerId, positions.Length, FormatInputPositions(positions));
                return;
            }
            catch (AppendConsistencyViolationException) {
                LogConcurrencyConflict(ProcessorId, workerId);
                continue;
            }
            catch (StreamAlreadyExistsException) {
                LogConcurrencyConflict(ProcessorId, workerId);
                continue;
            }
            catch (StreamRevisionConflictException) {
                LogConcurrencyConflict(ProcessorId, workerId);
                continue;
            }
        }
    }

    public ValueTask ResetAsync(string workerId, string checkpointId, PartitionPosition[] positions, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask ResetAsync(string workerId, string checkpointId, RecordPosition position, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public async ValueTask ResetLegacyAsync(
        string workerId,
        string checkpointId,
        LogPosition position,
        CancellationToken ct = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);

        if (position == LogPosition.Unset)
            throw new ArgumentException("Invalid log position", nameof(position));

        await EnsureReadyAsync(ct).ConfigureAwait(false);

        var partitions = ImmutableDictionary.CreateBuilder<int, StoredPartitionCheckpoint>();
        partitions.Add(0, new StoredPartitionCheckpoint(position, workerId));

        // Retry loop for optimistic concurrency conflicts
        while (true) {
            ct.ThrowIfCancellationRequested();

            var (_, expectedStreamState) = await LoadCurrentStateAsync(ct).ConfigureAwait(false);

            var resetState = new ProcessorCheckpointState {
                Partitions   = partitions.ToImmutable(),
                CheckpointId = checkpointId,
                ProcessorId  = ProcessorId,
                LastWorkerId = workerId,
                Timestamp    = _time.GetUtcNow()
            };

            var request = AppendStreamRequest.For(_stream)
                .ExpectingState(expectedStreamState)
                .WithRecord(rec => rec
                    .WithValue(resetState)
                    .WithMetadata("processor_id", ProcessorId)
                    .WithMetadata("worker_id", workerId))
                .Build();

            try {
                await _client.AppendStream(request, ct).ConfigureAwait(false);

                LogStored(ProcessorId, workerId, 1, $"[0] {position}");
                return;
            }
            catch (AppendConsistencyViolationException) {
                LogConcurrencyConflict(ProcessorId, workerId);
                continue;
            }
            catch (StreamAlreadyExistsException) {
                LogConcurrencyConflict(ProcessorId, workerId);
                continue;
            }
            catch (StreamRevisionConflictException) {
                LogConcurrencyConflict(ProcessorId, workerId);
                continue;
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

            return (new ProcessorCheckpointState(), ExpectedStreamCondition.Exists);
        }
        catch (StreamNotFoundException) {
            return (new ProcessorCheckpointState(), ExpectedStreamCondition.NoStream);
        }
    }

    ProcessorCheckpointState MergePositions(
        ProcessorCheckpointState current,
        string workerId,
        string checkpointId,
        PartitionPosition[] newPositions
    ) {
        var partitionsBuilder = current.Partitions.ToBuilder();

        foreach (var pp in newPositions)
            partitionsBuilder[pp.Partition.Value] = new StoredPartitionCheckpoint(pp.Position.LogPosition, workerId);

        return new ProcessorCheckpointState {
            Partitions   = partitionsBuilder.ToImmutable(),
            CheckpointId = checkpointId,
            ProcessorId  = ProcessorId,
            LastWorkerId = workerId,
            Timestamp    = _time.GetUtcNow()
        };
    }

    #region Formatting

    static string FormatPositions(ProcessorCheckpoint checkpoint) {
        var positions = checkpoint.Positions
            .Where(x => x.Position.LogPosition > LogPosition.Earliest)
            .Select(x => $"[{x.Partition}] {x.Position}")
            .ToArray();

        return string.Join(", ", positions);
    }

    static string FormatInputPositions(PartitionPosition[] positions) {
        var formatted = positions
            .Where(x => x.Position.LogPosition > LogPosition.Earliest)
            .Select(x => $"[{x.Partition}] {x.Position}")
            .ToArray();

        return string.Join(", ", formatted);
    }

    #endregion

    #region Logging

    [LoggerMessage(Level = LogLevel.Warning, Message = "[{ProcessorId}] Retention not configured, checkpoint stream will grow unbounded")]
    partial void LogRetentionNotConfigured(string processorId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{ProcessorId}] Retention configured with MaxCount={MaxCount}")]
    partial void LogRetentionConfigured(string processorId, int maxCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}] Ready on {Stream}")]
    partial void LogReady(string processorId, StreamName stream);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}] Loaded {PartitionCount} partitions @ checkpoint {CheckpointId} :: {Positions}")]
    partial void LogLoaded(string processorId, int partitionCount, string checkpointId, string positions);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{WorkerId}] Stored {PartitionCount} partitions: {Positions}")]
    partial void LogStored(string processorId, string workerId, int partitionCount, string positions);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}] No checkpoint found")]
    partial void LogNoCheckpointFound(string processorId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{WorkerId}] Concurrency conflict, retrying...")]
    partial void LogConcurrencyConflict(string processorId, string workerId);

    #endregion

    sealed record StoredPartitionCheckpoint(long Position, string WorkerId);

    sealed record ProcessorCheckpointState {
        public ImmutableDictionary<int, StoredPartitionCheckpoint> Partitions { get; init; } =
            ImmutableDictionary<int, StoredPartitionCheckpoint>.Empty;

        public DateTimeOffset Timestamp    { get; init; } = DateTimeOffset.MinValue;
        public string         CheckpointId { get; init; } = "";
        public string         ProcessorId  { get; init; } = "";
        public string         LastWorkerId { get; init; } = "";
    }
}
