using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Tests.Secure;

[Category("Secure")]
[Category("Integration")]
public class TransportTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask reads_back_over_tls(CancellationToken ct) {
        var stream = $"secure-read-{Guid.NewGuid():N}";

        var appendRecords = new[] {
            AppendRecord.Create(new TestEvent("event-1", DateTimeOffset.UtcNow), r => r with { Stream = stream }),
            AppendRecord.Create(new TestEvent("event-2", DateTimeOffset.UtcNow), r => r with { Stream = stream })
        };

        await Fixture.Client.Streams.AppendStream(stream, appendRecords, ExpectedStreamState.NoStream, ct);

        var records = await Fixture.Client.Streams
            .ReadStream(stream, cancellationToken: ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        await Assert.That(records).Count().IsEqualTo(2);
    }

    record TestEvent(string Description, DateTimeOffset Timestamp);
}
