using Grpc.Net.Client.Balancer;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// A node paired with its gRPC subchannel.
/// </summary>
/// <param name="Subchannel">The gRPC subchannel for this node.</param>
/// <param name="Node">The cluster node information.</param>
public readonly record struct SubchannelNode(Subchannel Subchannel, ClusterNode Node);
