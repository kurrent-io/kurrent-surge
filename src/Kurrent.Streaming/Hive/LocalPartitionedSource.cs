using System.Threading.Channels;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Checkpointing;
using Kurrent.Streaming.Gateway;
using Kurrent.Streaming.Options;

namespace Kurrent.Streaming.Hive;

/// <summary>
/// Per-drone pipe: receives messages from <see cref="MasterSubscriptionSource"/>, applies a
/// catch-up filter, and writes the drone's checkpoints under its own
/// <see cref="ProcessorOptions.DroneId"/>.
/// </summary>
/// <remarks>
/// TODO: Refactor to channel-writer model (follow-up to SubscriptionSource refactoring).
/// Currently implements <see cref="ISubscription"/> with a fire-and-forget pump that reads
/// from the master's fan-out channel and writes to the drone's channel.
/// </remarks>
sealed class LocalPartitionedSource : ISubscription {
	readonly MasterSubscriptionSource _master;
	readonly string                   _droneId;
	readonly Partition[]              _assignedPartitions;
	readonly ProcessorOptions         _settings;
	readonly ICheckpointStore          _checkpointStore;
	readonly ChannelWriter<SubscribeMessage> _writer;
	readonly HybridLogicalClock       _clock;

	readonly Channel<SubscribeMessage> _inbox;

	Dictionary<Partition, LogPosition> _myStart = new();

	public LocalPartitionedSource(
		MasterSubscriptionSource master,
		string droneId,
		Partition[] assignedPartitions,
		ProcessorOptions settings,
		ICheckpointStore checkpointStore,
		ChannelWriter<SubscribeMessage> writer,
		TimeProvider timeProvider
	) {
		_master             = master;
		_droneId            = droneId;
		_assignedPartitions = assignedPartitions;
		_settings           = settings;
		_checkpointStore    = checkpointStore;
		_writer             = writer;
		_clock              = new HybridLogicalClock(timeProvider);

		_inbox = Channel.CreateBounded<SubscribeMessage>(
			new BoundedChannelOptions(settings.BufferCapacity) {
				FullMode     = BoundedChannelFullMode.Wait,
				SingleReader = true,
				SingleWriter = false
			});

		_master.RegisterDrone(_droneId, _assignedPartitions, _inbox.Writer);
	}

	public async Task Subscribe(
		RecordPosition    startPosition,
		IRecordFilter     filter,
		bool              skipDecoding,
		CancellationToken ct
	) {
		var startPositions = _master.GetStartPositions(_droneId);

		_myStart = new Dictionary<Partition, LogPosition>(startPositions.Length);
		foreach (var p in startPositions)
			_myStart[p.Partition] = p.LogPosition;

		try {
			await foreach (var msg in _inbox.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false)) {
				switch (msg.Value) {
					case Record record: {
						if (_myStart.TryGetValue(record.Position.Partition, out var myStart)
							&& record.Position.LogPosition <= myStart)
							continue;

						await _writer.WriteAsync(record, ct).ConfigureAwait(false);
						break;
					}

					default:
						await _writer.WriteAsync(msg, ct).ConfigureAwait(false);
						break;
				}
			}

			_writer.TryComplete();
		}
		catch (OperationCanceledException) {
			_writer.TryComplete();
		}
		catch (Exception ex) {
			_writer.TryComplete(ex);
		}
	}

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
