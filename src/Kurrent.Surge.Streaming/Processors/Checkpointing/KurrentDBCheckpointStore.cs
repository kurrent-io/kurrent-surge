using Google.Protobuf.WellKnownTypes;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Kurrent.Schema;

namespace Kurrent.Streaming.Checkpointing;

/// <summary>
/// Event-sourced <see cref="CheckpointStoreBase"/> backed by KurrentDB.
/// </summary>
/// <remarks>
/// <para>
/// Stores one <see cref="PartitionsCheckpointed"/> event per drone per checkpoint cycle,
/// carrying all that drone's partition positions as an array.
/// State is reconstructed by folding events backwards from the stream tail.
/// </para>
/// <para>
/// A <see cref="CheckpointReset"/> event acts as a barrier — all prior events are superseded.
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
public sealed partial class KurrentDBCheckpointStore : CheckpointStoreBase {
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
    /// <param name="time">Optional time provider for timestamps.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <remarks>
    /// Checkpoint contract types are exported via this assembly's message catalog provider
    /// (see <c>KurrentStreamingCatalog</c>) and join every client through catalog
    /// auto-discovery — no per-store registration required or possible: the client's
    /// resolver snapshot is taken at client construction.
    /// </remarks>
    public KurrentDBCheckpointStore(
        string processorId,
        int partitionCount,
        int maxCount,
        StreamsClient client,
        TimeProvider time,
        ILogger<KurrentDBCheckpointStore> logger
    ) : base(processorId, time, logger) {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(partitionCount, 0);

        _partitionCount = partitionCount;
        _maxCount       = maxCount;
        _client         = client;
        _logger         = logger;
        _stream         = $"$processors/{processorId}/checkpoints";
    }

    protected override string ReadyTarget => _stream;

    #region ->> Hooks <<-

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    protected override async ValueTask<ProcessorCheckpoint> OnLoadLatestAsync(CancellationToken ct) {
        try {
            var readOptions = new ReadStreamOptions { Start = Revision.Max, Direction = ReadDirection.Backwards };

            var partitions      = new Dictionary<int, PartitionPosition>();
            var latestTimestamp = DateTimeOffset.MinValue;

            await foreach (var message in _client.ReadStream(_stream, readOptions, ct).ConfigureAwait(false)) {
                if (!message.IsRecord)
                    continue;

                var value = message.AsRecord.Value;

                if (value is CheckpointReset reset) {
                    if (latestTimestamp == DateTimeOffset.MinValue)
                        latestTimestamp = reset.Timestamp?.ToDateTimeOffset() ?? DateTimeOffset.MinValue;

                    // The reset is a single global position — a barrier. Fan it across every partition that
                    // has not committed since (already-collected partitions keep their newer commit). Unset
                    // is a full wipe: fan nothing, so ResolveStartPositions falls the group back to the
                    // configured InitialPosition.
                    if (LogPosition.From(reset.Position) != LogPosition.Unset)
                        for (var partition = 0; partition < _partitionCount; partition++)
                            partitions.TryAdd(partition, new PartitionPosition { Partition = partition, Position = reset.Position });

                    break;
                }
                else if (value is PartitionsCheckpointed evt) {
                    if (latestTimestamp == DateTimeOffset.MinValue)
                        latestTimestamp = evt.Timestamp?.ToDateTimeOffset() ?? DateTimeOffset.MinValue;

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

    /// <inheritdoc/>
    protected override async ValueTask OnStoreAsync(
        string droneId,
        string checkpointId,
        RecordPosition[] positions,
        CancellationToken ct
    ) {
        var evt = new PartitionsCheckpointed {
            ProcessorId  = ProcessorId,
            DroneId      = droneId,
            CheckpointId = checkpointId,
            Timestamp    = Timestamp.FromDateTimeOffset(Time.GetUtcNow()),
            Positions    = { ToContractPositions(positions) }
        };

        var appendRecord = CreateRecord(droneId, evt);

        await _client.AppendStream(_stream, [appendRecord], ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async ValueTask OnResetAsync(
        string droneId,
        string checkpointId,
        LogPosition position,
        CancellationToken ct
    ) {
        // A reset is a single global position, persisted as one barrier event. It is NOT completed to
        // stream+revision here: the runtime orders by log position, and a stream-targeted resume gets its
        // revision lazily from the client at subscribe. Sentinels (Earliest/Latest/Unset) pass through as
        // their raw values. On load the barrier is fanned across every partition (see OnLoadLatestAsync).
        var evt = new CheckpointReset {
            ProcessorId  = ProcessorId,
            DroneId      = droneId,
            CheckpointId = checkpointId,
            Timestamp    = Timestamp.FromDateTimeOffset(Time.GetUtcNow()),
            Position     = position.Value
        };

        await _client.AppendStream(_stream, [CreateRecord(droneId, evt)], ct).ConfigureAwait(false);
    }

    #endregion

    #region ->> Mapping <<-

    AppendRecord CreateRecord<T>(string droneId, T evt) where T : class =>
        AppendRecord.Create(evt, r => {
            r.Stream     = _stream;
            r.DataFormat = SchemaDataFormat.Protobuf;
            r.Metadata.With("processor_id", ProcessorId)
                      .With("drone_id", droneId);
        });

    static PartitionPosition[] ToContractPositions(RecordPosition[] positions) =>
        positions.Select(p => {
            var position = new PartitionPosition {
                Partition   = p.Partition,
                Position = p.LogPosition
            };

            // Stream context is optional — a position may carry only a log position. Stream and
            // revision move together (RecordPosition holds them as a pair), so set both or neither;
            // this keeps the paired presence check on load (HasStream && HasStreamRevision) truthful.
            if (p.Revision != Revision.Unset) {
                position.Stream         = p.Stream;
                position.StreamRevision = p.Revision;
            }

            return position;
        }).ToArray();

    static ProcessorCheckpoint MapToCheckpoint(Dictionary<int, PartitionPosition> events, DateTimeOffset timestamp) {
        var positions = events.Select(kvp => {
            var partition = Partition.From(kvp.Key);
            var evt       = kvp.Value;

            return evt is { HasStream: true, HasStreamRevision: true }
                ? RecordPosition.From(evt.Position, evt.Stream, evt.StreamRevision, partition)
                : RecordPosition.From(evt.Position, partition);
        }).ToArray();

        return new() {
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
