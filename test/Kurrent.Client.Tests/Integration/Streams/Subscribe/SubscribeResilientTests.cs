using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Subscribe")]
[Category("Integration")]
[Timeout(30_000)]
public class SubscribeResilientTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask consumes_stream_records(CancellationToken ct) {
        // Arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var data = await Fixture.SeedTestStream(10, ct);
        var records = new List<Record>();

        // Act
        await foreach (var msg in Fixture.Client.Streams.SubscribeWithResilience(
            RecordFilter.ByStream(data.Stream),
            RecordPosition.From(LogPosition.Earliest, data.Stream, Revision.Min),
            skipDecoding: true,
            ct: cts.Token
        )) {
            if (msg.Value is Record record) {
                records.Add(record);

                if (record.Position.LogPosition >= data.Position)
                    cts.Cancel();
            }
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask consumes_log_records(CancellationToken ct) {
        // Arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(10, ct);
        var records = new List<Record>();

        // Act
        await foreach (var msg in Fixture.Client.Streams.SubscribeWithResilience(
            RecordFilter.ByExpression($"stream LIKE '{data.Stream}%'"),
            RecordPosition.From(lastPosition.LogPosition),
            skipDecoding: true,
            ct: cts.Token
        )) {
            if (msg.Value is Record record) {
                records.Add(record);

                if (record.Position.LogPosition >= data.Position)
                    cts.Cancel();
            }
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask throws_stream_tombstoned_for_tombstoned_stream(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(ct);
        await Fixture.Client.Streams.Tombstone(data.Stream, ct);

        // Act & Assert
        await Assert.That(async () => {
            await foreach (var _ in Fixture.Client.Streams.SubscribeWithResilience(
                RecordFilter.ByStream(data.Stream),
                RecordPosition.From(LogPosition.Earliest, data.Stream, Revision.Min),
                skipDecoding: true,
                ct: ct
            )) { }
        }).Throws<StreamTombstonedException>();
    }

    [Test]
    public async ValueTask cancellation_stops_subscription_cleanly(CancellationToken ct) {
        // Arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var data = await Fixture.SeedTestStream(10, ct);
        var recordCount = 0;

        // Act
        await foreach (var msg in Fixture.Client.Streams.SubscribeWithResilience(
            RecordFilter.ByStream(data.Stream),
            RecordPosition.From(LogPosition.Earliest, data.Stream, Revision.Min),
            skipDecoding: true,
            ct: cts.Token
        )) {
            if (msg.Value is Record) {
                recordCount++;

                if (recordCount >= 3)
                    cts.Cancel();
            }
        }

        // Assert — stopped early, no exception thrown
        await Assert.That(recordCount).IsGreaterThanOrEqualTo(3);
        await Assert.That(recordCount).IsLessThan(data.Records.Length);
    }
}
