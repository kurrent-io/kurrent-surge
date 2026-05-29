using System.Threading.Channels;
using Kurrent.Client;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Checkpointing;
using Kurrent.Streaming.Diagnostics;
using Kurrent.Streaming.Options;

namespace Kurrent.Streaming;

using Gateway_ISubscription = Kurrent.Streaming.Gateway.ISubscription;

sealed class Drone : IDrone {
    readonly Gateway_ISubscription      _subscription;
    readonly IProcessor         _processor;
    readonly Checkpointer       _checkpointer;
    readonly StreamsClient      _streams;
    readonly DroneTelemetry     _telemetry;
    readonly ProgressTracker    _progress;
    readonly HybridLogicalClock _clock;

    readonly Channel<SubscribeMessage> _channel;

    int _started;

    DroneContext DroneContext { get; set; }

    public Drone(
        ProcessorOptions options,
        Gateway_ISubscription subscription,
        Checkpointer checkpointer,
        Channel<SubscribeMessage> channel,
        StreamsClient streams,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        IProcessor processor
    ) {
        Options = options;

        _subscription = subscription;
        _checkpointer = checkpointer;
        _channel      = channel;
        _streams      = streams;
        _clock        = new HybridLogicalClock(timeProvider);

        _processor = processor switch {
            IRecordBatchProcessor bp => new SafeRecordBatchProcessor(bp),
            IRecordProcessor rp      => new SafeRecordProcessor(rp),
            _ => throw new ArgumentException(
                $"Processor must implement {nameof(IRecordProcessor)} or {nameof(IRecordBatchProcessor)}.",
                nameof(processor))
        };

        _progress = new ProgressTracker(timeProvider);

        var logger = loggerFactory.CreateLogger<Drone>();

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

    public async Task RunAsync(CancellationToken stoppingToken) {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            throw new InvalidOperationException("This drone has already been started. Create a new instance to run again.");

        Status = DroneStatus.Starting;

        _telemetry.Starting();

        DroneContext = DroneContext with { CancellationToken = stoppingToken };

        var partitions = Enumerable
            .Range(0, Options.MaxParallelism)
            .Select(Partition.From)
            .ToArray();

        // Resolve explicit start position if provided (stream info may be missing)
        var resetTo = RecordPosition.Unset;

        if (Options.Subscription.StartPosition is { } explicitStart) {
            if (explicitStart.Stream.IsUnset) {
                var record = await _streams
                    .InspectRecord(explicitStart.LogPosition, stoppingToken)
                    .ConfigureAwait(false);

                explicitStart = RecordPosition.From(record.Position.LogPosition, record.Stream, record.Revision);
            }

            resetTo = explicitStart;
        }

        // Load checkpoint (resetting first if explicit start was provided), start timer
        var startPositions = await _checkpointer
            .Initialize(partitions, Options.Subscription.InitialPosition, resetTo, stoppingToken)
            .ConfigureAwait(false);

        // Catch-up filter: skip records at or below per-partition checkpoint
        var catchUp = startPositions.ToDictionary(p => p.Partition, p => p.LogPosition);

        var lowWatermark = startPositions.Length > 0
            ? startPositions.Min()
            : RecordPosition.Earliest;

        // Subscribe writes to channel, completes it when done. Never throws.
        _ = _subscription.Subscribe(
            lowWatermark, Options.Subscription.Filter,
            Options.SkipDecoding, stoppingToken);

        Exception? fatalError = null;

        try {
            switch (_processor) {
                case IRecordBatchProcessor batchProcessor: 
                    await RunBatched(batchProcessor, catchUp).ConfigureAwait(false); break;

                case IRecordProcessor recordProcessor: 
                    await RunPerRecord(recordProcessor, catchUp).ConfigureAwait(false); break;
            }
        }
        catch (ChannelClosedException ex) {
            fatalError = ex.InnerException;
        }
        catch (Exception ex) {
            fatalError = ex;
        }

        // Channel closed — subscription ended or user stopped.
        // If user stopped, the Shutdown barrier already committed pending positions.
        // If subscription ended naturally, pending positions may remain.
        // Skip if we already have a fatal error — the checkpoint hook would fail again.
        if (fatalError is null && _checkpointer.HasPendingPositions) {
            var now = _clock.Now();
            Checkpoint checkpoint = new ShutdownCheckpoint(new CheckpointId(now), now.DateTimeOffset);

            await HandleCheckpoint(checkpoint).ConfigureAwait(false);
        }

        await Shutdown(fatalError).ConfigureAwait(false);
    }

    async Task RunPerRecord(IRecordProcessor processor, Dictionary<Partition, LogPosition> catchUp) {
        var stopping = false;

        await foreach (var msg in _channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false)) {
            if (stopping && msg.Value is Record)
                continue;

            switch (msg.Value) {
                case Connected c: await HandleConnected(c).ConfigureAwait(false); break;

                case Record record: {
                    if (catchUp.TryGetValue(record.Position.Partition, out var cp)
                     && record.Position.LogPosition <= cp)
                        continue;

                    var result = await HandleRecord(processor, record).ConfigureAwait(false);

                    if (result.ShouldStop && !stopping) {
                        stopping = true;

                        await _checkpointer
                            .RaiseCheckpoint(CheckpointTrigger.Shutdown, result.Reason)
                            .ConfigureAwait(false);
                    }

                    break;
                }

                case Checkpoint checkpoint: await HandleCheckpoint(checkpoint).ConfigureAwait(false); break;
            }
        }
        
        async ValueTask<RecordProcessingResult> HandleRecord(IRecordProcessor processor, Record record) {
            using var scope = _telemetry.TrackRecord();

            var autoCheckpoint = Options.Checkpoint.AutoCheckpoint;

            var ctx = DroneContext.ForProcessRecord(
                Progress, record,
                requestTrack: _checkpointer.Track);

            await processor
                .ProcessRecord(ctx)
                .ConfigureAwait(false);

            if (autoCheckpoint && !ctx.StopRequested) {
                _checkpointer.Track(record.Position);
                _progress.TrackRecord(record.Position, record.Position.Partition);
            }

            if (ctx.CheckpointRequested)
                await _checkpointer
                    .RaiseCheckpoint(CheckpointTrigger.Manual, ctx.CheckpointReason)
                    .ConfigureAwait(false);

            scope.Complete(Progress.Throughput.TotalRecords);

            return ctx.StopRequested
                ? RecordProcessingResult.Stop(ctx.StopReason)
                : RecordProcessingResult.Continue;
        }
    }

