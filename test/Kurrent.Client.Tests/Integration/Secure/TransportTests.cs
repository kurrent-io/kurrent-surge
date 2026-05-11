using Kurrent.Client.Streams;
using Kurrent.Testkit.Aspire;

namespace Kurrent.Client.Tests.Secure;

[Category("Secure")]
[Category("Integration")]
public class TransportTests {
    [ClassDataSource<KurrentDBSecureFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBSecureFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask reads_back_over_tls(CancellationToken ct) {
        var stream = $"secure-read-{Guid.NewGuid():N}";

        var appendReq = AppendStreamRequest.For(stream)
            .WithMessage(new TestEvent("event-1", DateTimeOffset.UtcNow))
            .WithMessage(new TestEvent("event-2", DateTimeOffset.UtcNow))
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
