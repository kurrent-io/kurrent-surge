using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;

namespace Kurrent.Processors.Tests;

/// <summary>
/// Test helpers for CheckpointStore tests.
/// </summary>
public static class CheckpointTestHelpers {
	/// <summary>
	/// Creates an array of partition positions from (partition, logPosition) pairs.
	/// Positions have log position only (no stream or revision).
	/// </summary>
	public static PartitionPosition[] Positions(
		params (int partition, long position)[] pairs
	) => pairs
		.Select(p => new PartitionPosition(Partition.From(p.partition), RecordPosition.From(p.position)))
		.ToArray();

	/// <summary>
	/// Creates an array of partition positions with full stream info (stream name and revision).
	/// </summary>
	public static PartitionPosition[] PositionsWithStream(
		params (int partition, long logPosition, string stream, int revision)[] pairs
	) => pairs
		.Select(p => new PartitionPosition(
			Partition.From(p.partition),
			RecordPosition.From(p.logPosition, p.stream, Revision.From(p.revision), Partition.From(p.partition))))
		.ToArray();

	/// <summary>
	/// Generates a unique processor ID with the specified prefix.
	/// </summary>
	public static string ProcessorId(string prefix) =>
		$"{prefix}-{PunyId.New()}";
}
