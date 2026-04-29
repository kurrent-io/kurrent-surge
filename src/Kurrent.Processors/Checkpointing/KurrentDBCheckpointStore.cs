using Kurrent.Client.Exceptions;
using Kurrent.Client.Schema;
using Kurrent.Client.Streams;

namespace Kurrent.Processors.Checkpointing;

/// <summary>
/// Event-sourced <see cref="CheckpointStore"/> backed by KurrentDB.
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
public sealed partial class KurrentDBCheckpointStore : CheckpointStore {
    readonly StreamsClient _client;
    readonly ILogger       _logger;
    readonly int           _maxCount;
    readonly int           _partitionCount;
    readonly StreamName    _stream;

    /// <summary>
    /// Initializes a new instance of the <see cref="KurrentDBCheckpointStore"/> class.
    /// </summary>
    /// <param name="processorId">The processor group identifier this store is bound to.</param>
    /// <param name="partitionCount">Total number of partitions in the processor group.</param>
    /// <param name="maxCount">
    /// Retention count: actual KurrentDB MaxCount = maxCount * partitionCount.
    /// Set to 0 to disable retention (stream grows unbounded).
    /// </param>
    /// <param name="client">The streams client for KurrentDB operations.</param>
    /// <param name="typeMapper">The type mapper for schema registration. Required for deserialization of checkpoint events.</param>
    /// <param name="time">Optional time provider for timestamps.</param>
    /// <param name="logger">Optional logger instance.</param>
    public KurrentDBCheckpointStore(
        string processorId,
        int partitionCount,
        int maxCount,
        StreamsClient client,
        MessageTypeMapper typeMapper,
        TimeProvider time,
        ILogger<KurrentDBCheckpointStore> logger
    ) : base(processorId, time, logger) {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(partitionCount, 0);
        ArgumentNullException.ThrowIfNull(typeMapper);

        _partitionCount = partitionCount;
        _maxCount       = maxCount;
        _client         = client;
        _logger         = logger ?? NullLogger<KurrentDBCheckpointStore>.Instance;
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

    protected override string ReadyTarget => _stream;

    #region ->> Hooks <<-

    protected override async ValueTask OnInitializeAsync(CancellationToken ct) {
        if (_maxCount <= 0) {
            LogRetentionNotConfigured(ProcessorId);
            return;
        }

        var info = await _client.GetStreamInfo(_stream, ct).ConfigureAwait(false);

        if (_maxCount != info.Retention.MaxCount) {
            await _client.ConfigureDataRetention(_stream, _maxCount, ct).ConfigureAwait(false);
            LogRetentionConfigured(ProcessorId, _maxCount);
        }
    }

    protected override async ValueTask<ProcessorCheckpoint> OnLoadLatestAsync(CancellationToken ct) {
        try {
            var readRequest = ReadStreamRequest.New.ForStream(_stream).FromEnd().Build();

            var partitions      = new Dictionary<int, Contracts.PartitionPosition>();
            var latestTimestamp = DateTimeOffset.MinValue;

            await foreach (var message in _client.ReadStream(readRequest, ct).ConfigureAwait(false)) {
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

            return partitions.Count == 0
                ? ProcessorCheckpoint.Empty
                : MapToCheckpoint(partitions, latestTimestamp);
        }
        catch (StreamNotFoundException) {
            return ProcessorCheckpoint.Empty;
        }
    }

    protected override async ValueTask OnStoreAsync(
        string droneId,
        string checkpointId,
        PartitionPosition[] positions,
        CancellationToken ct
    ) {
        var evt = new Contracts.PartitionsCheckpointed {
            ProcessorId  = ProcessorId,
            DroneId      = droneId,
            CheckpointId = checkpointId,
            Timestamp    = Time.GetUtcNow(),
            Positions    = ToContractPositions(positions)
        };

        var request = CreateAppendRequest(droneId, evt);

        await _client.AppendStream(request, ct).ConfigureAwait(false);
    }

    protected override async ValueTask OnResetAsync(
        string droneId,
        string checkpointId,
        PartitionPosition[] positions,
        CancellationToken ct
    ) {
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
            Timestamp    = Time.GetUtcNow(),
            Positions    = ToContractPositions(positions)
        };

        var request = CreateAppendRequest(droneId, evt);

        await _client.AppendStream(request, ct).ConfigureAwait(false);
    }

    #endregion

    #region ->> Mapping <<-

    AppendStreamRequest CreateAppendRequest<T>(string droneId, T evt) where T : class =>
        AppendStreamRequest.For(_stream)
            .ExpectingState(ExpectedStreamState.Any)
            .WithRecord(rec => rec
                .WithValue(evt)
                .WithMetadata("processor_id", ProcessorId)
                .WithMetadata("drone_id", droneId))
            .Build();

    static Contracts.PartitionPosition[] ToContractPositions(PartitionPosition[] positions) =>
        positions.Select(p => new Contracts.PartitionPosition {
            Partition      = p.Partition.Value,
            LogPosition    = p.Position.LogPosition,
            Stream         = p.Position.Stream,
            StreamRevision = p.Position.Revision == Revision.Unset ? null : (int?)p.Position.Revision
        }).ToArray();

    static ProcessorCheckpoint MapToCheckpoint(Dictionary<int, Contracts.PartitionPosition> events, DateTimeOffset timestamp) {
        var positions = events.Select(kvp => {
            var partition = Partition.From(kvp.Key);
            var evt       = kvp.Value;

            var position = evt.Stream is not null && evt.StreamRevision is not null
                ? RecordPosition.From(evt.LogPosition, evt.Stream, evt.StreamRevision, partition)
                : RecordPosition.From(evt.LogPosition, partition);

            return new PartitionPosition(partition, position);
        }).ToArray();

        return new ProcessorCheckpoint {
            Positions = positions,
            Timestamp = timestamp
        };
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

    #endregion
}
