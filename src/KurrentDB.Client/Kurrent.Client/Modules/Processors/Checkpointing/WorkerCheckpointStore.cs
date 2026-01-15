using System.Collections.Immutable;
using Kurrent.Client.Streams;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Client.Processors.Checkpointing;

/// <summary>
/// Worker checkpoint store implementation tied to a specific processor and worker instance.
/// </summary>
/// <remarks>
/// <para>
/// Uses a single checkpoint stream per worker, and maintains the expected stream revision
/// internally for optimistic concurrency.
/// </para>
/// <para>
/// Applies document-sourcing where the latest event in the stream represents the current
/// checkpoint state.
/// </para>
/// </remarks>
[PublicAPI]
public sealed partial class WorkerCheckpointStore {
    readonly string        _processorId;
    readonly string        _workerId;
    readonly StreamsClient _client;
    readonly ILogger       _logger;
    readonly int           _maxCount;
    readonly StreamName    _stream;
    readonly TimeProvider  _time;

    ExpectedStreamState _expectedRevision = ExpectedStreamState.Any;

    volatile bool _isReady;

    public WorkerCheckpointStore(
        string processorId,
        string workerId,
        int maxCount,
        StreamsClient client,
        TimeProvider? time = null,
        ILogger<WorkerCheckpointStore>? logger = null
    ) {
        _processorId = processorId;
        _workerId    = workerId;
        _maxCount   = maxCount;

        _client     = client;
        _time       = time ?? TimeProvider.System;
        _logger     = logger ?? NullLogger<WorkerCheckpointStore>.Instance;
        _stream     = $"$processors/{processorId}/workers/{workerId}/checkpoints";
    }


    public async ValueTask<ImmutableDictionary<int, LogPosition>> LoadLatestAsync(CancellationToken ct = default) {
        await EnsureReadyAsync(ct).ConfigureAwait(false);

        var record = await _client
            .ReadLastStreamRecord(_stream, skipDecoding: false, ct)
            .UnwrapAsync()
            .ConfigureAwait(false);

        if (record.Value is not WorkerCheckpoint checkpoint || checkpoint.Positions.IsEmpty) {
            LogNoCheckpointFound(_processorId, _workerId);
            return ImmutableDictionary<int, LogPosition>.Empty;
        }

        _expectedRevision = record.FullPosition.StreamRevision;
        var maxPosition = checkpoint.Positions.Values.Max();
        LogLoaded(_processorId, _workerId, maxPosition, _expectedRevision);
        return checkpoint.Positions;
    }

    public async ValueTask StoreAsync(
        LogPosition barrierId,
        ImmutableDictionary<int, LogPosition> positions,
        CancellationToken ct = default
    ) {
        await EnsureReadyAsync(ct).ConfigureAwait(false);

        if (positions.IsEmpty)
            throw new InvalidOperationException("Cannot save an empty positions dictionary as a checkpoint.");

        var state = new WorkerCheckpoint {
            BarrierId   = barrierId,
            Positions   = positions,
            ProcessorId = _processorId,
            WorkerId    = _workerId,
            Timestamp   = _time.GetUtcNow()
        };

        var request = AppendStreamRequest.New()
            .ForStream(_stream)
            .ExpectingRevision(_expectedRevision)
            .WithRecord(rec => rec
                .WithValue(state)
                .WithMetadata("processor_id", _processorId)
                .WithMetadata("worker_id", _workerId))
            .Build();

        var result = await _client.Append(request, ct)
            .UnwrapAsync()
            .ConfigureAwait(false);

        _expectedRevision = result.StreamRevision;

        var maxPosition = positions.Values.Max();
        LogStored(_processorId, _workerId, maxPosition, _expectedRevision);
    }

    async ValueTask EnsureReadyAsync(CancellationToken ct = default) {
        if (_isReady)
            return;

        var info = await _client.GetStreamInfo(_stream, ct).UnwrapAsync().ConfigureAwait(false);

        if (info == StreamInfo.None || !info.Metadata.HasMaxCount) {
            if (_maxCount <= 0)
                LogRetentionNotConfigured(_processorId, _workerId);
            else {
                await _client
                    .ConfigureStreamDataRetention(_stream, new() { MaxCount = _maxCount }, ct)
                    .UnwrapAsync()
                    .ConfigureAwait(false);

                LogRetentionConfigured(_processorId, _workerId, _maxCount);
            }
        }

        _isReady = true;
        LogReady(_processorId, _workerId, _stream);
    }

    #region Logging

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "[{ProcessorId}/{WorkerId}] Retention not configured, checkpoint stream will grow unbounded"
    )]
    partial void LogRetentionNotConfigured(string processorId, string workerId);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[{ProcessorId}/{WorkerId}] Retention configured with MaxCount={MaxCount}"
    )]
    partial void LogRetentionConfigured(string processorId, string workerId, int maxCount);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[{ProcessorId}/{WorkerId}] Ready on {Stream}"
    )]
    partial void LogReady(string processorId, string workerId, string stream);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[{ProcessorId}/{WorkerId}] Loaded checkpoint @ {LogPosition}"
    )]
    partial void LogLoaded(string processorId, string workerId, ulong logPosition, long expectedRevision);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[{ProcessorId}/{WorkerId}] Stored checkpoint @ {LogPosition}"
    )]
    partial void LogStored(string processorId, string workerId, ulong logPosition, long expectedRevision);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[{ProcessorId}/{WorkerId}] No checkpoint found"
    )]
    partial void LogNoCheckpointFound(string processorId, string workerId);

    #endregion

    sealed record WorkerCheckpoint {
        /// <summary>
        /// The unique identifier for the checkpoint (barrier log position).
        /// </summary>
        public LogPosition BarrierId { get; init; } = LogPosition.Unset;

        /// <summary>
        /// Per-partition checkpoint positions.
        /// </summary>
        public ImmutableDictionary<int, LogPosition> Positions { get; init; } =
            ImmutableDictionary<int, LogPosition>.Empty;

        /// <summary>
        /// UTC timestamp when the checkpoint was created.
        /// </summary>
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.MinValue;

        /// <summary>
        /// The unique identifier for the processor.
        /// </summary>
        public string ProcessorId { get; init; } = "";

        /// <summary>
        /// Worker that created the checkpoint.
        /// </summary>
        public string WorkerId { get; init; } = "";
    }
}
