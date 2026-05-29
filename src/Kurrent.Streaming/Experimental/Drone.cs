using System.Collections.Frozen;
using System.Threading.Channels;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Checkpointing;
using Kurrent.Streaming.Options;
using Kurrent.Streaming.Partitioning;

namespace Kurrent.Streaming.Experimental;

public sealed class Drone : IAsyncDisposable {
    static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(30);

    static readonly FrozenSet<(DroneState From, DroneState To)> ValidTransitions = new HashSet<(DroneState, DroneState)> {
        (DroneState.Created, DroneState.Starting),
        (DroneState.Starting, DroneState.Running),
        (DroneState.Starting, DroneState.Faulted),
        (DroneState.Running, DroneState.Draining),
        (DroneState.Running, DroneState.Faulted),
        (DroneState.Draining, DroneState.Stopped),
        (DroneState.Draining, DroneState.Faulted),
    }.ToFrozenSet();

    readonly Channel<SubscribeMessage> _channel;
    readonly Checkpointer              _checkpointer;
    readonly ISubscription             _subscription;
    readonly IRecordProcessor          _processor;
    readonly ProcessorOptions          _options;

    readonly StateMachine<DroneState> _fsm;
    readonly CancellationTokenSource  _stopping = new();

    DroneContext _context;
    Task?        _dispatchLoop;
    Task?        _subscriptionLoop;

    public Drone(
        ISubscription subscription,
        ICheckpointStore checkpointStore,
        IRecordProcessor processor,
        ProcessorOptions options,
        TimeProvider timeProvider
    ) {
        _subscription = subscription;
        _processor    = processor;
        _options      = options;
        _fsm          = new StateMachine<DroneState>(DroneState.Created, ValidTransitions);

        _context = new DroneContext { ProcessorId = options.ProcessorId, DroneId = options.DroneId };

        _channel = Channel.CreateBounded<SubscribeMessage>(
            new BoundedChannelOptions(options.BufferCapacity) {
                SingleReader                  = true,
                SingleWriter                  = false,
                AllowSynchronousContinuations = false,
                FullMode                      = BoundedChannelFullMode.Wait
            });

        _checkpointer = new Checkpointer(
            options.DroneId, 
            options.Checkpoint, checkpointStore,
            _channel.Writer, timeProvider);
    }

    /// <summary>Internal FSM position — fine-grained, drives behavior.</summary>
    DroneState State => _fsm.Current;

    /// <summary>Public-facing condition for observers. Coarser than <see cref="State"/>.</summary>
    public DroneStatus Status =>
        _fsm.Current switch {
            DroneState.Created                        => DroneStatus.Unspecified,
            DroneState.Starting                       => DroneStatus.Starting,
            DroneState.Running or DroneState.Draining => DroneStatus.Processing,
            DroneState.Stopped                        => DroneStatus.Stopped,
            DroneState.Faulted                        => DroneStatus.Error,
            _                                         => DroneStatus.Unspecified
        };
    
    public async ValueTask Start(CancellationToken ct = default) {
        if (State is not DroneState.Created)
            throw new InvalidOperationException("This drone has already been started. Create a new instance to run again.");

        _fsm.Transition(DroneState.Starting);

        _context = _context with { CancellationToken = _stopping.Token };

        try {
            var partitions = PartitionAssigner.Default
                .Distribute([_options.DroneId], _options.MaxParallelism)
                [_options.DroneId].All;
            
            var startPositions = await _checkpointer
                .Initialize(partitions, _options.Subscription.InitialPosition, ct: _stopping.Token)
                .ConfigureAwait(false);

            _dispatchLoop     = Task.Run(() => RunDispatchAsync(_stopping.Token));
            _subscriptionLoop = Task.Run(() => RunSubscriptionPumpAsync(startPositions, _stopping.Token));

            _fsm.Transition(DroneState.Running);
        }
        catch {
            _fsm.Transition(DroneState.Faulted);
            throw;
        }
    }

    async ValueTask Drain(string reason, CancellationToken ct = default) {
        // TryTransition is atomic — only the caller that actually moves Running→Draining
        // proceeds. Concurrent racers see Draining (or later) and bail. The FSM IS the
        // reentrance guard.
        if (!_fsm.TryTransition(DroneState.Draining))
            return;

        // Final barrier: the checkpointer writes it and completes the channel writer
        // atomically. The dispatch loop processes any remaining records, the final
        // barrier (firing OnCheckpoint + commit), then exits when the channel drains.
        await _checkpointer.RaiseCheckpoint(CheckpointTrigger.Rebalance, reason).ConfigureAwait(false);

        // Awaiting the dispatch task IS the synchronization barrier — it returns only
        // after the final OnCheckpoint has been invoked.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(DefaultDrainTimeout);

        try {
            await _dispatchLoop!.WaitAsync(deadline.Token).ConfigureAwait(false);
            _fsm.Transition(DroneState.Stopped);
        }
        catch (OperationCanceledException) {
            _fsm.Transition(DroneState.Faulted);
            throw new TimeoutException($"Drain exceeded deadline of {DefaultDrainTimeout}.");
        }
    }

