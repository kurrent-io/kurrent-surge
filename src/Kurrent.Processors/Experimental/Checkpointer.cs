using System.Threading.Channels;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Gateway;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;

namespace Kurrent.Processors.Experimental;

public sealed class Checkpointer : IAsyncDisposable {
    readonly string                          _droneId;
    readonly ChannelWriter<SubscribeMessage> _writer;
    readonly ICheckpointStore                _store;
    readonly PartitionedPositionTracker      _tracker;
    readonly HybridLogicalClock              _clock;
    readonly AsyncPeriodicTimer              _timer;
    
    volatile bool _shutdownIssued;
    
    public Checkpointer(
        string droneId,
        CheckpointOptions options,
        ICheckpointStore store,
        ChannelWriter<SubscribeMessage> writer,
        TimeProvider timeProvider
    ) {
        _droneId = droneId;
        _writer  = writer;
        _store   = store;
        _tracker = new();
        _clock   = new(timeProvider);
        
        _timer = new(
            options.Interval, timeProvider,
            _ => _shutdownIssued || !_initialized || !_tracker.HasPendingPositions
                ? Task.CompletedTask
                : RaiseCheckpoint(CheckpointTrigger.Interval).AsTask());
    }

    bool _initialized;
    
    /// <summary>
    /// Loads the latest checkpoin from the store and returns the start positions to feed to the subscription.
    /// </summary>
    public async ValueTask<RecordPosition[]> Initialize(Partition[] partitions, SubscriptionInitialPosition initialPosition, CancellationToken ct) {
        ArgumentOutOfRangeException.ThrowIfZero(partitions.Length, nameof(partitions));

        if (_initialized)
            throw new InvalidOperationException("Checkpointer has already been initialized.");
        
        var checkpoint = await _store
            .LoadLatestAsync(partitions, initialPosition, ct)
            .ConfigureAwait(false);

        _tracker.Reset(checkpoint.Positions);

        _timer.Start(ct);
        _initialized = true;

        return checkpoint.Positions;
    }

    public void Track(RecordPosition position) {
        if (!_initialized)
            throw new InvalidOperationException("Checkpointer has not been initialized yet.");
        
        _tracker.Track(position);
    }

    /// <summary>
    /// Writes a barrier into the channel. On Shutdown/Rebalance, completes the writer
    /// so the dispatch loop drains the remaining items and exits — the sole-completer
    /// rule.
    /// </summary>
    public async ValueTask RaiseCheckpoint(CheckpointTrigger trigger, string? reason = null) {
        ArgumentOutOfRangeException.ThrowIfEqual(trigger, CheckpointTrigger.Unspecified);
        
        EnsureIsInitialized();
        
        if (_shutdownIssued)
            throw new InvalidOperationException("Shutdown has already been issued, cannot raise further checkpoints.");

        var now = _clock.Now();

        var checkpoint = new Checkpoint(
            now,
            trigger,
            now.DateTimeOffset,
            _tracker.Snapshot()) { Reason = reason };
        
        try {
            await _writer
                .WriteAsync(checkpoint)
                .ConfigureAwait(false);
        }
        catch (ChannelClosedException) {
            return; // if its closed, why are we even Raising a checkpoint?
        }

        if (trigger is CheckpointTrigger.Shutdown or CheckpointTrigger.Rebalance) {
            _shutdownIssued = true;
            _writer.TryComplete();
        }
    }

    /// <summary>
    /// Commits a checkpoint to the checkpoint store and updates the internal position tracker
    /// with the provided record positions.
    /// </summary>
    public async ValueTask CommitCheckpoint(
        string checkpointId,
        RecordPosition[] positions,
        CancellationToken ct = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
       
        EnsureIsInitialized();
        
        await _store
            .StoreAsync(_droneId, checkpointId, positions, ct)
            .ConfigureAwait(false);
        
        _tracker.FlushUpTo(positions);
    }

    void EnsureIsInitialized() {
        if (!_initialized)
            throw new InvalidOperationException("Checkpointer has not been initialized yet.");
    }

    public async ValueTask DisposeAsync() {
        await _timer.DisposeAsync();
        _tracker.Dispose();
    }
}



