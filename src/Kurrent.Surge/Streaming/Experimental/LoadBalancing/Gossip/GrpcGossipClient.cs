using System.Collections.Concurrent;
using EventStore.Client.Gossip;
using Grpc.Net.Client;
using KurrentDB.Protocol.V1;
using static EventStore.Client.Gossip.MemberInfo.Types;

namespace Kurrent.Surge.Client.LoadBalancing;

/// <summary>
/// Reads cluster topology over the V1 gossip service, reusing one channel per endpoint.
/// </summary>
sealed class GrpcGossipClient(Func<ClusterEndpoint, GrpcChannel> createChannel, TimeSpan timeout) : IGossipClient {
    static readonly Empty EmptyRequest = new();

    readonly ConcurrentDictionary<ClusterEndpoint, (GrpcChannel Channel, Gossip.GossipClient Client)> _channels = new();

    public async ValueTask<ClusterTopology> GetTopology(ClusterEndpoint endpoint, CancellationToken ct) {
        var (_, client) = _channels.GetOrAdd(
            endpoint,
            static (target, factory) => {
                var channel = factory(target);
                return (channel, new(channel));
            },
            createChannel);

        using var call = client.ReadAsync(EmptyRequest, deadline: DateTime.UtcNow.Add(timeout), cancellationToken: ct);

        var result = await call.ResponseAsync.ConfigureAwait(false);
        var nodes  = result.Members.ToClusterNodes().ToArray();

        return new(nodes);
    }

    public async ValueTask DisposeAsync() {
        foreach (var (channel, _) in _channels.Values) {
            await channel.ShutdownAsync().ConfigureAwait(false);
            channel.Dispose();
        }

        _channels.Clear();
    }
}

static class MemberInfoExtensions {
    extension(IEnumerable<MemberInfo> members) {
        // http_end_point is a proto3 singular message field: null whenever a member
        // hasn't advertised one yet (transitional states). Drop it here instead of
        // letting the whole response fail because one member is incomplete.
        public IEnumerable<ClusterNode> ToClusterNodes() =>
            members.Where(member => member.HttpEndPoint is not null).Select(member => member.ToClusterNode());
    }

    extension(MemberInfo source) {
        public ClusterNode ToClusterNode() =>
            new(source.NodeRole, source.IsAlive, source.ClusterEndPoint);

        ClusterEndpoint ClusterEndPoint =>
            new(source.HttpEndPoint.Address, (int)source.HttpEndPoint.Port);
        
        NodeRole NodeRole =>
            source.State switch {
                VNodeState.ReadOnlyReplica or
                    VNodeState.ReadOnlyLeaderless => NodeRole.Replica,
                VNodeState.Leader   => NodeRole.Leader,
                VNodeState.Follower => NodeRole.Follower,
                _                   => NodeRole.NotAllowed
            };
    }
}
