using Kurrent.Client.Streams;
using Kurrent.Sdk.Testkit.Fixtures;

namespace Kurrent.Client.Tests.Secure;

[Category("Secure")]
[Category("Integration")]
[Category("Cluster")]
public class ClusterTests {
    [ClassDataSource<KurrentDBSecureClusterTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBSecureClusterTestFixture Fixture { get; [UsedImplicitly] init; }

    // Appending requires a leader. If gossip or TLS handshake between nodes
    // is broken, the client never resolves a leader and this call hangs/fails.
    [Test]
    public async ValueTask append_then_read_roundtrips_through_cluster(CancellationToken ct) {
        var stream = $"cluster-roundtrip-{Guid.NewGuid():N}";

        var req = AppendStreamRequest.For(stream)
            .WithRecord(new TestEvent("hello cluster"))
            .ExpectingState(ExpectedStreamState.NoStream)
            .Build();

        await Fixture.Client.Streams.AppendStream(req, ct);

        var records = await Fixture.Client.Streams
            .ReadStreamRecords(stream, cancellationToken: ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        await Assert.That(records).Count().IsEqualTo(1);
    }

    record TestEvent(string Description);
}
