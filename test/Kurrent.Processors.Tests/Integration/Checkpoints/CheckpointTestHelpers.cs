using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;

namespace Kurrent.Processors.Tests;

/// <summary>
/// Test helpers for CheckpointStore tests.
/// </summary>
public static class CheckpointTestHelpers {
	/// <summary>
	/// Creates an array of partition positions from (partition, position) pairs.
	/// </summary>
	public static PartitionPosition[] Positions(
		params (int partition, long position)[] pairs
	) => pairs
		.Select(p => new PartitionPosition(Partition.From(p.partition), RecordPosition.FromLogPosition(p.position)))
		.ToArray();

	/// <summary>
	/// Generates a unique processor ID with the specified prefix.
	/// </summary>
	public static string ProcessorId(string prefix) =>
		$"{prefix}-{PunyId.New()}";
}
