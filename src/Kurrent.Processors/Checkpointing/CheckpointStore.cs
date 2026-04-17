using Kurrent.Client.Exceptions;
using Kurrent.Client.Schema;
using Kurrent.Client.Streams;
using Kurrent.Processors.Models;

namespace Kurrent.Processors.Checkpointing;

/// <summary>
/// Event-sourced checkpoint store bound to a specific processor group.
/// </summary>
/// <remarks>
/// <para>
/// Stores one <see cref="Contracts.PartitionsCheckpointed"/> event per drone per checkpoint cycle,
/// carrying all that drone's partition positions as an array.
/// State is reconstructed by folding events backwards from the stream tail.
/// </para>
/// <para>
/// A <see cref="Contracts.CheckpointReset"/> event acts as a barrier — all prior events are superseded.
/// </para>
/// <para>
/// No fencing: all appends use <see cref="ExpectedStreamState.Any"/>.
/// Drones are independent distributed processes writing to the same stream.
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
    readonly int           _partitionCount;
    readonly StreamName    _stream;
    readonly TimeProvider  _time;

    volatile bool _isReady;

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckpointStore"/> class.
    /// </summary>
    /// <param name="processorId">The processor group identifier this store is bound to.</param>
    /// <param name="partitionCount">Total number of partitions in the processor group.</param>
    /// <param name="maxCount">
    /// Retention count: actual KurrentDB MaxCount = maxCount * partitionCount.
    /// Set to 0 to disable retention (stream grows unbounded).
    /// Default: 1000.
    /// </param>
    /// <param name="client">The streams client for KurrentDB operations.</param>
    /// <param name="typeMapper">The type mapper for schema registration. Required for deserialization of checkpoint events.</param>
    /// <param name="time">Optional time provider for timestamps.</param>
    /// <param name="logger">Optional logger instance.</param>
    public CheckpointStore(
        string processorId,
        int partitionCount,
        int maxCount,
        StreamsClient client,
        MessageTypeMapper typeMapper,
        TimeProvider? time = null,
        ILogger<CheckpointStore>? logger = null
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(processorId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(partitionCount, 0);
        ArgumentNullException.ThrowIfNull(typeMapper);

        ProcessorId     = processorId;
        _partitionCount = partitionCount;
        _maxCount       = maxCount;
        _client         = client;
        _time           = time ?? TimeProvider.System;
        _logger         = logger ?? NullLogger<CheckpointStore>.Instance;
        _stream         = $"$processors/{processorId}/checkpoints";

        RegisterContractTypes(typeMapper);
    }

    /// <summary>
    /// Registers checkpoint contract types with the given type mapper.
    /// Call this if the client was configured separately from the checkpoint store.
    /// </summary>
    public static void RegisterContractTypes(MessageTypeMapper typeMapper) {
        typeMapper.TryMap<Contracts.PartitionsCheckpointed>("Kurrent.Processors.Checkpointing.PartitionsCheckpointed");
        typeMapper.TryMap<Contracts.CheckpointReset>("Kurrent.Processors.Checkpointing.CheckpointReset");
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

    #region ->> Load <<-

    public async ValueTask<ProcessorCheckpoint> LoadLatestAsync(CancellationToken ct = default) {
        await EnsureReadyAsync(ct).ConfigureAwait(false);

        try {
            var readRequest = ReadStreamRequest.New.ForStream(_stream)
                .FromEnd()
                .Build();

            var recordStream = _client.ReadStream(readRequest, ct);

            var partitions     = new Dictionary<int, Contracts.PartitionPosition>();
            var latestTimestamp = DateTimeOffset.MinValue;

            await foreach (var message in recordStream.ConfigureAwait(false)) {
                if (!message.IsRecord)
                    continue;

                var value = message.AsRecord.Value;

                if (value is Contracts.CheckpointReset reset) {
                    if (latestTimestamp == DateTimeOffset.MinValue)
                        latestTimestamp = reset.Timestamp;

                    if (reset.Positions.Length == 0)
                        break;

                    foreach (var pos in reset.Positions)
                        partitions.TryAdd(pos.Partition, pos);
                }
                else if (value is Contracts.PartitionsCheckpointed evt) {
                    if (latestTimestamp == DateTimeOffset.MinValue)
                        latestTimestamp = evt.Timestamp;

                    foreach (var pos in evt.Positions)
                        partitions.TryAdd(pos.Partition, pos);

                    if (partitions.Count >= _partitionCount)
                        break;
                }
            }

            if (partitions.Count == 0) {
                LogNoCheckpointFound(ProcessorId);
                return ProcessorCheckpoint.Empty;
            }

            var checkpoint = MapToCheckpoint(partitions, latestTimestamp);

            LogLoaded(ProcessorId, checkpoint.Positions.Length, FormatPositions(checkpoint));

            return checkpoint;
        }
        catch (StreamNotFoundException) {
            LogNoCheckpointFound(ProcessorId);
            return ProcessorCheckpoint.Empty;
        }
    }

    public async ValueTask<ProcessorCheckpoint> LoadLatestAsync(
        Partition[] partitions,
        SubscriptionInitialPosition initialPosition,
        CancellationToken ct = default
    ) {
        if (partitions.Length == 0)
            return ProcessorCheckpoint.Empty;

        var checkpoint = await LoadLatestAsync(ct).ConfigureAwait(false);

        return ResolvePartitions(checkpoint, partitions, initialPosition);
    }

    #endregion

    #region ->> Store <<-

    public async ValueTask StoreAsync(
        string droneId,
        string checkpointId,
        PartitionPosition[] positions,
        CancellationToken ct = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(droneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
        ArgumentOutOfRangeException.ThrowIfZero(positions.Length, nameof(positions));

        await EnsureReadyAsync(ct).ConfigureAwait(false);

        var evt = new Contracts.PartitionsCheckpointed {
            ProcessorId  = ProcessorId,
            DroneId      = droneId,
            CheckpointId = checkpointId,
            Timestamp    = _time.GetUtcNow(),
            Positions = positions.Select(p => new Contracts.PartitionPosition {
                Partition      = p.Partition.Value,
                LogPosition    = p.Position.LogPosition,
                Stream         = p.Position.Stream,
                StreamRevision = p.Position.Revision == Revision.Unset ? null : (int?)p.Position.Revision
            }).ToArray()
        };

        var request = AppendStreamRequest.For(_stream)
            .ExpectingState(ExpectedStreamState.Any)
            .WithRecord(rec => rec
                .WithValue(evt)
                .WithMetadata("processor_id", ProcessorId)
                .WithMetadata("drone_id", droneId))
            .Build();

        await _client.AppendStream(request, ct).ConfigureAwait(false);

        LogStored(ProcessorId, droneId, positions.Length, FormatInputPositions(positions));
    }

    /// <summary>
    /// Resets checkpoint positions.
    /// Empty positions = full reset (barrier, all prior state wiped).
    /// Positions present = selective reset (those partitions repositioned).
    /// </summary>
    public async ValueTask ResetAsync(
        string droneId,
        string checkpointId,
        PartitionPosition[] positions,
        CancellationToken ct = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(droneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);

        await EnsureReadyAsync(ct).ConfigureAwait(false);

        // Resolve positions with unset streams from the database
        for (var i = 0; i < positions.Length; i++) {
            if (positions[i].Position.Stream.IsUnset) {
                var record = await _client
                    .InspectRecord(positions[i].Position.LogPosition, ct)
                    .ConfigureAwait(false);

                positions[i] = positions[i] with {
                    Position = RecordPosition.From(
                        record.Position.LogPosition,
                        record.Stream,
                        record.Revision,
                        positions[i].Partition)
                };
            }
        }

        var evt = new Contracts.CheckpointReset {
            ProcessorId  = ProcessorId,
            DroneId      = droneId,
            CheckpointId = checkpointId,
            Timestamp    = _time.GetUtcNow(),
            Positions = positions.Select(p => new Contracts.PartitionPosition {
                Partition      = p.Partition.Value,
                LogPosition    = p.Position.LogPosition,
                Stream         = p.Position.Stream,
                StreamRevision = p.Position.Revision == Revision.Unset ? null : (int?)p.Position.Revision
            }).ToArray()
        };

        var request = AppendStreamRequest.For(_stream)
            .ExpectingState(ExpectedStreamState.Any)
            .WithRecord(rec => rec
                .WithValue(evt)
                .WithMetadata("processor_id", ProcessorId)
                .WithMetadata("drone_id", droneId))
            .Build();

        await _client.AppendStream(request, ct).ConfigureAwait(false);

        LogReset(ProcessorId, droneId, positions.Length);
    }

    /// <summary>
    /// Resets all partitions to the same position, or performs a full wipe.
    /// <see cref="RecordPosition.Unset"/> = full wipe (barrier, all prior state cleared).
    /// Any other position = load current state and override all existing partitions to that position.
    /// </summary>
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

    #endregion

    #region ->> Mapping <<-

    static ProcessorCheckpoint MapToCheckpoint(
        Dictionary<int, Contracts.PartitionPosition> events,
        DateTimeOffset timestamp
    ) {
        var positions = events.Select(kvp => {
            var partition = Partition.From(kvp.Key);
            var evt       = kvp.Value;

            var position = evt.Stream is not null && evt.StreamRevision is not null
                ? RecordPosition.From(evt.LogPosition, evt.Stream, Revision.From(evt.StreamRevision.Value), partition)
                : RecordPosition.FromLogPosition(evt.LogPosition, partition);

            return new PartitionPosition(partition, position);
        }).ToArray();

        return new ProcessorCheckpoint {
            Positions = positions,
            Timestamp = timestamp
        };
    }

    static ProcessorCheckpoint ResolvePartitions(
        ProcessorCheckpoint checkpoint,
        Partition[] partitions,
        SubscriptionInitialPosition initialPosition
    ) {
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

    #endregion

    #region ->> Formatting <<-

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

    #region ->> Logging <<-

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "[{ProcessorId}] Retention not configured, checkpoint stream will grow unbounded"
    )]
    partial void LogRetentionNotConfigured(string processorId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[{ProcessorId}] Retention configured with MaxCount={MaxCount}"
    )]
    partial void LogRetentionConfigured(string processorId, int maxCount);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[{ProcessorId}] Ready on {Stream}"
    )]
    partial void LogReady(string processorId, StreamName stream);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[{ProcessorId}] Loaded {PartitionCount} partitions :: {Positions}"
    )]
    partial void LogLoaded(string processorId, int partitionCount, string positions);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[{ProcessorId}/{DroneId}] Stored {PartitionCount} partitions: {Positions}"
    )]
    partial void LogStored(string processorId, string droneId, int partitionCount, string positions);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[{ProcessorId}] No checkpoint found"
    )]
    partial void LogNoCheckpointFound(string processorId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[{ProcessorId}/{DroneId}] Reset {PartitionCount} partitions"
    )]
    partial void LogReset(string processorId, string droneId, int partitionCount);

    #endregion
}
