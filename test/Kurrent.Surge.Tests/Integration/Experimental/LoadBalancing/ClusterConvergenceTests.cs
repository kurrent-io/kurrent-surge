using Grpc.Core;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Testkit;

namespace Kurrent.Surge.Tests.Experimental.LoadBalancing;

[Category("Integration")]
public class ClusterConvergenceTests {
    [ClassDataSource<KurrentDBClusterFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBClusterFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    [Timeout(120_000)]
    public async ValueTask converges_after_real_leader_election(CancellationToken cancellationToken) {
        // Arrange: the cluster fixture hands out a discovery connection string, so leader
        // routing rides gossip rather than redirect announcements alone.
        await using var client = Fixture.GetNewClient();

        var electionRetries = new EventuallyOptions {
                Timeout           = TimeSpan.FromSeconds(60),
                PollingInterval   = TimeSpan.FromMilliseconds(500),
                CancellationToken = cancellationToken
            }
            .RetryOn<NotLeaderNodeException>()
            .RetryOn<ServerNotReadyException>()
            .RetryOn<RpcException>()
            // The balancer disposes the old subchannel on a leader swap, which cancels
            // in-flight calls — expected turbulence while the cluster converges. The poll
            // loop checks the test's own token separately, so a real timeout still lands.
            .RetryOn<OperationCanceledException>();

        // The seed append tolerates the same window: on a cold cluster the first election
        // may still be settling and gossip can briefly rank a stale leader.
        await Assert.That(async () => {
            var seeded = await Fixture.TestData.GenerateSet(cancellationToken);
            await client.Streams.AppendStream(seeded.Stream, seeded.Records, cancellationToken);
        }).EventuallyPasses(electionRetries);

        // Act: resign the leader over the leader-pinned write channel; a real election follows.
        await client.Admin.ResignNode(cancellationToken);

        // Assert: appends converge on the post-election leader.
        await Assert.That(async () => {
            var ds = await Fixture.TestData.GenerateSet(cancellationToken);
            await client.Streams.AppendStream(ds.Stream, ds.Records, cancellationToken);
        }).EventuallyPasses(electionRetries);
    }
}