    async Task RunDispatchAsync(CancellationToken ct) {
        try {
            await foreach (var msg in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
                switch (msg.Value) {
                    case Connected c: await HandleConnected(c).ConfigureAwait(false); break;

                    case Record r: await HandleRecord(r, ct).ConfigureAwait(false); break;

                    case Checkpoint cp: await HandleCheckpoint(cp, ct).ConfigureAwait(false); break;
                }
            }
        }
        catch (OperationCanceledException) {
            // Expected only on forced shutdown via DisposeAsync. Graceful Drain does NOT
            // cancel _stopping — it relies on the channel completing naturally so the
            // dispatch loop drains the remaining records and the final barrier first.
        }
    }

    async Task RunSubscriptionPumpAsync(RecordPosition[] startPositions, CancellationToken ct) {
        try {
            await foreach (var msg in _subscription.Subscribe(startPositions, ct).ConfigureAwait(false))
                await _channel.Writer.WriteAsync(msg, ct).ConfigureAwait(false);

            // Server stream ended cleanly — let the checkpointer close the channel.
            await _checkpointer.RaiseCheckpoint(CheckpointTrigger.Shutdown, "stream ended").ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            // Expected: forced shutdown via DisposeAsync.
        }
        catch (Exception ex) {
            // Subscription faulted. Let the checkpointer be the sole completer.
            try {
                await _checkpointer
                    .RaiseCheckpoint(CheckpointTrigger.Shutdown, $"fault: {ex.GetType().Name}: {ex.Message}")
                    .ConfigureAwait(false);
            }
            catch {
                // Best-effort: another path may have already closed the channel.
            }
        }
    }

    async ValueTask HandleConnected(Connected connected) {
        _context = _context with { SessionId = connected.SessionId };

        try {
            await _processor.OnConnected(_context.ForConnected()).ConfigureAwait(false);
        }
        catch {
            // User hook failed. Continue dispatching — we don't surface a separate
            // OnError path.
        }
    }

    async ValueTask HandleRecord(Record record, CancellationToken ct) {
        var ctx = _context.ForProcessRecord(
            DroneProgress.Empty,
            record,
            requestTrack: pos => _checkpointer.Track(pos));

        try {
            await _processor.ProcessRecord(ctx).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception) {
            // User handler threw. Skip Track and continue dispatching — at-least-once
            // redelivery on next start. The user owns their own error handling inside
            // ProcessRecord.
            return;
        }

        // Auto-track unless the user requested stop. Tracker is idempotent if the user
        // also called ctx.Track(). Mirrors the production AutoCheckpoint semantics.
        if (_options.Checkpoint.AutoCheckpoint && !ctx.StopRequested)
            _checkpointer.Track(record.Position);

        if (ctx.CheckpointRequested)
            await _checkpointer
                .RaiseCheckpoint(CheckpointTrigger.Manual, ctx.CheckpointReason)
                .ConfigureAwait(false);

        if (ctx.StopRequested)
            await _checkpointer
                .RaiseCheckpoint(CheckpointTrigger.Shutdown, ctx.StopReason)
                .ConfigureAwait(false);
    }

    async ValueTask HandleCheckpoint(Checkpoint checkpoint, CancellationToken ct) {
        var positions = _checkpointer.PendingPositions();

        if (positions.Length == 0)
            return;

        try {
            await _processor.OnCheckpoint(_context.ForCheckpoint(positions, checkpoint)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch {
            // User OnCheckpoint failed. We still commit positions — the user's hook is
            // observational, the framework's commit obligation is unconditional.
        }

        await _checkpointer
            .CommitCheckpoint(checkpoint.Id, positions, ct)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() {
        if (_fsm.Current is DroneState.Running) {
            try {
                await Drain("disposed").ConfigureAwait(false);
            }
            catch (TimeoutException) {
                // Drain hit its deadline — proceed with forced disposal.
            }
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        await _checkpointer.DisposeAsync().ConfigureAwait(false);

        if (_subscriptionLoop is not null) {
            try {
                await _subscriptionLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // Expected: cancellation we just initiated.
            }
        }

        if (_dispatchLoop is not null) {
            try {
                await _dispatchLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // Expected: cancellation we just initiated.
            }
        }

        _stopping.Dispose();
    }
}

/// <summary>
/// Internal FSM position. Distinct from the public-facing <see cref="DroneStatus"/> —
/// this enum has finer-grained states (e.g., <see cref="Draining"/>) that gate
/// behavior but aren't worth surfacing to external observers.
/// </summary>
internal enum DroneState {
    Created,
    Starting,
    Running,
    Draining,
    Stopped,
    Faulted
}