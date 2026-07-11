using System.Threading.Channels;
using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Configuration;
using Kurrent.Surge.Streaming.Gateway;

namespace Kurrent.Surge.Streaming;

/// <summary>
/// Bare drone for studying the data source ↔ checkpointer responsibility split. The ONLY things removed
/// vs <c>Drone</c> are <b>telemetry</b> (DroneTelemetry, scopes, source-lag probe, progress gauges) and
/// <b>batching</b> (<see cref="IRecordBatchProcessor"/>, RecordBatch / ReadBatched / per-partition prune).
/// Core logic is kept: the safe-processor wrapper, the connected / record / checkpoint handlers, and the
/// shutdown path. NOT integrated — nothing constructs it.
/// </summary>
sealed class DroneV0 {
    readonly ProcessorOptions          _options;
    readonly IDataSource               _dataSource;
    readonly Checkpointer              _checkpointer;
    readonly Channel<DataSourceMessage> _channel;
    readonly IRecordProcessor          _processor;
    readonly TimeProvider              _timeProvider;

    DroneContext DroneContext { get; set; }

    public DroneV0(
        ProcessorOptions options,
        IDataSource dataSource,
        Checkpointer checkpointer,
        Channel<DataSourceMessage> channel,
        IRecordProcessor processor,
        TimeProvider timeProvider
    ) {
        _options      = options;
        _dataSource   = dataSource;
        _checkpointer = checkpointer;
        _channel      = channel;
        _timeProvider = timeProvider;

        // Safe wrapper: translate a throwing processor into a DroneException instead of letting a raw
        // user exception fault the loop. Core error handling, not telemetry.
        _processor = new SafeRecordProcessor(processor);

        DroneContext = new DroneContext {
            ProcessorId = _options.ProcessorId,
            DroneId     = _options.DroneId,
            SessionId   = SessionId.Unset
        };
    }

    public async Task RunAsync(CancellationToken stoppingToken) {
        var partitions = Enumerable
            .Range(0, _options.MaxParallelism)
            .Select(Partition.From)
            .ToArray();

        DroneContext = DroneContext with { CancellationToken = stoppingToken };

        // ── STARTUP ──
        //   checkpointer : resolves the start — load committed, fold the initial-position fallback across owned
        //                  partitions, reset the store on an explicit override, seed the tracker.
        //   data source  : feed-only — subscribes from the low watermark and prunes the caught-up prefix.
        var startPositions = await _checkpointer
            .Initialize(partitions, _options.Subscription.InitialPosition, _options.Subscription.StartPosition, stoppingToken)
            .ConfigureAwait(false);

        // The source feeds records + barriers into the channel (resilient subscribe, partition-tagged, already
        // pruned of the catch-up prefix). Fire-and-forget; it completes the channel when the subscription ends —
        // faulting it with an exception if it failed.
        _ = _dataSource.Connect(_channel.Writer, startPositions, stoppingToken);

        Exception? fatalError = null;

        // ── PROCESS — one record at a time; commit on every barrier ──
        try {
            await foreach (var message in _channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false)) {
                // One dispatch per message: the switch selects the handler, awaiting it runs it once.
                var handler = message.Value switch {
                    Connected connected   => HandleConnected(connected),
                    Record record         => HandleRecord(record),
                    Checkpoint checkpoint => HandleCheckpoint(checkpoint, partitions),
                    _                     => ValueTask.CompletedTask
                };

                await handler.ConfigureAwait(false);
            }
        }
        catch (ChannelClosedException ex) {
            // The source faults by completing the channel with an exception; ReadAllAsync rethrows it here.
            fatalError = ex.InnerException;
        }
        catch (Exception ex) {
            fatalError = ex;
        }

