using Kurrent.Client.Processors.Partitioning;
using Kurrent.Streaming.Options;
using Kurrent.Streaming.Partitioning;

namespace Kurrent.Streaming.Hive;

/// <summary>
/// Configuration for a <see cref="Hive"/>: fan-out count and the underlying processor options.
/// </summary>
[PublicAPI]
public sealed class HiveOptions {
    /// <summary>
    /// Number of drones to run in parallel. Each drone owns a subset of partitions.
    /// </summary>
    public int DroneCount { get; set; } = 1;

    /// <summary>
    /// Partition distribution strategy. Defaults to <see cref="StickyStrategy"/>.
    /// </summary>
    public PartitionAssigner Assigner { get; set; } = PartitionAssigner.Default;

    /// <summary>
    /// The processor options shared by every drone. Individual drones override <c>DroneId</c>.
    /// </summary>
    public ProcessorOptions ProcessorOptions { get; init; } = new();

    /// <summary>
    /// Factory invoked once per drone at <see cref="Hive.RunAsync"/>. Must be non-null.
    /// </summary>
    public Func<IRecordProcessor> ProcessorFactory { get; init; } = () =>
        throw new InvalidOperationException("HiveOptions.ProcessorFactory is required.");
}