    async Task RunBatched(IRecordBatchProcessor processor, Dictionary<Partition, LogPosition> catchUp) {
        var stream = _channel.Reader
            .ReadAllAsync(CancellationToken.None)
            .ReadBatched<SubscribeMessage, Record, BatchedSubscribeMessage>(
                processor.MaxBatchSize, processor.BatchWindow,
                classify: static msg => msg.Match<BatchAction<Record, BatchedSubscribeMessage>>(
                    static connected  => BatchAction<Record, BatchedSubscribeMessage>.FlushThenYield(connected),
                    static record     => BatchAction<Record, BatchedSubscribeMessage>.Batch(record),
                    static checkpoint => BatchAction<Record, BatchedSubscribeMessage>.FlushThenYield(checkpoint)),
                batchToOutput: static records => RecordBatch.From(records),
                CancellationToken.None);

        var stopping = false;

        await foreach (var output in stream.ConfigureAwait(false)) {
            
            // If stopping, skip any remaining batches but continue to drain checkpoint messages
            // to ensure we can checkpoint final positions and shut down cleanly.
            if (stopping && output.Value is RecordBatch)
                continue;

            switch (output.Value) {
                case Connected c: await HandleConnected(c).ConfigureAwait(false); break;

                case RecordBatch batch: {
                    var result = await HandleBatch(processor, batch, catchUp).ConfigureAwait(false);

                    if (result.ShouldStop && !stopping) {
                        stopping = true;

                        await _checkpointer
                            .RaiseCheckpoint(CheckpointTrigger.Shutdown, result.Reason)
                            .ConfigureAwait(false);
                    }

                    break;
                }

                case Checkpoint checkpoint: await HandleCheckpoint(checkpoint).ConfigureAwait(false); break;
            }
        }
        
        async ValueTask<RecordProcessingResult> HandleBatch(
            IRecordBatchProcessor processor,
            RecordBatch batch,
            Dictionary<Partition, LogPosition> catchUp
        ) {
            var ctx = DroneContext.ForBatch(Progress, batch);

            await processor
                .ProcessBatch(ctx)
                .ConfigureAwait(false);

            foreach (var record in batch) {
                // Catch-up filter: skip records already processed
                if (catchUp.TryGetValue(record.Position.Partition, out var cp)
                 && record.Position.LogPosition <= cp)
                    continue;

                _checkpointer.Track(record.Position);

                using var scope = _telemetry.TrackRecord();
                _progress.TrackRecord(record.Position, record.Position.Partition);
                scope.Complete(Progress.Throughput.TotalRecords);
            }

            return ctx.StopRequested
                ? RecordProcessingResult.Stop(ctx.StopReason)
                : RecordProcessingResult.Continue;
        }
    }

