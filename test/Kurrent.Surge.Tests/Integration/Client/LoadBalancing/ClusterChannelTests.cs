using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Grpc.Core;
using Kurrent.Surge.Client.LoadBalancing;
using Kurrent.Surge.Tests.Client.LoadBalancing.FakeCluster;
using NodeState = Kurrent.Surge.Tests.Client.LoadBalancing.FakeCluster.Protocol.MemberInfo.Types.VNodeState;
using ProbeClient = EventStore.Client.Gossip.Gossip.GossipClient;

namespace Kurrent.Surge.Tests.Client.LoadBalancing;

[Category("Integration")]
[Timeout(30_000)]
public class ClusterChannelTests {
    const string ServingNodePortTrailer = "serving-node-port";

    [Test]
    public async ValueTask routes_calls_to_leader_when_leader_preferred(CancellationToken cancellationToken) {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower, NodeState.Follower);
        var expectedPort = cluster.NodeInState(NodeState.Leader).Endpoint.Port.ToString();

        await using var channel = ClusterChannel.Create(CreateOptions(cluster, NodeReadPreference.Leader));
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

        await using var channel = ClusterChannel.Create(CreateOptions(cluster, NodeReadPreference.Follower));
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

        await using var channel = ClusterChannel.Create(CreateOptions(cluster, NodeReadPreference.ReadOnlyReplica));
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

        var options = CreateOptions(cluster, NodeReadPreference.Leader);
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

        await using var channel = ClusterChannel.Create(CreateOptions(cluster, NodeReadPreference.Leader));
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

        await using var channel = ClusterChannel.Create(CreateOptions(cluster, NodeReadPreference.Leader));
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
                ReadPreference      = NodeReadPreference.Leader,
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

        var options = CreateOptions(cluster, NodeReadPreference.Leader);
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
    public async ValueTask sends_identity_headers_without_requires_leader(CancellationToken cancellationToken) {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower);
        var leader = cluster.NodeInState(NodeState.Leader);

        var options = CreateOptions(cluster, NodeReadPreference.Leader);
        var expectedConnectionName = options.ConnectionName;

        await using var channel = ClusterChannel.Create(options);
        var client = new ProbeClient(channel.CallInvoker);

        // Act
        await ReadServingPort(client, cancellationToken);
        var headers = leader.LastRequestHeaders;

