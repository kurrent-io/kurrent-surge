using Kurrent.Client.Streams;
using Kurrent.SDK.Testing.Fixtures;

namespace Kurrent.Client.Tests.Secure;

[Category("Secure")]
[Category("Integration")]
public class TransportTests {
    [ClassDataSource<KurrentDBSecureTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBSecureTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask appends_over_tls(CancellationToken ct) {
        var stream = $"secure-append-{Guid.NewGuid():N}";

        var req = AppendStreamRequest.For(stream)
            .WithRecord(new TestEvent("hello from TLS", DateTimeOffset.UtcNow))
            .ExpectingState(ExpectedStreamState.NoStream)
            .Build();

        var result = await Fixture.Client.Streams.AppendStream(req, ct);

        await Assert.That(result.Revision).IsEqualTo(0);
    }

    [Test]
    public async ValueTask reads_back_over_tls(CancellationToken ct) {
        var stream = $"secure-read-{Guid.NewGuid():N}";

        var appendReq = AppendStreamRequest.For(stream)
            .WithRecord(new TestEvent("event-1", DateTimeOffset.UtcNow))
            .WithRecord(new TestEvent("event-2", DateTimeOffset.UtcNow))
            .ExpectingState(ExpectedStreamState.NoStream)
            .Build();

        await Fixture.Client.Streams.AppendStream(appendReq, ct);

        var records = await Fixture.Client.Streams
            .ReadStreamRecords(stream, cancellationToken: ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        await Assert.That(records).Count().IsEqualTo(2);
    }

    record TestEvent(string Description, DateTimeOffset Timestamp);
}
