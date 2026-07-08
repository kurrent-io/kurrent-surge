using System.Threading.Channels;
using Kurrent.Client;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Configuration;
using Kurrent.Streaming.Diagnostics;
using Kurrent.Streaming.Gateway;

namespace Kurrent.Streaming;

/// <summary>
/// EXPERIMENTAL — a child of <see cref="DroneCommander"/> owning ONE partition. A pure process-and-report
/// worker: it runs the user's processor (both shapes — <see cref="IRecordProcessor"/> and
/// <see cref="IRecordBatchProcessor"/>) over the records the commander routes to it, tracks its partition's
/// frontier, and reports progress up the control channel. It owns NO checkpointer — the commander owns the
/// one checkpointer, the cadence, and the commit. Stop, manual checkpoint, and fault are posted up.
/// </summary>
sealed class ChildDrone : IDrone {
    readonly Partition                     _partition;
    readonly RecordPosition                _floor;
    readonly IProcessor                    _processor;
    readonly ChannelWriter<CommanderEvent> _control;
    readonly ProgressTracker               _progress;
    readonly DroneTelemetry                _telemetry;
    readonly bool                          _autoTrack;
    readonly Channel<DataSourceMessage>     _channel;

    // The committable frontier for this child's single partition — owned by the Run loop, reported to the
    // commander after each batch. No sharing, no locks.
    RecordPosition? _frontier;

    int _started;

    DroneContext DroneContext { get; set; }

    public ChildDrone(
        ProcessorOptions options,
        Partition partition,
        RecordPosition floor,
        Channel<DataSourceMessage> channel,
        ChannelWriter<CommanderEvent> control,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        IProcessor processor
    ) {
        Options    = options;
        _partition = partition;
        _floor     = floor;
        _channel   = channel;
        _control   = control;
        _autoTrack = options.Checkpoint.AutoTrack;

        _processor = processor switch {
            IRecordBatchProcessor bp => new SafeRecordBatchProcessor(bp),
            IRecordProcessor rp      => new SafeRecordProcessor(rp),
            _ => throw new ArgumentException(
                $"Processor must implement {nameof(IRecordProcessor)} or {nameof(IRecordBatchProcessor)}.",
                nameof(processor))
        };

        _progress = new ProgressTracker(timeProvider, options.MaxParallelism);

        var logger = loggerFactory.CreateLogger<ChildDrone>();

        _telemetry = new DroneTelemetry(logger, options.ProcessorId, options.DroneId);

        DroneContext = new DroneContext {
            ProcessorId = ProcessorId,
            DroneId     = DroneId,
            SessionId   = SessionId.Unset,
            Logger      = logger
        };
    }

    public string ProcessorId => Options.ProcessorId;
    public string DroneId     => Options.DroneId;

    public ProcessorOptions Options { get; }

    public DroneStatus Status { get; private set; } = DroneStatus.Starting;

    public DroneProgress Progress => _progress.Progress;

    public int Partition => _partition.Value;