        // Assert
        await Assert.That(headers).IsNotNull();
        await Assert.That(headers!.GetValue("connection-name")).IsEqualTo(expectedConnectionName);
        await Assert.That(headers.GetValue("kurrentdb.client.name")).IsNotNull();
        await Assert.That(headers.GetValue("requires-leader")).IsNull();
    }

    [Test]
    public async ValueTask falls_back_to_follower_when_preferred_replica_stops(CancellationToken cancellationToken) {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower, NodeState.ReadOnlyReplica);
        var replica      = cluster.NodeInState(NodeState.ReadOnlyReplica);
        var followerPort = cluster.NodeInState(NodeState.Follower).Endpoint.Port.ToString();

        await using var channel = ClusterChannel.Create(CreateOptions(cluster, NodeReadPreference.ReadOnlyReplica));
        var client = new ProbeClient(channel.CallInvoker);

        var initialPort = await ReadServingPort(client, cancellationToken);

        // Act: the replica dies; read offload falls back to the follower, not the leader.
        replica.IsAlive = false;
        await replica.StopAsync();
        var servingPort = await WaitUntilServedBy(client, followerPort, cancellationToken);

        // Assert
        await Assert.That(initialPort).IsEqualTo(replica.Endpoint.Port.ToString());
        await Assert.That(servingPort).IsEqualTo(followerPort);
    }

    [Test]
    public async ValueTask pre_warms_the_channel_with_connect_async(CancellationToken cancellationToken) {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower);

        await using var channel = ClusterChannel.Create(CreateOptions(cluster, NodeReadPreference.Leader));

        // Act
        await channel.ConnectAsync(cancellationToken);

        // Assert
        await Assert.That(channel.Channel.State).IsEqualTo(ConnectivityState.Ready);
    }

    [Test]
    public async ValueTask target_reports_connected_node_endpoint(CancellationToken cancellationToken) {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower, NodeState.Follower);
        var leader = cluster.NodeInState(NodeState.Leader);
        var expectedTarget = $"{leader.Endpoint.Host}:{leader.Endpoint.Port}";

        await using var channel = ClusterChannel.Create(CreateOptions(cluster, NodeReadPreference.Leader));
        var client = new ProbeClient(channel.CallInvoker);

        await channel.ConnectAsync(cancellationToken);
        await ReadServingPort(client, cancellationToken);

        // Act
        var target = channel.Target;

        // Assert
        await Assert.That(target).IsEqualTo(expectedTarget);
    }

    [Test]
    public async ValueTask sends_authorization_header_from_basic_credentials(CancellationToken cancellationToken) {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower);
        var leader = cluster.NodeInState(NodeState.Leader);

        const string username = "admin";
        const string password = "changeit";
        var expectedAuthorizationHeader = $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"))}";

        var options = CreateOptions(cluster, NodeReadPreference.Leader);
        options.Security.Authentication = new BasicCredentials(username, password);

        await using var channel = ClusterChannel.Create(options);
        var client = new ProbeClient(channel.CallInvoker);

        // Act
        await ReadServingPort(client, cancellationToken);
        var headers = leader.LastRequestHeaders;

        // Assert
        await Assert.That(headers).IsNotNull();
        await Assert.That(headers!.GetValue("authorization")).IsEqualTo(expectedAuthorizationHeader);
    }

    [Test]
    public async ValueTask redirect_loop_fails_calls_without_spinning(CancellationToken cancellationToken) {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower);
        var nodeA = cluster[0];
        var nodeB = cluster[1];

        await using var channel = ClusterChannel.Create(CreateOptions(cluster, NodeReadPreference.Leader));
        var client = new ProbeClient(channel.CallInvoker);

        await ReadServingPort(client, cancellationToken);

        nodeA.NotLeaderRedirect = nodeB.Endpoint;
        nodeB.NotLeaderRedirect = nodeA.Endpoint;

        var servedCallsBefore = nodeA.ServedCalls + nodeB.ServedCalls;
        var expectedStatusCodes = Enumerable.Repeat(StatusCode.FailedPrecondition, 5).ToArray();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var statusCodes = new List<StatusCode>();

        for (var i = 0; i < 5; i++) {
            var failure = await Assert.ThrowsAsync<RpcException>(async () => {
                using var call = client.ReadAsync(new(), cancellationToken: cancellationToken);
                await call.ResponseAsync;
            });

            statusCodes.Add(failure!.StatusCode);
        }

        stopwatch.Stop();
        var servedCallsAfter = nodeA.ServedCalls + nodeB.ServedCalls;

        // Assert
        await Assert.That(statusCodes).IsEquivalentTo(expectedStatusCodes);
        await Assert.That(stopwatch.ElapsedMilliseconds).IsLessThan(5000);
        await Assert.That(servedCallsAfter - servedCallsBefore).IsEqualTo(0L);
    }

    [Test]
    public async ValueTask presents_file_based_client_certificate(CancellationToken cancellationToken) {
        // Arrange
        using var tls           = FakeClusterTls.Create();
        var       certDirectory = Directory.CreateTempSubdirectory("cluster-channel-tls-");

        try {
            var caPath   = Path.Combine(certDirectory.FullName, "ca.pem");
            var certPath = Path.Combine(certDirectory.FullName, "client.pem");
            var keyPath  = Path.Combine(certDirectory.FullName, "client.key.pem");

            tls.ExportCaPem(caPath);
            tls.ExportClientPem(certPath, keyPath);

            var expectedThumbprint = tls.ClientCertificate.Thumbprint;

            await using var cluster = await FakeClusterHarness.StartSecure(tls, NodeState.Leader, NodeState.Follower);
            var leader = cluster.NodeInState(NodeState.Leader);

            var options = CreateOptions(cluster, NodeReadPreference.Leader);
            options.Security.Transport      = TransportSecurity.Certificate(caPath);
            options.Security.Authentication = ClientCredentials.Certificate(certPath, keyPath);

            await using var channel = ClusterChannel.Create(options);
            var client = new ProbeClient(channel.CallInvoker);

            // Act
            await ReadServingPort(client, cancellationToken);
            var presentedThumbprint = leader.LastClientCertificate?.Thumbprint;

            // Assert
            await Assert.That(presentedThumbprint).IsEqualTo(expectedThumbprint);
        }
        finally {
            certDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async ValueTask reads_stay_on_replica_while_appends_route_to_leader(CancellationToken cancellationToken) {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower, NodeState.ReadOnlyReplica);
        var expectedLeaderPort  = cluster.NodeInState(NodeState.Leader).Endpoint.Port.ToString();
        var expectedReplicaPort = cluster.NodeInState(NodeState.ReadOnlyReplica).Endpoint.Port.ToString();

        var options = CreateOptions(cluster, NodeReadPreference.ReadOnlyReplica);

        await using var channels = ClusterChannels.Create(options);
        var writeClient = new ProbeClient(channels.Write.CallInvoker);
        var readClient  = new ProbeClient(channels.Read.CallInvoker);

        // Act
        var writeServingPort = await ReadServingPort(writeClient, cancellationToken);
        var readServingPort  = await ReadServingPort(readClient, cancellationToken);

        // Assert
        await Assert.That(writeServingPort).IsEqualTo(expectedLeaderPort);
        await Assert.That(readServingPort).IsEqualTo(expectedReplicaPort);
    }

    [Test]
    public async ValueTask collapses_to_a_single_channel_when_read_preference_is_leader() {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader, NodeState.Follower);

        // Act
        await using var channels = ClusterChannels.Create(CreateOptions(cluster, NodeReadPreference.Leader));

        // Assert
        await Assert.That(channels.Read).IsSameReferenceAs(channels.Write);
    }

    [Test]
    public async ValueTask collapses_to_a_single_channel_when_scheme_is_direct() {
        // Arrange
        await using var cluster = await FakeClusterHarness.Start(NodeState.Leader);

        var options = CreateOptions(cluster, NodeReadPreference.Follower);
        options.ConnectionScheme = KurrentConnectionScheme.Direct;
        options.Endpoints        = [cluster[0].Endpoint];

        // Act
        await using var channels = ClusterChannels.Create(options);

        // Assert
        await Assert.That(channels.Read).IsSameReferenceAs(channels.Write);
    }

    #region ->> Helpers <<-

    static KurrentClientOptions CreateOptions(FakeClusterHarness cluster, NodeReadPreference preference) {
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
