using System.Net;
using System.Net.Sockets;
using Grpc.Core;
using Kurrent.Surge.Experimental.LoadBalancing;
using Kurrent.Surge.Tests.Experimental.LoadBalancing.FakeCluster;
using KurrentDB.Client;
using NodeState = Kurrent.Surge.Tests.Experimental.LoadBalancing.FakeCluster.Protocol.MemberInfo.Types.VNodeState;
using ProbeClient = EventStore.Client.Gossip.Gossip.GossipClient;

namespace Kurrent.Surge.Tests.Experimental.LoadBalancing;

[Category("Integration")]
[Timeout(30_000)]
public class ClusterChannelTests {
    const string ServingNodePortTrailer = "serving-node-port";

    [Test]
    public async ValueTask routes_calls_to_leader_when_leader_preferred(CancellationToken cancellationToken) {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower, NodeState.Follower);
        var expectedPort = cluster.NodeInState(NodeState.Leader).Endpoint.Port.ToString();

        await using var channel = ClusterChannel.Create(CreateOptions(cluster, NodePreference.Leader));
        var client = new ProbeClient(channel.CallInvoker);

        // Act
        var servingPort = await ReadServingPort(client, cancellationToken);

        // Assert
        await Assert.That(servingPort).IsEqualTo(expectedPort);
    }

    [Test]
    public async ValueTask routes_calls_to_follower_when_follower_preferred(CancellationToken cancellationToken) {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower, NodeState.Follower);
        var expectedPorts = cluster.Nodes
            .Where(node => node.State == NodeState.Follower)
            .Select(node => node.Endpoint.Port.ToString())
            .ToArray();

        await using var channel = ClusterChannel.Create(CreateOptions(cluster, NodePreference.Follower));
        var client = new ProbeClient(channel.CallInvoker);

        // Act
        var servingPort = await ReadServingPort(client, cancellationToken);

        // Assert
        await Assert.That(expectedPorts).Contains(servingPort!);
    }

    [Test]
    public async ValueTask routes_calls_to_read_only_replica_when_replica_preferred(CancellationToken cancellationToken) {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower, NodeState.ReadOnlyReplica);
        var expectedPort = cluster.NodeInState(NodeState.ReadOnlyReplica).Endpoint.Port.ToString();

        await using var channel = ClusterChannel.Create(CreateOptions(cluster, NodePreference.ReadOnlyReplica));
        var client = new ProbeClient(channel.CallInvoker);

        // Act
        var servingPort = await ReadServingPort(client, cancellationToken);

        // Assert
        await Assert.That(servingPort).IsEqualTo(expectedPort);
    }

    [Test]
    public async ValueTask discovers_full_topology_from_a_single_seed(CancellationToken cancellationToken) {
        // Arrange: seed only a follower; the leader is discovered through its gossip.
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower, NodeState.Follower);
        var seed         = cluster.NodeInState(NodeState.Follower);
        var expectedPort = cluster.NodeInState(NodeState.Leader).Endpoint.Port.ToString();

        var options = CreateOptions(cluster, NodePreference.Leader);
        options.Endpoints = [seed.Endpoint];

        await using var channel = ClusterChannel.Create(options);
        var client = new ProbeClient(channel.CallInvoker);

        // Act
        var servingPort = await ReadServingPort(client, cancellationToken);

        // Assert
        await Assert.That(servingPort).IsEqualTo(expectedPort);
    }

    [Test]
    public async ValueTask follows_leader_change_announced_via_not_leader_redirect(CancellationToken cancellationToken) {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower, NodeState.Follower);
        var oldLeader = cluster.NodeInState(NodeState.Leader);
        var newLeader = cluster.Nodes.First(node => node != oldLeader);
        var expectedPort = newLeader.Endpoint.Port.ToString();

        await using var channel = ClusterChannel.Create(CreateOptions(cluster, NodePreference.Leader));
        var client = new ProbeClient(channel.CallInvoker);

        var initialPort = await ReadServingPort(client, cancellationToken);

        // Act: leadership moves; the old leader answers with not-leader redirects.
        cluster.PromoteLeader(newLeader);
        var servingPort = await WaitUntilServedBy(client, expectedPort, cancellationToken);

        // Assert
        await Assert.That(initialPort).IsEqualTo(oldLeader.Endpoint.Port.ToString());
        await Assert.That(servingPort).IsEqualTo(expectedPort);
    }

    [Test]
    public async ValueTask fails_over_to_next_preferred_node_when_selected_node_stops(CancellationToken cancellationToken) {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower, NodeState.Follower);
        var leader = cluster.NodeInState(NodeState.Leader);
        var followerPorts = cluster.Nodes
            .Where(node => node != leader)
            .Select(node => node.Endpoint.Port.ToString())
            .ToArray();

        await using var channel = ClusterChannel.Create(CreateOptions(cluster, NodePreference.Leader));
        var client = new ProbeClient(channel.CallInvoker);

        var initialPort = await ReadServingPort(client, cancellationToken);

        // Act: the leader dies; the survivors gossip it as dead.
        leader.IsAlive = false;
        await leader.StopAsync();
        var servingPort = await WaitUntilServedByAny(client, followerPorts, cancellationToken);

        // Assert
        await Assert.That(initialPort).IsEqualTo(leader.Endpoint.Port.ToString());
        await Assert.That(followerPorts).Contains(servingPort!);
    }

    [Test]
    public async ValueTask surfaces_unavailable_when_discovery_exhausts_attempts(CancellationToken cancellationToken) {
        // Arrange: a seed with no server behind it.
        var deadEndpoint = ReserveDeadEndpoint();

        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Discover,
            Endpoints        = [deadEndpoint],
            Security         = KurrentClientSecurityOptions.Insecure,
            Gossip = new() {
                ReadPreference      = NodePreference.Leader,
                MaxDiscoverAttempts = 2,
                DiscoveryInterval   = TimeSpan.FromMilliseconds(50),
                Timeout             = TimeSpan.FromSeconds(1)
            }
        };
        options.Resilience.Retry.Enabled = false;

        await using var channel = ClusterChannel.Create(options);
        var client = new ProbeClient(channel.CallInvoker);

        // Act
        var failure = await Assert.ThrowsAsync<RpcException>(async () => {
            using var call = client.ReadAsync(new(), deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: cancellationToken);
            await call.ResponseAsync;
        });

        // Assert
        await Assert.That(failure!.StatusCode).IsEqualTo(StatusCode.Unavailable);
    }

    [Test]
    public async ValueTask connects_direct_without_gossip_when_scheme_is_direct(CancellationToken cancellationToken) {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader);
        var node         = cluster[0];
        var expectedPort = node.Endpoint.Port.ToString();

        var options = CreateOptions(cluster, NodePreference.Leader);
        options.ConnectionScheme = KurrentConnectionScheme.Direct;
        options.Endpoints        = [node.Endpoint];

        await using var channel = ClusterChannel.Create(options);
        var client = new ProbeClient(channel.CallInvoker);

        // Act
        var servingPort = await ReadServingPort(client, cancellationToken);

        // Assert
        await Assert.That(servingPort).IsEqualTo(expectedPort);
    }

    [Test]
    public async ValueTask sends_requires_leader_and_identity_headers(CancellationToken cancellationToken) {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower);
        var leader = cluster.NodeInState(NodeState.Leader);

        var options = CreateOptions(cluster, NodePreference.Leader);
        var expectedRequiresLeader = bool.TrueString;
        var expectedConnectionName = options.ConnectionName;

        await using var channel = ClusterChannel.Create(options);
        var client = new ProbeClient(channel.CallInvoker);

        // Act
        await ReadServingPort(client, cancellationToken);
        var headers = leader.LastRequestHeaders;

        // Assert
        await Assert.That(headers).IsNotNull();
        await Assert.That(headers!.GetValue("requires-leader")).IsEqualTo(expectedRequiresLeader);
        await Assert.That(headers.GetValue("connection-name")).IsEqualTo(expectedConnectionName);
        await Assert.That(headers.GetValue("kurrentdb.client.name")).IsNotNull();
    }

    [Test]
    public async ValueTask pre_warms_the_channel_with_connect_async(CancellationToken cancellationToken) {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower);

        await using var channel = ClusterChannel.Create(CreateOptions(cluster, NodePreference.Leader));

        // Act
        await channel.ConnectAsync(cancellationToken);

        // Assert
        await Assert.That(channel.Channel.State).IsEqualTo(ConnectivityState.Ready);
    }

    #region ->> Helpers <<-

    static KurrentClientOptions CreateOptions(FakeClusterHarness cluster, NodePreference preference) {
        var options = new KurrentClientOptions {
            ConnectionScheme = KurrentConnectionScheme.Discover,
            Endpoints        = cluster.Endpoints,
            Security         = KurrentClientSecurityOptions.Insecure,
            Gossip = new() {
                ReadPreference      = preference,
                MaxDiscoverAttempts = 3,
                DiscoveryInterval   = TimeSpan.FromMilliseconds(50),
                Timeout             = TimeSpan.FromSeconds(2)
            }
        };

        options.Resilience.Retry.Enabled = false;

        return options;
    }

    static async ValueTask<string?> ReadServingPort(ProbeClient client, CancellationToken cancellationToken) {
        using var call = client.ReadAsync(new(), cancellationToken: cancellationToken);
        await call.ResponseAsync;
        return call.GetTrailers().GetValue(ServingNodePortTrailer);
    }

    static ValueTask<string?> WaitUntilServedBy(ProbeClient client, string expectedPort, CancellationToken cancellationToken) =>
        WaitUntilServedByAny(client, [expectedPort], cancellationToken);

    static async ValueTask<string?> WaitUntilServedByAny(ProbeClient client, string[] expectedPorts, CancellationToken cancellationToken) {
        string? servingPort = null;

        while (!cancellationToken.IsCancellationRequested) {
            try {
                servingPort = await ReadServingPort(client, cancellationToken);
            }
            catch (RpcException) {
                // Routing has not converged yet; the call may still land on a stopped or
                // redirecting node.
            }

            if (servingPort is not null && expectedPorts.Contains(servingPort))
                return servingPort;

            await Task.Delay(50, cancellationToken);
        }

        return servingPort;
    }

    static DnsEndPoint ReserveDeadEndpoint() {
        // Bind an ephemeral port and release it: nothing listens there afterwards.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return new("127.0.0.1", port);
    }

    #endregion
}
