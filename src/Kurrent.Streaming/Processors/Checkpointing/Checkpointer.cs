using System.Threading.Channels;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
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
    readonly bool                            _autoCheckpoint;

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

		_autoCheckpoint = options.AutoCheckpoint;

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

		// AutoCheckpoint gates the interval commit timer: when off, pending positions are committed
		// only by a manual ctx.Checkpoint barrier or on shutdown, so the timer simply never starts.
		if (_autoCheckpoint)
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
	/// Broadcasts a global scan watermark: advances every given partition to <paramref name="logPosition"/>,
	/// skipping any already at or past it (and skipping an unset position). Lets a server checkpoint's log
	/// position fold into all owned partitions so a cold restart skips already-scanned regions. The skip is
	/// required because the underlying tracker is strictly monotonic — re-tracking a reached position throws.
	/// </summary>
	public void AdvanceWatermark(LogPosition logPosition, Partition[] partitions) {
		EnsureIsInitialized();

		if (logPosition == LogPosition.Unset)
			return;

		// Current position per partition (last pending, else last flushed) — only advance the ones behind.
		var current = _tracker.Snapshot().ToDictionary(p => p.Partition);

		foreach (var partition in partitions) {
			var watermark = RecordPosition.From(logPosition, partition);

			if (current.TryGetValue(partition, out var pos) && watermark <= pos)
				continue;

			_tracker.Track(watermark);
		}
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
        
		CheckpointId id = new(now);

		Checkpoint checkpoint = trigger switch {
			CheckpointTrigger.Interval  => new IntervalCheckpoint(id, now.DateTimeOffset),
			CheckpointTrigger.CaughtUp  => new CaughtUpCheckpoint(id, now.DateTimeOffset),
			CheckpointTrigger.Manual    => new ManualCheckpoint(id, now.DateTimeOffset, reason),
			CheckpointTrigger.Shutdown  => new ShutdownCheckpoint(id, now.DateTimeOffset),
			CheckpointTrigger.Rebalance => new RebalanceCheckpoint(id, now.DateTimeOffset),
			_                           => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, null)
		};

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
