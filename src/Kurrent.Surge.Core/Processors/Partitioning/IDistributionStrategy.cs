namespace Kurrent.Client.Processors.Partitioning;

/// <summary>
/// Strategy for distributing partitions across drones.
/// </summary>
/// <remarks>
/// <para>
/// Implementations define how partitions are assigned to drones during initial
/// distribution and rebalancing. Different strategies optimize for different goals:
/// </para>
/// <list type="bullet">
///   <item><see cref="StickyStrategy"/>: Minimizes partition movement (production default)</item>
///   <item><see cref="RangeStrategy"/>: Deterministic contiguous ranges (testing, initial assignment)</item>
/// </list>
/// </remarks>
[PublicAPI]
public interface IDistributionStrategy {
    /// <summary>
    /// Distributes partitions across drones, computing both assignments and transfers.
    /// </summary>
    /// <param name="current">The current distribution manifest (use <see cref="DistributionManifest.Empty"/> for initial distribution).</param>
    /// <param name="drones">The drones to distribute partitions to. Order may affect assignment depending on strategy.</param>
    /// <param name="maxParallelism">The total number of partitions to distribute (1 to 32768).</param>
    /// <returns>A new manifest with updated assignments and transfer information.</returns>
    /// <remarks>
    /// The returned manifest includes:
    /// <list type="bullet">
    ///   <item><b>Assignments:</b> Per-drone partition ownership with kept/gained/lost tracking</item>
    ///   <item><b>Transfers:</b> Which partitions moved from which drone to which drone</item>
    /// </list>
    /// </remarks>
    DistributionManifest Distribute(
        DistributionManifest current,
        string[] drones,
        int maxParallelism
    );
}
