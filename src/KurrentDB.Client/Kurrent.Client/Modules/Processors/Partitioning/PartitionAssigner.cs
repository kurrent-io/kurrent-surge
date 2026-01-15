using Kurrent.Client.Streams;

namespace Kurrent.Client.Processors.Partitioning;

/// <summary>
/// Assigns partitions to workers using a configurable distribution strategy.
/// </summary>
/// <remarks>
/// <para>
/// This is the main entry point for partition distribution. It combines key-to-partition
/// mapping (via <see cref="PartitionCalculator"/>) with worker assignment (via <see cref="IDistributionStrategy"/>).
/// </para>
/// <para>
/// The default strategy is <see cref="StickyStrategy"/>, which minimizes partition movement
/// during rebalancing. Use <see cref="RangeStrategy"/> for deterministic contiguous ranges.
/// </para>
/// <para>
/// <b>Thread Safety:</b> This class is thread-safe. All methods are pure functions with no shared state.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Default behavior (sticky strategy)
/// var manifest = PartitionAssigner.Default.Distribute(
///     DistributionManifest.Empty,
///     workers,
///     maxParallelism: 128
/// );
///
/// // With range strategy for deterministic testing
/// var rangeAssigner = new PartitionAssigner(RangeStrategy.Instance);
/// var manifest = rangeAssigner.Distribute(DistributionManifest.Empty, workers, 128);
///
/// // Key-to-partition mapping
/// var partition = PartitionAssigner.Default.GetPartition("customer-123", maxParallelism: 128);
/// </code>
/// </example>
[PublicAPI]
public sealed class PartitionAssigner {
    readonly IDistributionStrategy _strategy;
    readonly PartitionCalculator _calculator;

    /// <summary>
    /// Default instance using <see cref="StickyStrategy"/> (minimal partition movement).
    /// </summary>
    public static readonly PartitionAssigner Default = new(StickyStrategy.Instance);

    /// <summary>
    /// Creates a partition assigner with the specified distribution strategy.
    /// </summary>
    /// <param name="strategy">The strategy to use for distributing partitions to workers.</param>
    /// <param name="calculator">Optional partition calculator for key-to-partition mapping. Defaults to <see cref="PartitionCalculator.Default"/>.</param>
    public PartitionAssigner(IDistributionStrategy strategy, PartitionCalculator? calculator = null) {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _calculator = calculator ?? PartitionCalculator.Default;
    }

    /// <summary>
    /// Maps a partition key to a <see cref="Partition"/> using a deterministic hash function.
    /// </summary>
    /// <param name="key">The partition key (e.g., stream ID, customer ID).</param>
    /// <param name="maxParallelism">The total number of partitions (1 to 32768).</param>
    /// <returns>A <see cref="Partition"/> with value in range [0, maxParallelism).</returns>
    /// <remarks>
    /// The same key always maps to the same partition for a given maxParallelism.
    /// This ensures state locality: all records with the same key are processed by the same worker.
    /// </remarks>
    public Partition GetPartition(string key, int maxParallelism) =>
        _calculator.For(key, maxParallelism);

    /// <summary>
    /// Distributes partitions across workers using the configured strategy.
    /// </summary>
    /// <param name="current">The current distribution manifest (use <see cref="DistributionManifest.Empty"/> for initial distribution).</param>
    /// <param name="workers">The workers to distribute partitions to.</param>
    /// <param name="maxParallelism">The total number of partitions to distribute (1 to 32768).</param>
    /// <returns>A new manifest with updated assignments and transfer information.</returns>
    public DistributionManifest Distribute(
        DistributionManifest current,
        WorkerId[] workers,
        int maxParallelism
    ) => _strategy.Distribute(current, workers, maxParallelism);
}