    #region ->> Message Handlers <<-

    async ValueTask<RecordProcessingResult> HandleConnected(Connected connected) {
        DroneContext = DroneContext with { SessionId = connected.SessionId };
        Status       = DroneStatus.Processing;
        
        _telemetry.Processing(connected.SessionId);
        
        await _processor
            .OnConnected(DroneContext.ForConnected())
            .ConfigureAwait(false);
        
        return RecordProcessingResult.Continue;
    }
    
    async ValueTask HandleCheckpoint(Checkpoint checkpoint) {
        var trigger = checkpoint.TriggerKind;

        _telemetry.CheckpointInitiated(checkpoint.Id, trigger);

        var positions = _checkpointer.PendingPositions();

        if (positions.Length == 0)
            return;

        var checkpointToken = CancellationToken.None;

        var ctx = DroneContext.ForCheckpoint(positions, checkpoint) with { CancellationToken = checkpointToken };

        try {
            await _processor.OnCheckpoint(ctx).ConfigureAwait(false);
        }
        catch (Exception ex) {
            _telemetry.CheckpointFailed(checkpoint.Id, trigger, positions, ex);

            if (Options.Debug)
                throw;

            if (Options.Checkpoint.FailurePolicy == CheckpointFailurePolicy.Fail)
                throw;

            return;
        }

        try {
            await _checkpointer
                .CommitCheckpoint(checkpoint.Id, positions, checkpointToken)
                .ConfigureAwait(false);

            _progress.TrackCheckpoint(checkpoint, positions);
            _telemetry.CheckpointComplete(checkpoint.Id, trigger, positions);
        }
        catch (Exception ex) {
            _telemetry.CheckpointFailed(checkpoint.Id, trigger, positions, ex);

            if (Options.Checkpoint.FailurePolicy == CheckpointFailurePolicy.Fail)
                throw;
        }
    }

    #endregion

    #region ->> Shutdown <<-

    async ValueTask Shutdown(Exception? fatalError = null) {
        var progress   = Progress;
        var throughput = progress.Throughput;

        _telemetry.Stopping(throughput.TotalRecords, throughput.Elapsed, throughput.RecordsPerSecond);

        List<Exception> errors = [];

        if (fatalError is not null)
            errors.Add(fatalError);

        try {
            await _checkpointer.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            errors.Add(ex);
        }

        try {
            await _subscription.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            errors.Add(ex);
        }

        if (errors.Count > 0) {
            Status = DroneStatus.Error;

            var aex = new AggregateException(
                fatalError is not null
                    ? "Fatal error in processor drone"
                    : "Errors during graceful shutdown",
                errors).Flatten();

            _telemetry.Error(aex);

            if (fatalError is not null)
                throw aex;
        }
        else {
            Status = DroneStatus.Stopped;
        }

        _telemetry.Stopped(throughput.TotalRecords, throughput.Elapsed);
    }

    #endregion
}

/// <summary>
/// Outcome of dispatching a single channel message to the drone's handlers.
/// </summary>
readonly record struct RecordProcessingResult(bool ShouldStop, string? Reason) {
    public static readonly RecordProcessingResult Continue = new(false, null);

    public static RecordProcessingResult Stop(string? reason) =>
        new(true, reason);
}