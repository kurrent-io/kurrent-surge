using System.Collections.Concurrent;
using System.Net;
using EventStore.Client.Gossip;
using Grpc.Net.Client;

namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// Reads cluster topology over the V1 gossip service, reusing one channel per endpoint.
/// </summary>
sealed class GrpcGossipClient(Func<DnsEndPoint, GrpcChannel> createChannel, TimeSpan timeout) : IGossipClient {
    readonly ConcurrentDictionary<DnsEndPoint, GrpcChannel> _channels = new(DnsEndPointComparer.Instance);

    public async ValueTask<ClusterTopology> GetTopology(DnsEndPoint endpoint, CancellationToken cancellationToken) {
        var channel = _channels.GetOrAdd(endpoint, createChannel);
        var client  = new Gossip.GossipClient(channel);

        using var call = client.ReadAsync(new(), deadline: DateTime.UtcNow.Add(timeout), cancellationToken: cancellationToken);

        var result = await call.ResponseAsync.ConfigureAwait(false);

        var nodes = result.Members
            .Select(member => new ClusterNode(
                (VNodeState)member.State,
                member.IsAlive,
                new(member.HttpEndPoint.Address, (int)member.HttpEndPoint.Port)))
            .ToArray();

        return new(nodes);
    }

    public async ValueTask DisposeAsync() {
        foreach (var channel in _channels.Values) {
            await channel.ShutdownAsync().ConfigureAwait(false);
            channel.Dispose();
        }

        _channels.Clear();
    }
}
