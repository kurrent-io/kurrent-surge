using System.Collections.Immutable;
using Kurrent.Client.Streams;

namespace Kurrent.Processors.Tests;

/// <summary>
/// Test helpers for CheckpointStore tests.
/// </summary>
public static class CheckpointTestHelpers {
	/// <summary>
	/// Creates an ImmutableDictionary of partition positions from (partition, position) pairs.
	/// </summary>
	public static ImmutableDictionary<Partition, RecordPosition> Positions(
		params (int partition, long position)[] pairs
	) {
		var builder = ImmutableDictionary.CreateBuilder<Partition, RecordPosition>();
		foreach (var (partition, position) in pairs)
			builder[Partition.From(partition)] =
				RecordPosition.FromLogPosition(new LogPosition(position));
		return builder.ToImmutable();
	}

	/// <summary>
	/// Generates a unique processor ID with the specified prefix.
	/// </summary>
	public static string ProcessorId(string prefix) =>
		$"{prefix}-{Guid.NewGuid():N}";
}
