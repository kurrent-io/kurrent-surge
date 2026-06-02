using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Secure;

[Category("Secure")]
[Category("Integration")]
public class TransportTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask reads_back_over_tls(CancellationToken ct) {
        var stream = $"secure-read-{Guid.NewGuid():N}";

        var appendRecords = new[] {
            AppendRecord.New().Stream(stream).Value(new TestEvent("event-1", DateTimeOffset.UtcNow)).Build(),
            AppendRecord.New().Stream(stream).Value(new TestEvent("event-2", DateTimeOffset.UtcNow)).Build()
        };

        await Fixture.Client.Streams.AppendStream(stream, appendRecords, ExpectedStreamState.NoStream, ct);

        var records = await Fixture.Client.Streams
            .ReadStreamRecords(stream, cancellationToken: ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        await Assert.That(records).Count().IsEqualTo(2);
    }

    record TestEvent(string Description, DateTimeOffset Timestamp);
}
