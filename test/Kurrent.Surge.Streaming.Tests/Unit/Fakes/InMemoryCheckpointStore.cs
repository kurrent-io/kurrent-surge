using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors.Checkpointing;
using Kurrent.Surge.Streaming.Models;

namespace Kurrent.Surge.Streaming.Tests.Fakes;

sealed class InMemoryCheckpointStore() : ICheckpointStore {

    public ValueTask<ProcessorCheckpoint> LoadLatestAsync(string droneId, CancellationToken ct = default) =>
		new(ProcessorCheckpoint.Empty);

	public ValueTask<ProcessorCheckpoint> LoadLatestAsync(
		string droneId,
		Partition[] partitions,
		SubscriptionInitialPosition initialPosition,
		CancellationToken ct = default
	) {
		var defaultPos = initialPosition == SubscriptionInitialPosition.Earliest
			? RecordPosition.Earliest
			: RecordPosition.Latest;

		var positions = partitions
			.Select(p => defaultPos with { Partition = p })
			.ToArray();

		return new(new ProcessorCheckpoint { Positions = positions });
	}

	public ValueTask StoreAsync(
		string droneId, string checkpointId,
		RecordPosition[] positions, CancellationToken ct = default
	) => ValueTask.CompletedTask;

	public ValueTask<ProcessorCheckpoint> ResetAsync(
		string droneId, string checkpointId,
		LogPosition position, CancellationToken ct = default
	) => new(ProcessorCheckpoint.Empty);
}
