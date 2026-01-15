using System.Threading.Channels;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Base class for topology discovery sources.
/// Subclasses implement protocol-specific discovery logic.
/// </summary>
/// <remarks>
/// <para>
/// The topology source is responsible for:
/// <list type="number">
/// <item>Discovering cluster members via protocol-specific means</item>
/// <item>Mapping protocol state to <see cref="ClusterNode"/></item>
/// </list>
/// </para>
/// <para>
/// The resolver owns the <see cref="ChannelWriter{T}"/> and passes it to
/// <see cref="RunAsync"/>. This decouples the producer from the consumer.
/// </para>
/// <para>
/// Node selection/picking is handled separately by <see cref="IClusterNodePickingStrategy"/>,
/// not by the topology source. This separation allows the same topology source to be
/// used with different picking strategies.
/// </para>
/// </remarks>
public abstract class TopologySource {
    /// <summary>
    /// Runs the topology discovery loop, writing updates to the channel.
    /// </summary>
    /// <param name="writer">Channel writer for topology updates.</param>
    /// <param name="context">Discovery context with channel, seeds, cancellation.</param>
    /// <returns>Task that completes when discovery stops.</returns>
    /// <remarks>
    /// This method should run continuously until cancelled, periodically
    /// (or reactively) writing new <see cref="ClusterTopology"/> snapshots.
    /// </remarks>
    protected internal abstract Task RunAsync(
        ChannelWriter<ClusterTopology> writer,
        TopologyContext context);
}
