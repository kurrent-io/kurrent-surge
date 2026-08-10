using System.Net;
using Kurrent.Surge.Tests.Experimental.LoadBalancing.FakeCluster.Protocol;

namespace Kurrent.Surge.Tests.Experimental.LoadBalancing.FakeCluster;

/// <summary>
/// An in-process KurrentDB-shaped cluster: N Kestrel nodes on dynamic loopback ports,
/// all gossiping one shared membership view. Node states, liveness, and not-leader
/// redirects are mutable per test.
/// </summary>
public sealed class FakeClusterHarness : IAsyncDisposable {
    readonly List<FakeClusterNode> _nodes = [];

    public IReadOnlyList<FakeClusterNode> Nodes => _nodes;

    public FakeClusterNode this[int index] => _nodes[index];

    public DnsEndPoint[] Endpoints => _nodes.Select(node => node.Endpoint).ToArray();

    public static async ValueTask<FakeClusterHarness> Start(params MemberInfo.Types.VNodeState[] states) {
        var cluster = new FakeClusterHarness();

        foreach (var state in states)
            cluster._nodes.Add(new(cluster, state));

        foreach (var node in cluster._nodes)
            await node.StartAsync();

        return cluster;
    }

    public FakeClusterNode NodeInState(MemberInfo.Types.VNodeState state) =>
        _nodes.First(node => node.State == state);

    /// <summary>
    /// Moves leadership to <paramref name="newLeader"/> in the shared view and makes the
    /// old leader answer with not-leader redirects, like a real cluster mid-election.
    /// </summary>
    public void PromoteLeader(FakeClusterNode newLeader) {
        foreach (var node in _nodes.Where(node => node.State == MemberInfo.Types.VNodeState.Leader)) {
            node.State             = MemberInfo.Types.VNodeState.Follower;
            node.NotLeaderRedirect = newLeader.Endpoint;
        }

        newLeader.State             = MemberInfo.Types.VNodeState.Leader;
        newLeader.NotLeaderRedirect = null;
    }

    internal ClusterInfo BuildClusterInfo() {
        var info = new ClusterInfo();

        foreach (var node in _nodes) {
            info.Members.Add(new MemberInfo {
                InstanceId   = new() { String = node.InstanceId.ToString() },
                TimeStamp    = 0,
                State        = node.State,
                IsAlive      = node.IsAlive,
                HttpEndPoint = new() {
                    Address = node.Endpoint.Host,
                    Port    = (uint)node.Endpoint.Port
                }
            });
        }

        return info;
    }

    public async ValueTask DisposeAsync() {
        foreach (var node in _nodes)
            await node.DisposeAsync();
    }
}