// public sealed class Checkpointer : IAsyncDisposable {
//     readonly ChannelWriter<SubscribeMessage> _writer;
//     readonly ICheckpointStore                _store;
//     readonly string                          _droneId;
//     // readonly SubscriptionInitialPosition     _initialPosition;
//     // readonly Partition[]                     _partitions;
//     readonly HybridLogicalClock              _clock;
//     readonly PartitionedPositionTracker      _tracker;
//     readonly AsyncPeriodicTimer              _timer;
//  
//     
//     volatile bool _shutdownIssued;
//     
//     public Checkpointer(
//         ChannelWriter<SubscribeMessage> writer,
//         ICheckpointStore store,
//         ProcessorOptions options,
//         TimeProvider timeProvider) {
//         _writer          = writer;
//         _store           = store;
//         _droneId         = options.DroneId;
//         _initialPosition = options.Subscription.InitialPosition;
//
//         _tracker = new();
//         
//         _partitions = Enumerable
//             .Range(0, options.MaxParallelism)
//             .Select(Partition.From)
//             .ToArray();
//
//         _clock = new(timeProvider);
//         
//         _timer = new(
//             options.Checkpoint.Interval, timeProvider,
//             _ => _shutdownIssued || !_initialized 
//                 ? Task.CompletedTask 
//                 : RaiseCheckpoint(CheckpointTrigger.Interval).AsTask());
//     }
//
//     bool _initialized;
//     
//     /// <summary>
//     /// Loads the latest checkpoin from the store and returns the start positions to feed to the subscription.
//     /// </summary>
//     public async ValueTask<RecordPosition[]> Initialize(CancellationToken ct) {
//         if (_initialized)
//             throw new InvalidOperationException("Checkpointer has already been initialized.");
//
//         var checkpoint = await _store
//             .LoadLatestAsync(_partitions, _initialPosition, ct)
//             .ConfigureAwait(false);
//
//         _tracker.Reset(checkpoint.Positions);
//
//         _initialized = true;
//         
//         return checkpoint.Positions;
//     }
//     
//     public async ValueTask<RecordPosition[]> Initialize(Partition[] partitions, SubscriptionInitialPosition initialPosition, CancellationToken ct) {
//         if (_initialized)
//             throw new InvalidOperationException("Checkpointer has already been initialized.");
//
//         var checkpoint = await _store
//             .LoadLatestAsync(partitions, initialPosition, ct)
//             .ConfigureAwait(false);
//
//         _tracker.Reset(checkpoint.Positions);
//
//         _initialized = true;
//         
//         return checkpoint.Positions;
//     }
//
//     public void Track(RecordPosition position) {
//         if (!_initialized)
//             throw new InvalidOperationException("Checkpointer has not been initialized yet.");
//         
//         _tracker.Track(position);
//     }
//
//     /// <summary>
//     /// Writes a barrier into the channel. On Shutdown/Rebalance, completes the writer
//     /// so the dispatch loop drains the remaining items and exits — the sole-completer
//     /// rule.
//     /// </summary>
//     public async ValueTask RaiseCheckpoint(CheckpointTrigger trigger, string? reason = null) {
//         if (!_initialized)
//             throw new InvalidOperationException("Checkpointer has not been initialized yet.");
//
//         if (_shutdownIssued)
//             return;
//
//         var now = _clock.Now();
//
//         var checkpoint = new Checkpoint(
//             CheckpointId.From(now),
//             trigger,
//             now.DateTimeOffset,
//             _tracker.LastTrackedPositions) { Reason = reason };
//         
//         try {
//             await _writer
//                 .WriteAsync(checkpoint)
//                 .ConfigureAwait(false);
//         }
//         catch (ChannelClosedException) {
//             return;
//         }
//
//         if (trigger is CheckpointTrigger.Shutdown or CheckpointTrigger.Rebalance) {
//             _shutdownIssued = true;
//             _writer.TryComplete();
//         }
//     }
//
//     public async ValueTask CommitCheckpoint(
//         CheckpointId      checkpointId,
//         RecordPosition[]  positions,
//         CancellationToken ct = default
//     ) {
//         if (!_initialized)
//             throw new InvalidOperationException("Checkpointer has not been initialized yet.");
//
//         await _store
//             .StoreAsync(_droneId, checkpointId.Value, positions, ct)
//             .ConfigureAwait(false);
//         
//         _tracker.FlushUpTo(positions);
//     }
//
//     public async ValueTask DisposeAsync() {
//         await _timer.DisposeAsync();
//         _tracker.Dispose();
//     }
// }