        await Shutdown(fatalError, partitions).ConfigureAwait(false);
    }

    async ValueTask HandleConnected(Connected connected) {
        DroneContext = DroneContext with { SessionId = connected.SessionId };
        await _processor.OnConnected(DroneContext.ForConnected()).ConfigureAwait(false);
    }

    async ValueTask HandleRecord(Record record) {
        var ctx = DroneContext.ForProcessRecord(DroneProgress.Empty, record, _checkpointer.Track);
        await _processor.ProcessRecord(ctx).ConfigureAwait(false);

        // AutoTrack claims the record unless the user tracked it themselves or asked to stop.
        if (_options.Checkpoint.AutoTrack && !ctx.StopRequested)
            _checkpointer.Track(record.Position);

        // Stop / manual checkpoint are raised as barriers into the SAME channel, so they flow back through the
        // loop and the one HandleCheckpoint path — never committed inline. Shutdown also completes the channel,
        // so the loop drains and exits.
        if (ctx.StopRequested)
            await _checkpointer.RaiseCheckpoint(CheckpointTrigger.Shutdown, ctx.StopReason).ConfigureAwait(false);
        else if (ctx.CheckpointRequested)
            await _checkpointer.RaiseCheckpoint(CheckpointTrigger.Manual, ctx.CheckpointReason).ConfigureAwait(false);
    }

    // The barrier path, reduced to essentials. A server scan watermark (Filter / CaughtUp) carries a log
    // position only; folding it across owned partitions is the CHECKPOINTER's job (AdvanceWatermark), gated by
    // AutoTrack — the one bit of "position logic" that lives on the checkpointer rather than the source. Then
    // OnCheckpoint runs (the user may track more, governed by the failure policy), and pending positions commit.
    async ValueTask HandleCheckpoint(Checkpoint checkpoint, Partition[] partitions) {
        var scanPosition = checkpoint is FilterCheckpoint or CaughtUpCheckpoint && checkpoint.Positions is [.., var watermark]
            ? watermark
            : (RecordPosition?)null;

        if (_options.Checkpoint.AutoTrack && scanPosition is { } w)
            _checkpointer.AdvanceWatermark(w.LogPosition, partitions);

        var ctx = DroneContext.ForCheckpoint(
            checkpoint, _checkpointer.PendingPositions(), scanPosition,
            requestTrack: pos => _checkpointer.AdvanceWatermark(pos.LogPosition, partitions)
        ) with { CancellationToken = CancellationToken.None };

        try {
            await _processor.OnCheckpoint(ctx).ConfigureAwait(false);
        }
        catch (Exception) {
            // A checkpoint-handler failure crashes the processor under Debug or the Fail policy; otherwise it
            // is tolerated and this barrier commits nothing (a later barrier retries).
            if (_options.Debug || _options.Checkpoint.FailurePolicy == CheckpointFailurePolicy.Fail)
                throw;
            return;
        }

        var positions = _checkpointer.PendingPositions();
        if (positions.Length > 0)
            await _checkpointer.CommitCheckpoint(checkpoint.Id, positions, CancellationToken.None).ConfigureAwait(false);
    }

    // Flush the tracked frontier, then release the checkpointer and surface any fatal error.
    async ValueTask Shutdown(Exception? fatalError, Partition[] partitions) {
        // Flush on a clean end OR a ProcessRecord failure — a processing failure must still commit what the
        // user durably tracked before rethrowing, rather than replay it. An OnCheckpoint failure is excluded
        // (code ON_CHECKPOINT_FAILED): re-running OnCheckpoint via this flush would just throw again.
        var isProcessFailure = fatalError is DroneException { ErrorDetails.ErrorCode.Value: "PROCESS_RECORD_FAILED" };

        if ((fatalError is null || isProcessFailure) && _checkpointer.HasPendingPositions)
            await HandleCheckpoint(new ShutdownCheckpoint(CheckpointId.New(), _timeProvider.GetUtcNow()), partitions).ConfigureAwait(false);

        List<Exception> errors = [];

        if (fatalError is not null)
            errors.Add(fatalError);

        try {
            await _checkpointer.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            errors.Add(ex);
        }

        // Only a fatal error is propagated; a disposal failure on an otherwise-clean exit is dropped (the
        // full Drone logs it — DroneV0 has no logger).
        if (fatalError is not null)
            throw new AggregateException("Fatal error in processor drone", errors).Flatten();
    }
}
