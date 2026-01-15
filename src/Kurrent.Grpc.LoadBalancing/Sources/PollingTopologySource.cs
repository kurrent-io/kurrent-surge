using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Base class for polling-based topology discovery.
/// Subclasses implement <see cref="ReadAsync"/> to fetch current topology.
/// </summary>
/// <remarks>
/// <para>
/// This class handles the polling loop. Subclasses only need to implement
/// the single <see cref="ReadAsync"/> method for their specific protocol.
/// </para>
/// <para>
/// The source is responsible for querying topology from a given endpoint.
/// The <see cref="ClusterResolver"/> manages the "working set" of endpoints
/// (discovered nodes vs seeds fallback). See <c>reference-cluster-resolver-qa.md</c>.
/// </para>
/// </remarks>
public abstract class PollingTopologySource : TopologySource {
    readonly TimeSpan _pollingInterval;

    /// <summary>
    /// Initializes a new polling topology source.
    /// </summary>
    /// <param name="pollingInterval">Interval between polls.</param>
    protected PollingTopologySource(TimeSpan pollingInterval) {
        if (pollingInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollingInterval), "Must be positive");

        _pollingInterval = pollingInterval;
    }

    /// <summary>
    /// Reads the current cluster topology.
    /// </summary>
    /// <param name="context">Discovery context.</param>
    /// <returns>Current topology snapshot.</returns>
    /// <remarks>
    /// Implement this method to perform protocol-specific discovery
    /// (e.g., gossip, DNS-SRV, health endpoints).
    /// </remarks>
    protected abstract ValueTask<ClusterTopology> ReadAsync(TopologyContext context);

    /// <inheritdoc/>
    protected internal sealed override async Task RunAsync(
        ChannelWriter<ClusterTopology> writer,
        TopologyContext context) {

        var ct = context.CancellationToken;

        while (!ct.IsCancellationRequested) {
            try {
                var topology = await ReadAsync(context).ConfigureAwait(false);
                await writer.WriteAsync(topology, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                context.Logger?.LogWarning(ex, "Topology read failed, retrying after interval");
            }

            try {
                await Task.Delay(_pollingInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                break;
            }
        }

        writer.TryComplete();
    }
}