    public async Task RunAsync(CancellationToken stoppingToken) {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            throw new InvalidOperationException("This drone has already been started. Create a new instance to run again.");

        Status = DroneStatus.Starting;

        _telemetry.Starting();

        DroneContext = DroneContext with { CancellationToken = stoppingToken };

        // This child owns one partition; the commander resolved its floor. Prune the caught-up prefix once,
        // then latch off. The Latest / Unset sentinels mean "no prior progress" and never prune.
        var floor    = _floor.LogPosition;
        var hasFloor = floor != LogPosition.Latest && floor != LogPosition.Unset;
        var caughtUp = !hasFloor;
        var stopping = false;

        string?    stopReason = null;
        Exception? fatalError = null;

        try {
            // Single batched read of the channel. The commander routes only records here, but the batched
            // read still classifies the union — anything that isn't a record batch is ignored below.
            var stream = _channel.Reader
                .ReadAllAsync(CancellationToken.None)
                .ReadBatched<DataSourceMessage, Record, BatchedSubscribeMessage>(
                    Options.Batching.MaxBatchSize, Options.Batching.BatchWindow,
                    classify: static msg => msg.Match<BatchAction<Record, BatchedSubscribeMessage>>(
                        static connected  => BatchAction<Record, BatchedSubscribeMessage>.FlushThenYield(connected),
                        static record     => BatchAction<Record, BatchedSubscribeMessage>.Batch(record),
                        static checkpoint => BatchAction<Record, BatchedSubscribeMessage>.FlushThenYield(checkpoint)),
                    batchToOutput: static records => RecordBatch.From(records),
                    CancellationToken.None);

            await foreach (var output in stream.ConfigureAwait(false)) {
                // Once stopped, drain without processing until the commander completes the channel.
                if (stopping || output.Value is not RecordBatch raw)
                    continue;

                var batch = caughtUp ? raw : Prune(raw);
                if (batch.Count == 0)
                    continue;

                var stop = await Dispatch(batch).ConfigureAwait(false);
                Report();

                if (stop) {
                    _control.TryWrite(new ChildStopRequested(_partition.Value, stopReason));
                    stopping = true;
                }
            }
        }
        catch (ChannelClosedException ex) {
            fatalError = ex.InnerException;
        }
        catch (Exception ex) {
            fatalError = ex;
        }

        // The commander owns the commit; the child only surfaces a fault and signals completion.
        if (fatalError is not null)
            _control.TryWrite(new ChildFaulted(_partition.Value, fatalError));

        _control.TryWrite(new ChildCompleted(_partition.Value));

        Shutdown(fatalError);

        // ── local functions ──────────────────────────────────────────────────────────────────────────

        // Drop the caught-up prefix once (records at or below the floor), latching off after the first
        // record clears it. Single partition ⇒ one floor.
        RecordBatch Prune(RecordBatch raw) {
            List<Record>? kept = null;

            for (var i = 0; i < raw.Count; i++) {
                var position = raw[i].Position.LogPosition;

                if (position > floor)
                    caughtUp = true;

                if (hasFloor && position <= floor)
                    kept ??= [..raw.Take(i)];
                else
                    kept?.Add(raw[i]);
            }

            return kept is null ? raw : RecordBatch.From([..kept]);
        }

        // Dispatch through whichever processor shape the user gave us; returns true on a stop request.
        async ValueTask<bool> Dispatch(RecordBatch batch) {
            switch (_processor) {
                case IRecordBatchProcessor bp:
                    return await HandleBatch(bp, batch).ConfigureAwait(false);

                case IRecordProcessor rp:
                    foreach (var record in batch) {
                        if (await HandleRecord(rp, record).ConfigureAwait(false))
                            return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        async ValueTask<bool> HandleRecord(IRecordProcessor processor, Record record) {
            using var scope = _telemetry.TrackRecord();

            var ctx = DroneContext.ForProcessRecord(Progress, record, requestTrack: Track);

            await processor.ProcessRecord(ctx).ConfigureAwait(false);

            _progress.CountThroughput(record);

            if (_autoTrack && !ctx.StopRequested)
                Track(record.Position);

            if (ctx.CheckpointRequested && !ctx.StopRequested)
                _control.TryWrite(new ChildCheckpointRequested(_partition.Value, ctx.CheckpointReason));

            scope.Complete(_progress.TotalRecords, _progress.UncommittedRecords, _progress.UncommittedBytes);

            if (!ctx.StopRequested)
                return false;

            stopReason = ctx.StopReason;
            return true;
        }

        async ValueTask<bool> HandleBatch(IRecordBatchProcessor processor, RecordBatch batch) {
            using var scope = _telemetry.TrackBatch(batch.Count);

            var ctx = DroneContext.ForBatch(Progress, batch, requestTrack: Track);

            await processor.ProcessBatch(ctx).ConfigureAwait(false);

            // Single partition ⇒ the batch frontier is just its last record's position.
            if (_autoTrack && !ctx.StopRequested && batch.Count > 0)
                Track(batch[batch.Count - 1].Position);

            if (batch.Count > 0)
                _progress.CountThroughput(batch.Count, batch[batch.Count - 1].Timestamp);

            scope.Complete(batch.Count, _progress.TotalRecords, _progress.UncommittedRecords, _progress.UncommittedBytes);

            if (ctx.CheckpointRequested && !ctx.StopRequested)
                _control.TryWrite(new ChildCheckpointRequested(_partition.Value, ctx.CheckpointReason));

            if (!ctx.StopRequested)
                return false;

            stopReason = ctx.StopReason;
            return true;
        }

        // The transparent tracking primitive — advance the local frontier (monotonic) AND progress together.
        void Track(RecordPosition position) {
            if (_frontier is not { } current || position > current)
                _frontier = position;

            _progress.TrackPosition(position);
        }

        // Report this partition's frontier; the commander tracks it into the one checkpointer.
        void Report() {
            if (_frontier is { } frontier)
                _control.TryWrite(new ChildProgress(_partition.Value, frontier));
        }
    }

    void Shutdown(Exception? fatalError) {
        var throughput = Progress.Throughput;

        _telemetry.Stopping(throughput.TotalRecords, throughput.Elapsed, throughput.RecordsPerSecond);

        if (fatalError is not null) {
            Status = DroneStatus.Error;
            _telemetry.Error(fatalError);
        }
        else {
            Status = DroneStatus.Stopped;
        }

        _telemetry.Stopped(throughput.TotalRecords, throughput.Elapsed);
    }
}
