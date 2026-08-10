namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// Reads the cluster topology from a node's gossip endpoint.
/// </summary>
interface IGossipClient : IAsyncDisposable {
    ValueTask<ClusterTopology> GetTopology(ClusterEndpoint endpoint, CancellationToken ct);
}
