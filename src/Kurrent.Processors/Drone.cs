using System.Collections.Immutable;
using System.Threading.Channels;
using Kurrent.Client;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Client.Processors.Partitioning;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Diagnostics;
using Kurrent.Processors.Gateway;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;

namespace Kurrent.Processors;

sealed class Drone : IProcessorWorker {
    readonly ISubscriptionSource        _source;
    readonly ICheckpointStore           _checkpointStore;
    readonly IRecordProcessor           _processor;
    readonly WorkerTelemetry            _telemetry;
    readonly PartitionedPositionTracker _tracker;
    readonly ProgressTracker            _progress;
    readonly ILogger                    _logger;
    readonly IKurrentClient             _client;
    readonly bool                       _ownsClient;
    readonly Channel<SubscribeMessage>  _channel;
    readonly HybridLogicalClock         _clock;

    int                         _started;

    WorkerContext WorkerContext { get; set; }

    public Drone(
        ProcessorSettings settings,
        ISubscriptionSource source,
        ICheckpointStore checkpointStore,
        IRecordProcessor processor,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        IKurrentClient client,
        bool ownsClient = false
    ) {
        Settings         = settings;
        _source          = source;
        _checkpointStore = checkpointStore;
        _processor       = new SafeRecordProcessor(processor);
        _client          = client;
        _ownsClient      = ownsClient;

        _clock    = new HybridLogicalClock(timeProvider);
        _progress = new ProgressTracker(timeProvider);
        _tracker  = new PartitionedPositionTracker();

        _logger = loggerFactory.CreateLogger<Drone>();

        _telemetry = new WorkerTelemetry(_logger, settings.ProcessorId, settings.WorkerId);

        WorkerContext = new WorkerContext {
            ProcessorId = ProcessorId,
            WorkerId    = WorkerId,
            SessionId   = SessionId.Unset,
            Logger      = _logger
        };

        //_loadedCheckpoint = ProcessorCheckpoint.Empty;

        _channel = Channel.CreateBounded<SubscribeMessage>(
            new BoundedChannelOptions(Settings.Worker.BufferCapacity) {
                FullMode     = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
    }

    public string ProcessorId => Settings.ProcessorId;
    public string WorkerId    => Settings.WorkerId;

    public ProcessorSettings Settings { get; }

    public ProcessorWorkerStatus Status { get; private set; } = ProcessorWorkerStatus.Starting;

    public WorkerProgress Progress => _progress.Progress;

    public async Task RunAsync(CancellationToken stoppingToken) {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            throw new InvalidOperationException("This drone has already been started. Create a new instance to run again.");

        Status = ProcessorWorkerStatus.Starting;

        _telemetry.Starting();

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        WorkerContext = WorkerContext with { CancellationToken = stoppingToken };

        var partitions = Enumerable.Range(0, Settings.MaxParallelism).Select(Partition.From).ToArray();

        if (Settings.Subscription.StartPosition is not { } startPosition) {
            var checkpoint = await _checkpointStore
                .LoadLatestAsync(partitions, Settings.Subscription.InitialPosition, cancellator.Token)
                .ConfigureAwait(false);

            startPosition = checkpoint.LowWatermark;

            // var positions = checkpoint.Positions.Length > 0
            //     ? checkpoint.Positions.ToImmutableDictionary(p => p.Partition, p => p.Position)
            //     : ImmutableDictionary<Partition, RecordPosition>.Empty.Add(Partition.From(0), _startPosition);

            var positions = checkpoint.Positions.ToImmutableDictionary(p => p.Partition, p => p.Position);
            _tracker.Reset(positions);
        } else {
            if (startPosition.Stream.IsUnset) {
                var record = await _client.Streams
                    .InspectRecord(startPosition.LogPosition, cancellator.Token)
                    .ConfigureAwait(false);

                startPosition = RecordPosition.From(
                    record.Position.LogPosition,
                    record.Stream,
                    record.Revision);
            }

            await _checkpointStore
                .ResetAsync(
                    WorkerId,
                    CheckpointId.From(_clock.Now()),
                    startPosition,
                    cancellator.Token
                )
                .ConfigureAwait(false);

            startPosition = Settings.Subscription.InitialPosition == SubscriptionInitialPosition.Earliest
                ? RecordPosition.Earliest
                : RecordPosition.Latest;

            var positions = ImmutableDictionary<Partition, RecordPosition>.Empty.Add(Partition.From(0), startPosition);

            _tracker.Reset(positions);
        }

        _progress.SetPartitionTracker(_tracker);

        // _telemetry.RestoringState(positions.Count);
        //
        // _tracker.Reset(positions);
        // _progress.SetPartitionTracker(_tracker);
        //
        // var lowWatermark = positions.Count > 0 ? positions.Values.Min() : RecordPosition.Earliest;
        //
        // _telemetry.StateRestored(lowWatermark.LogPosition, positions.Count);


        // var checkpoint = await _checkpointStore
        //     .LoadLatestAsync(partitions, Settings.Subscription.InitialPosition, cancellator.Token)
        //     .ConfigureAwait(false);
        //
        // // Load checkpoint and resolve start position
        // _loadedCheckpoint = await _checkpointStore.LoadLatestAsync(cancellator.Token).ConfigureAwait(false);
        // _startPosition    = ResolveStartPosition(_loadedCheckpoint);

        StartSubscriptionPump(startPosition, cancellator.Token);

        try {
            await foreach (var msg in _channel.Reader.ReadAllAsync(cancellator.Token).ConfigureAwait(false)) {
                await msg
                    .SwitchAsync(
                        HandleConnected,
                        HandleRecord,
                        HandleCheckpoint
                    )
                    .ConfigureAwait(false);
            }

            await HandleStopped(cancellator).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) {
            await HandleStopped(cancellator, ex).ConfigureAwait(false);
        }
        catch (Exception ex) {
            await HandleError(cancellator, ex).ConfigureAwait(false);
        }

        void StartSubscriptionPump(RecordPosition from, CancellationToken ct) {
            _ = Task.Run(async () => {
                try {
                    var subscription = _source.Subscribe(
                        Settings.Subscription.Filter, from,
                        Settings.Worker.SkipDecoding, ct);

                    // var sub = _client.Streams.Subscribe(
                    //     Settings.Subscription.Filter, from,
                    //     Settings.Worker.SkipDecoding, ct);

                    await foreach (var msg in subscription.ConfigureAwait(false))
                        await _channel.Writer.WriteAsync(msg, ct).ConfigureAwait(false);

                    _channel.Writer.TryComplete();
                }
                catch (OperationCanceledException) {
                    _channel.Writer.TryComplete();
                }
                catch (Exception ex) {
                    _channel.Writer.TryComplete(ex);
                }
            }, ct);
        }

        // RecordPosition ResolveStartPosition(ProcessorCheckpoint checkpoint) {
        //     if (Settings.Subscription.StartPosition is { } startPos)
        //         return startPos;
        //
        //     if (checkpoint.Positions.Length > 0 && !checkpoint.LowWatermark.IsUnset)
        //         return checkpoint.LowWatermark;
        //
        //     return Settings.Subscription.InitialPosition == SubscriptionInitialPosition.Earliest
        //         ? RecordPosition.Earliest
        //         : RecordPosition.Latest;
        // }
    }

    #region ->> Message Handlers <<-

    ValueTask HandleConnected(Connected connected) {
        WorkerContext = WorkerContext with { SessionId = connected.SessionId };
        StartFlushTimer(WorkerContext.CancellationToken);
        Status = ProcessorWorkerStatus.Processing;
        _telemetry.Processing(connected.SessionId);
        return ValueTask.CompletedTask;

        void StartFlushTimer(CancellationToken ct) {
            _ = Task.Run(async () => {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Settings.Checkpoint.Interval));

                try {
                    while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false)) {
                        await _channel.Writer
                            .WriteAsync(CreateCheckpointEvent(CheckpointTrigger.Interval), ct)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) {
                    // Expected on shutdown
                }
            }, ct);
        }
    }

    // ValueTask HandleConnected(Connected connected) {
    //     // Ensure at least partition 0 exists — on first run with no checkpoint,
    //     // the start position seeds the tracker so Track(record) doesn't fail.
    //     var positions = _loadedCheckpoint.Positions.Length > 0
    //         ? _loadedCheckpoint.Positions.ToImmutableDictionary(p => p.Partition, p => p.Position)
    //         : ImmutableDictionary<Partition, RecordPosition>.Empty.Add(Partition.From(0), _startPosition);
    //
    //     _telemetry.RestoringState(positions.Count);
    //
    //     _tracker.Reset(positions);
    //     _progress.SetPartitionTracker(_tracker);
    //
    //     var lowWatermark = positions.Count > 0 ? positions.Values.Min() : RecordPosition.Earliest;
    //
    //     _telemetry.StateRestored(lowWatermark.LogPosition, positions.Count);
    //
    //     WorkerContext = WorkerContext with { SessionId = connected.SessionId };
    //
    //     StartFlushTimer(WorkerContext.CancellationToken);
    //
    //     Status = ProcessorWorkerStatus.Processing;
    //     _telemetry.Processing(connected.SessionId);
    //
    //     return ValueTask.CompletedTask;
    // }

    async ValueTask HandleRecord(Record record) {
        using var scope = _telemetry.TrackRecord();

        var partition = PartitionCalculator.XXHash
            .Calculate(record.Stream, Settings.MaxParallelism);

        record = record with { Position = record.Position with { Partition = partition } };

        var ctx = WorkerContext.ForProcessRecord(Progress, record);

        await _processor
            .ProcessRecord(ctx)
            .ConfigureAwait(false);

        if (ctx.StopProcessingRequest.Trigger == StopProcessingTrigger.StopBeforeTrack) {
            var cex = new OperationCanceledException(
                $"Before tracking record by user request. Reason: {ctx.StopProcessingRequest.Reason}");

            scope.Fail(cex);
            throw cex;
        }

        _tracker.Track(record);
        _progress.TrackRecord();

        if (ctx.StopProcessingRequest.Trigger == StopProcessingTrigger.StopAfterTrack) {
            var cex = new OperationCanceledException(
                $"After tracking record by user request. Reason: {ctx.StopProcessingRequest.Reason}");

            scope.Fail(cex);
            throw cex;
        }

        scope.Complete(Progress.Throughput.TotalRecords);
    }

    async ValueTask HandleCheckpoint(Checkpoint checkpoint) {
        _telemetry.CheckpointInitiated(checkpoint.CheckpointId, checkpoint.Trigger);

        var pendingFlush = _tracker.PrepareFlush();

        PartitionPositions positions = pendingFlush.Positions
            .Where(x => x.Value.LogPosition != LogPosition.Earliest)
            .ToImmutableDictionary();

        try {
            var ctx = WorkerContext.ForCheckpoint(positions, checkpoint);

            try {
                await _processor.OnCheckpoint(ctx).ConfigureAwait(false);
            }
            catch (Exception ex) when (!Settings.Debug) {
                _logger.LogWarning(ex, "[{ProcessorId}/{WorkerId}] User OnCheckpoint handler failed", ProcessorId, WorkerId);
            }

            await PersistCheckpointAsync(checkpoint.CheckpointId, positions, CancellationToken.None)
                .ConfigureAwait(false);

            pendingFlush.Execute();

            _progress.TrackCheckpoint(checkpoint);
            _telemetry.CheckpointComplete(checkpoint.CheckpointId, checkpoint.Trigger, positions);
        }
        catch (Exception ex) {
            _telemetry.CheckpointFailed(checkpoint.CheckpointId, checkpoint.Trigger, positions, ex);

            if (Settings.Checkpoint.FailurePolicy == CheckpointFailurePolicy.FailProcessor)
                throw;
        }
    }

    #endregion

    #region ->> Stop Flows <<-

    async ValueTask HandleStopped(CancellationTokenSource cts, OperationCanceledException? cex = null) {
        await cts.CancelAsync().ConfigureAwait(false);

        var progress   = Progress;
        var throughput = progress.Throughput;

        _telemetry.Stopping(throughput.TotalRecords, throughput.Elapsed, throughput.RecordsPerSecond);

        List<Exception> errors = [];

        var checkpointFailed = false;

        #region ->> Checkpoint <<-

        if (Status == ProcessorWorkerStatus.Processing) {
            var checkpoint = CreateCheckpointEvent(CheckpointTrigger.Shutdown);

            _telemetry.CheckpointInitiated(checkpoint.CheckpointId, checkpoint.Trigger);

            var pendingFlush = _tracker.PrepareFlush();

            PartitionPositions positions = pendingFlush.Positions
                .Where(x => x.Value.LogPosition != LogPosition.Earliest)
                .ToImmutableDictionary();

            // User OnCheckpoint — best-effort in production, throws in Debug
            try {
                var ctx = WorkerContext.ForCheckpoint(positions, checkpoint);
                await _processor.OnCheckpoint(ctx).ConfigureAwait(false);
            }
            catch (Exception ex) when (!Settings.Debug) {
                _logger.LogWarning(ex, "[{ProcessorId}/{WorkerId}] User OnCheckpoint handler failed during shutdown", ProcessorId, WorkerId);
            }

            // Persist — best-effort, independent of user handler
            try {
                await PersistCheckpointAsync(checkpoint.CheckpointId, positions, CancellationToken.None)
                    .ConfigureAwait(false);

                pendingFlush.Execute();
                _progress.TrackCheckpoint(checkpoint);
                _telemetry.CheckpointComplete(checkpoint.CheckpointId, checkpoint.Trigger, positions);
            }
            catch (Exception ex) {
                checkpointFailed = true;
                _telemetry.CheckpointFailed(checkpoint.CheckpointId, checkpoint.Trigger, positions, ex);
                errors.Add(ex);
            }
        }

        #endregion

        // User OnComplete — best-effort in production, throws in Debug
        try {
            await _processor
                .OnComplete(WorkerContext.ForComplete(Progress))
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!Settings.Debug) {
            errors.Add(ex);
        }

        await Cleanup(errors).ConfigureAwait(false);

        // Final status — checkpoint policy can escalate to error
        if (checkpointFailed && Settings.Checkpoint.FailurePolicy == CheckpointFailurePolicy.FailProcessor) {
            Status = ProcessorWorkerStatus.Error;

            var aex = new AggregateException("Checkpoint failed during shutdown", errors).Flatten();
            _telemetry.Error(aex);
            throw aex;
        }

        if (errors.Count > 0) {
            Status = ProcessorWorkerStatus.Error;

            var aex = new AggregateException("Errors during graceful shutdown", errors).Flatten();
            _telemetry.Error(aex);
        }
        else {
            Status = ProcessorWorkerStatus.Stopped;
        }

        _telemetry.Stopped(throughput.TotalRecords, throughput.Elapsed, cex?.Message);
    }

    async ValueTask HandleError(CancellationTokenSource cancellator, Exception ex) {
        await cancellator.CancelAsync().ConfigureAwait(false);

        List<Exception> errors = [ex];

        var progress   = Progress;
        var throughput = progress.Throughput;

        _telemetry.Stopping(throughput.TotalRecords, throughput.Elapsed, throughput.RecordsPerSecond);

        // User OnError — always guarded, even in Debug mode.
        // If the error handler itself fails, both exceptions surface in the aggregate.
        try {
            await _processor
                .OnError(WorkerContext.ForError(progress, ex))
                .ConfigureAwait(false);
        }
        catch (Exception onErrorEx) {
            errors.Add(onErrorEx);
        }

        await Cleanup(errors).ConfigureAwait(false);

        Status = ProcessorWorkerStatus.Error;

        var aex = new AggregateException("Fatal error in processor drone", errors).Flatten();
        _telemetry.Error(aex);
        throw aex;
    }

    #endregion

    #region ->> Internals <<-

    async ValueTask PersistCheckpointAsync(string checkpointId, PartitionPositions positions, CancellationToken ct) {
        if (positions.Count == 0) return;

        var partitionPositions = positions
            .Select(kvp => new PartitionPosition(kvp.Key, kvp.Value))
            .ToArray();

        await _checkpointStore
            .StoreAsync(WorkerId, checkpointId, partitionPositions, ct)
            .ConfigureAwait(false);
    }

    Checkpoint CreateCheckpointEvent(CheckpointTrigger trigger) {
        var now = _clock.Now();

        return new(
            CheckpointId.From(now),
            trigger,
            now.DateTimeOffset
        );
    }

    async ValueTask Cleanup(List<Exception> errors) {
        try {
            await _source.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            errors.Add(new Exception("Error disposing subscription source", ex));
        }

        try {
            _tracker.Dispose();
        }
        catch (Exception ex) {
            errors.Add(new Exception("Error disposing position tracker", ex));
        }

        try {
            if (_ownsClient)
                await _client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            errors.Add(new Exception("Error disposing client", ex));
        }
    }

    #endregion
}
