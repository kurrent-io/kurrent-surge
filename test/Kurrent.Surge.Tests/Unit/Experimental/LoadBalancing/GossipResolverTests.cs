using System.Threading.Channels;
using Grpc.Core;
using Grpc.Net.Client.Balancer;
using Kurrent.Surge.Client.LoadBalancing;
using KurrentDB.Client;
using Microsoft.Extensions.Logging.Abstractions;
using IGossipClient = Kurrent.Surge.Client.LoadBalancing.IGossipClient;

namespace Kurrent.Surge.Tests.Client.LoadBalancing;

public class GossipResolverTests {
    static readonly ClusterEndpoint Seed     = new("seed", 1113);
    static readonly ClusterEndpoint Leader   = new("leader", 2113);
    static readonly ClusterEndpoint Follower = new("follower", 3113);
    static readonly ClusterEndpoint Replica  = new("replica", 4113);

    static readonly ClusterTopology Topology = new([
        new(NodeRole.Follower, IsAlive: true, Follower),
        new(NodeRole.Leader, IsAlive: true, Leader),
        new(NodeRole.Replica, IsAlive: true, Replica)
    ]);

    [Test]
    public async ValueTask publishes_addresses_ranked_by_preference(CancellationToken cancellationToken) {
        // Arrange
        var gossip = A.Fake<IGossipClient>();
        A.CallTo(() => gossip.GetTopology(A<ClusterEndpoint>._, A<CancellationToken>._)).Returns(Topology);

        using var resolver = CreateResolver(new() { Seeds = [Seed], NodeReadPreference = NodeReadPreference.Leader }, gossip);
        var results = StartCapturing(resolver);

        // Act
        resolver.Refresh();
        var result = await results.ReadAsync(cancellationToken);

        // Assert
        await Assert.That(result.Addresses).IsNotNull();
        await Assert.That(result.Addresses![0].EndPoint.Host).IsEqualTo(Leader.Host);
        await Assert.That(RankOf(result.Addresses[0])).IsEqualTo(0);
        await Assert.That(result.Addresses.Select(RankOf).Order().SequenceEqual([0, 1, 2])).IsTrue();
    }

    [Test]
    public async ValueTask tries_next_candidate_when_gossip_fails(CancellationToken cancellationToken) {
        // Arrange: two seeds, one of them unreachable.
        var deadSeed = new ClusterEndpoint("dead-seed", 1114);
        var gossip   = A.Fake<IGossipClient>();

        A.CallTo(() => gossip.GetTopology(deadSeed, A<CancellationToken>._)).Throws(new RpcException(new(StatusCode.Unavailable, "unreachable")));
        A.CallTo(() => gossip.GetTopology(Seed, A<CancellationToken>._)).Returns(Topology);

        using var resolver = CreateResolver(new() { Seeds = [deadSeed, Seed], NodeReadPreference = NodeReadPreference.Leader }, gossip);
        var results = StartCapturing(resolver);

        // Act
        resolver.Refresh();
        var result = await results.ReadAsync(cancellationToken);

        // Assert
        await Assert.That(result.Addresses).IsNotNull();
        await Assert.That(result.Addresses![0].EndPoint.Host).IsEqualTo(Leader.Host);
    }

    [Test]
    public async ValueTask publishes_failure_after_discovery_exhausts_attempts(CancellationToken cancellationToken) {
        // Arrange
        var gossip = A.Fake<IGossipClient>();
        A.CallTo(() => gossip.GetTopology(A<ClusterEndpoint>._, A<CancellationToken>._)).Throws(new RpcException(new(StatusCode.Unavailable, "unreachable")));

        using var resolver = CreateResolver(
            new() {
                Seeds               = [Seed],
                NodeReadPreference      = NodeReadPreference.Leader,
                MaxDiscoverAttempts = 2,
                DiscoveryInterval   = TimeSpan.FromMilliseconds(10)
            },
            gossip
        );

        var results = StartCapturing(resolver);

        // Act
        resolver.Refresh();
        var result = await results.ReadAsync(cancellationToken);

        // Assert: one gossip probe per attempt for the single seed.
        await Assert.That(result.Status.StatusCode).IsEqualTo(StatusCode.Unavailable);
        A.CallTo(() => gossip.GetTopology(A<ClusterEndpoint>._, A<CancellationToken>._)).MustHaveHappenedTwiceOrMore();
    }

