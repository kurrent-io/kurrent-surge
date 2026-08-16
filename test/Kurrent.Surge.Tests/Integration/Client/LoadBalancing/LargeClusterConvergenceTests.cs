using Grpc.Core;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Testkit;

namespace Kurrent.Surge.Tests.Client.LoadBalancing;

[Category("Integration")]
public class LargeClusterConvergenceTests {
    [ClassDataSource<KurrentDBLargeClusterFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBLargeClusterFixture Fixture { get; [UsedImplicitly] init; }

    static EventuallyOptions ElectionRetries(CancellationToken ct) =>
        new EventuallyOptions {
                Timeout           = TimeSpan.FromSeconds(60),
                PollingInterval   = TimeSpan.FromMilliseconds(500),
                CancellationToken = ct
            }
            .RetryOn<NotLeaderNodeException>()
            .RetryOn<ServerNotReadyException>()
            .RetryOn<RpcException>()
            // The balancer disposes the old subchannel on a leader swap, which cancels in-flight
            // calls — expected turbulence while the cluster converges. The poll loop checks the
            // test's own token separately, so a real timeout still lands.
            .RetryOn<OperationCanceledException>();

    [Test]
    [Timeout(180_000)]
    public async ValueTask converges_after_two_consecutive_leader_elections(CancellationToken cancellationToken) {
        // Arrange — quorum is three of five, so the cluster survives losing a leader twice in a
        // row. Three nodes is the smallest topology that survives one; this is the smallest that
        // survives two.
        await using var client = Fixture.GetNewClient();

        var retries = ElectionRetries(cancellationToken);

        await Assert.That(async () => {
            var seeded = await Fixture.TestData.GenerateSet(cancellationToken);
            await client.Streams.AppendStream(seeded.Stream, seeded.Records, cancellationToken);
        }).EventuallyPasses(retries);

        // Act & Assert — resign twice, confirming writes land again after each election.
        for (var round = 0; round < 2; round++) {
            await client.Admin.ResignNode(cancellationToken);

            await Assert.That(async () => {
                var data = await Fixture.TestData.GenerateSet(cancellationToken);
                await client.Streams.AppendStream(data.Stream, data.Records, cancellationToken);
            }).EventuallyPasses(retries);
        }
    }

    [Test]
    [Timeout(180_000)]
    public async ValueTask reads_and_writes_round_trip_across_the_cluster(CancellationToken cancellationToken) {
        // Arrange
        await using var client = Fixture.GetNewClient();

        var seeded = await Fixture.TestData.GenerateSet(cancellationToken);

        await Assert.That(async () => {
            await client.Streams.AppendStream(seeded.Stream, seeded.Records, cancellationToken);
        }).EventuallyPasses(ElectionRetries(cancellationToken));

        var expectedCount = seeded.Records.Length;

        // Act
        var read = await client.Streams
            .ReadStream(seeded.Stream, cancellationToken)
            .Where(msg => msg.IsRecord)
            .ToListAsync(cancellationToken);

        // Assert
        await Assert.That(read).Count().IsEqualTo(expectedCount);
    }
}
