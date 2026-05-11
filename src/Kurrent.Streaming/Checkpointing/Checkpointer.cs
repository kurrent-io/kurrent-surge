using System.Threading.Channels;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Gateway;
using Kurrent.Streaming.Models;
using Kurrent.Streaming.Options;

namespace Kurrent.Streaming.Checkpointing;

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
	/// Initializes the checkpointer: loads the latest checkpoint and starts the timer.
	/// When <paramref name="startFrom"/> is not <see cref="RecordPosition.Unset"/>, the store
	/// is reset to that position before loading.
	/// </summary>
	public async ValueTask<RecordPosition[]> Initialize(
		Partition[] partitions,
		SubscriptionInitialPosition initialPosition,
		RecordPosition startFrom,
		CancellationToken ct = default
	) {
		ArgumentOutOfRangeException.ThrowIfZero(partitions.Length, nameof(partitions));

		if (_initialized)
			throw new InvalidOperationException("Checkpointer has already been initialized.");

        if (startFrom != RecordPosition.Unset)
            await _store.ResetAsync(_droneId, _clock.Now(), startFrom, ct).ConfigureAwait(false);

		var checkpoint = await _store
			.LoadLatestAsync(_droneId, partitions, initialPosition, ct)
			.ConfigureAwait(false);

		_tracker.Reset(checkpoint.Positions);

		_timer.Start(ct);
		_initialized = true;

		return checkpoint.Positions;
	}
    
    /// <summary>Loads the latest checkpoint without resetting.</summary>
    public async ValueTask<RecordPosition[]> Initialize(
        Partition[] partitions,
        SubscriptionInitialPosition initialPosition,
        CancellationToken ct = default
    ) => await Initialize(partitions, initialPosition, RecordPosition.Unset, ct).ConfigureAwait(false);

	/// <summary>Tracks a processed position for its partition.</summary>
	public void Track(RecordPosition position) {
		EnsureIsInitialized();
		_tracker.Track(position);
	}

	/// <summary>
	/// Returns true if there are tracked positions that have not yet been committed.
	/// </summary>
	public bool HasPendingPositions => _tracker.HasPendingPositions;

	/// <summary>
	/// Snapshots all pending positions (tracked but not yet committed). Positions
	/// still at <see cref="LogPosition.Earliest"/> are filtered out — they represent
	/// partitions with no progress.
	/// </summary>
	public RecordPosition[] PendingPositions() {
		EnsureIsInitialized();
		return _tracker.Snapshot()
			.Where(x => x.LogPosition != LogPosition.Earliest)
			.ToArray();
	}

	/// <summary>
	/// Writes a barrier into the channel. Barriers carry no positions — the Drone
	/// snapshots from <see cref="PendingPositions"/> at handle time. On Shutdown/Rebalance,
	/// completes the writer so the dispatch loop drains and exits.
	/// </summary>
	public async ValueTask RaiseCheckpoint(CheckpointTrigger trigger, string? reason = null) {
		ArgumentOutOfRangeException.ThrowIfEqual(trigger, CheckpointTrigger.Unspecified);

		EnsureIsInitialized();

		if (_shutdownIssued)
			throw new InvalidOperationException("Shutdown has already been issued, cannot raise further checkpoints.");

		var now = _clock.Now();

		var checkpoint = new Checkpoint(now, trigger, now.DateTimeOffset, []) { Reason = reason };

		try {
			await _writer
				.WriteAsync(checkpoint)
				.ConfigureAwait(false);
		}
		catch (ChannelClosedException) {
			return;
		}

		if (trigger is CheckpointTrigger.Shutdown or CheckpointTrigger.Rebalance) {
			_shutdownIssued = true;
			_writer.TryComplete();
		}
	}

	/// <summary>
	/// Commits a checkpoint to the store and advances the tracker past the committed positions.
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
