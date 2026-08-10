using System.Collections.Concurrent;
using EventStore.Client.Gossip;
using Grpc.Net.Client;

namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// Reads cluster topology over the V1 gossip service, reusing one channel per endpoint.
/// </summary>
sealed class GrpcGossipClient(Func<ClusterEndpoint, GrpcChannel> createChannel, TimeSpan timeout) : IGossipClient {
    readonly ConcurrentDictionary<ClusterEndpoint, (GrpcChannel Channel, Gossip.GossipClient Client)> _channels = new();

    public async ValueTask<ClusterTopology> GetTopology(ClusterEndpoint endpoint, CancellationToken cancellationToken) {
        var (_, client) = _channels.GetOrAdd(
            endpoint,
            static (target, factory) => {
                var channel = factory(target);
                return (channel, new(channel));
            },
            createChannel);

        using var call = client.ReadAsync(new(), deadline: DateTime.UtcNow.Add(timeout), cancellationToken: cancellationToken);

        var result = await call.ResponseAsync.ConfigureAwait(false);

        var nodes = result.Members
            .Select(member => new ClusterNode(
                member.State.ToNodeRole(),
                member.IsAlive,
                new(member.HttpEndPoint.Address, (int)member.HttpEndPoint.Port)))
            .ToArray();

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