    [Test]
    public async ValueTask announce_leader_publishes_hinted_endpoint_first_without_gossip(CancellationToken cancellationToken) {
        // Arrange
        var gossip = A.Fake<IGossipClient>();
        A.CallTo(() => gossip.GetTopology(A<ClusterEndpoint>._, A<CancellationToken>._)).Returns(Topology);

        using var resolver = CreateResolver(new() { Seeds = [Seed], NodeReadPreference = NodeReadPreference.Leader }, gossip);
        var results = StartCapturing(resolver);

        resolver.Refresh();
        await results.ReadAsync(cancellationToken);

        // Act: a not-leader redirect points at the replica endpoint.
        resolver.AnnounceLeader(Replica);
        var result = await results.ReadAsync(cancellationToken);

        // Assert: hinted endpoint ranks first, no extra gossip round-trip happened.
        await Assert.That(result.Addresses).IsNotNull();
        await Assert.That(result.Addresses![0].EndPoint.Host).IsEqualTo(Replica.Host);
        await Assert.That(RankOf(result.Addresses[0])).IsEqualTo(0);
        A.CallTo(() => gossip.GetTopology(A<ClusterEndpoint>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Test]
    [Timeout(5_000)]
    public async ValueTask announced_leader_survives_a_pass_already_in_flight(CancellationToken cancellationToken) {
        // Arrange: gossip blocks until released, so AnnounceLeader lands while a resolve
        // pass is still running. PollingResolver.Refresh() is a documented no-op whenever
        // a pass hasn't completed yet, so the announcement's own Refresh() call is dropped.
        var gossipEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gossipGate     = new TaskCompletionSource<ClusterTopology>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gossip         = A.Fake<IGossipClient>();

        A.CallTo(() => gossip.GetTopology(A<ClusterEndpoint>._, A<CancellationToken>._))
            .ReturnsLazily(() => {
                gossipEntered.TrySetResult();
                return new ValueTask<ClusterTopology>(gossipGate.Task);
            });

        using var resolver = CreateResolver(new() { Seeds = [Seed], NodeReadPreference = NodeReadPreference.Leader }, gossip);
        var results = StartCapturing(resolver);

        // Act: start a pass, wait until it is genuinely in flight (blocked on gossip),
        // announce a leader while it's running, then let the pass complete.
        resolver.Refresh();
        await gossipEntered.Task;

        resolver.AnnounceLeader(Replica);
        gossipGate.SetResult(Topology);

        var fromDiscovery = await results.ReadAsync(cancellationToken);
        var fromAnnounce  = await results.ReadAsync(cancellationToken);

        // Assert: the in-flight pass still publishes its own (stale) gossip view first,
        // but the dropped announcement is not lost — it catches up right after instead of
        // waiting for an external trigger that may never come.
        await Assert.That(fromDiscovery.Addresses![0].EndPoint.Host).IsEqualTo(Leader.Host);
        await Assert.That(fromAnnounce.Addresses![0].EndPoint.Host).IsEqualTo(Replica.Host);
        await Assert.That(RankOf(fromAnnounce.Addresses[0])).IsEqualTo(0);
    }

    [Test]
    public async ValueTask queries_discovered_members_instead_of_seeds_after_first_success(CancellationToken cancellationToken) {
        // Arrange: the seed is not a cluster member; discovery must move on to members.
        var gossip = A.Fake<IGossipClient>();
        A.CallTo(() => gossip.GetTopology(A<ClusterEndpoint>._, A<CancellationToken>._)).Returns(Topology);

        using var resolver = CreateResolver(new() { Seeds = [Seed], NodeReadPreference = NodeReadPreference.Leader }, gossip);
        var results = StartCapturing(resolver);

        resolver.Refresh();
        await results.ReadAsync(cancellationToken);

        // Act
        resolver.Refresh();
        await results.ReadAsync(cancellationToken);

        // Assert: the seed was only queried during the first discovery round.
        A.CallTo(() => gossip.GetTopology(Seed, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => gossip.GetTopology(A<ClusterEndpoint>.That.Matches(endpoint => endpoint != Seed), A<CancellationToken>._))
            .MustHaveHappenedOnceOrMore();
    }

    [Test]
    public async ValueTask publishes_seeds_verbatim_when_gossip_is_disabled(CancellationToken cancellationToken) {
        // Arrange: direct single-node mode.
        var gossip = A.Fake<IGossipClient>();

        using var resolver = CreateResolver(new() { Seeds = [Seed], DiscoveryEnabled = false }, gossip);
        var results = StartCapturing(resolver);

        // Act
        resolver.Refresh();
        var result = await results.ReadAsync(cancellationToken);

        // Assert
        await Assert.That(result.Addresses).IsNotNull();
        await Assert.That(result.Addresses![0].EndPoint.Host).IsEqualTo(Seed.Host);
        A.CallTo(gossip).MustNotHaveHappened();
    }

    static GossipResolver CreateResolver(GossipResolverOptions options, IGossipClient gossip) =>
        new(options, gossip, NullLoggerFactory.Instance);

    static ChannelReader<ResolverResult> StartCapturing(GossipResolver resolver) {
        var results = Channel.CreateUnbounded<ResolverResult>();
        resolver.Start(result => results.Writer.TryWrite(result));
        return results.Reader;
    }

    static int RankOf(BalancerAddress address) =>
        address.Attributes.TryGetValue(GossipResolver.RankKey, out var rank) ? rank : int.MaxValue;
}
