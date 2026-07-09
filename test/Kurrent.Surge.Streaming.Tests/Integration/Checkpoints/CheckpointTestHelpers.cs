using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Streaming.Tests;

/// <summary>
/// Test helpers for CheckpointStore tests.
/// </summary>
public static class CheckpointTestHelpers {
	/// <summary>
	/// Creates an array of record positions from (partition, logPosition) pairs.
	/// Positions have log position only (no stream or revision).
	/// </summary>
	public static RecordPosition[] Positions(
		params (int partition, long position)[] pairs
	) => pairs
		.Select(p => RecordPosition.From(p.position, Partition.From(p.partition)))
		.ToArray();

	/// <summary>
	/// Creates an array of record positions with full stream info (stream name and revision).
	/// </summary>
	public static RecordPosition[] PositionsWithStream(
		params (int partition, long logPosition, string stream, int revision)[] pairs
	) => pairs
		.Select(p => RecordPosition.From(p.logPosition, p.stream, Revision.From(p.revision), Partition.From(p.partition)))
		.ToArray();

	/// <summary>
	/// Generates a unique processor ID with the specified prefix.
	/// </summary>
	public static string ProcessorId(string prefix) =>
		$"{prefix}-{TestId.New()}";
}
