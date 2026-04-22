using Kurrent.Client.Streams;
using Kurrent.Sdk.Testkit.Fixtures;

namespace Kurrent.Client.Tests.Secure;

[Category("Secure")]
[Category("Integration")]
public class StreamingTests {
    [ClassDataSource<KurrentDBSecureTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBSecureTestFixture Fixture { get; [UsedImplicitly] init; }

    // Exercises long-lived HTTP/2 streaming over TLS. Enough records to span
    // multiple frames so a broken stream codepath wouldn't silently pass.
    [Test]
    public async ValueTask streams_many_records_over_tls(CancellationToken ct) {
        const int count = 50;

        var stream = $"secure-stream-{Guid.NewGuid():N}";

        var builder = AppendStreamRequest.For(stream).ExpectingState(ExpectedStreamState.NoStream);
        for (var i = 0; i < count; i++)
            builder = builder.WithRecord(new TestEvent(i, DateTimeOffset.UtcNow));

        await Fixture.Client.Streams.AppendStream(builder.Build(), ct);

        var records = await Fixture.Client.Streams
            .ReadStreamRecords(stream, cancellationToken: ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        await Assert.That(records).Count().IsEqualTo(count);
    }

    record TestEvent(int Sequence, DateTimeOffset Timestamp);
}
