using Kurrent.Client.Streams;
using Microsoft.Extensions.Logging;

namespace Kurrent.Client.Tests.Streams;

/// <summary>
/// The record-only projections. Both reads and subscriptions acknowledge the checkpoint protocol
/// via a handler — a client-filtered read synthesizes scan checkpoints just as a filtered
/// subscription does, and both route them through the handler so progress can never be dropped by
/// accident.
/// </summary>
[Category("Streams")]
[Category("Read")]
[Category("Integration")]
[Timeout(30_000)]
public class RecordsExtensionsTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask read_records_routes_checkpoints_to_handler(CancellationToken ct) {
        // Arrange — barren foreign records between the anchor and the target force the client-filtered
        // read to synthesize scan checkpoints (window = 1) before any record flows. The poisoned-OR
        // filter runs client-side (RequiresClientEvaluation); a server-routable filter would get no
        // read checkpoints at all (V1 read enumerators emit none).
        var anchor = await Fixture.LatestRecordPosition(ct);
        var barren = await Fixture.SeedTestStream(10, ct);
        var target = await Fixture.SeedTestStream(5, ct);

        var expectedCount = target.Records.Length;
        var checkpoints   = new List<Checkpoint>();

        // Act — Take(n) on the RECORD stream ends the read after the target's records
        var records = await Fixture.Client.Streams
            .ReadRecords(new ReadRecordsOptions {
                Start                 = anchor.LogPosition,
                Filter                = RecordFilter.ByExpression($"stream = '{target.Stream}' OR schema_name = 'no-such-schema'"),
                FilterMaxSearchWindow = 1,
                SkipDecoding          = true
            }, ct)
            .Records(checkpoints.Add, ct)
            .Take(expectedCount)
            .ToListAsync(ct);

        Fixture.Logger.LogInformation(
            "Projected {Records} records; handler received {Checkpoints} checkpoints",
            records.Count, checkpoints.Count);

        // Assert — records are the target's, and the scan progress reached the handler
        await Assert.That(records.Count).IsEqualTo(expectedCount);

        foreach (var record in records)
            await Assert.That(record.Stream).IsEqualTo(target.Stream);

        await Assert.That(checkpoints.Count).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async ValueTask read_records_supports_async_checkpoint_handler(CancellationToken ct) {
        // Arrange — same shape, asynchronous handler (the checkpoint-store persistence shape)
        var anchor = await Fixture.LatestRecordPosition(ct);
        var barren = await Fixture.SeedTestStream(10, ct);
        var target = await Fixture.SeedTestStream(5, ct);

        var expectedCount = target.Records.Length;
        var watermarks    = new List<LogPosition>();

        // Act
        var records = await Fixture.Client.Streams
            .ReadRecords(new ReadRecordsOptions {
                Start                 = anchor.LogPosition,
                Filter                = RecordFilter.ByExpression($"stream = '{target.Stream}' OR schema_name = 'no-such-schema'"),
                FilterMaxSearchWindow = 1,
                SkipDecoding          = true
            }, ct)
            .Records(checkpoint => {
                watermarks.Add(checkpoint.Positions[0].LogPosition);
                return ValueTask.CompletedTask;
            }, ct)
            .Take(expectedCount)
            .ToListAsync(ct);

        // Assert — handler ran, and watermarks never go backwards
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(watermarks.Count).IsGreaterThanOrEqualTo(1);

        for (var i = 1; i < watermarks.Count; i++)
            await Assert.That(watermarks[i].Value).IsGreaterThanOrEqualTo(watermarks[i - 1].Value);
    }

    [Test]
    public async ValueTask subscribe_records_routes_checkpoints_to_handler(CancellationToken ct) {
        // Arrange — barren foreign records between the anchor and the target stream force the
        // filtered subscription to emit scan checkpoints (window = 1) before any record flows.
        var anchor = await Fixture.LatestRecordPosition(ct);
        var barren = await Fixture.SeedTestStream(10, ct);
        var target = await Fixture.SeedTestStream(5, ct);

        var expectedCount = target.Records.Length;
        var checkpoints   = new List<Checkpoint>();

        // Act — Take(n) on the RECORD stream ends the live subscription deterministically
        var records = await Fixture.Client.Streams
            .Subscribe(new SubscribeOptions {
                Start           = anchor,
                Filter          = RecordFilter.ByExpression($"stream LIKE '{target.Stream}%'"),
                MaxSearchWindow = 1,
                SkipDecoding    = true
            }, ct)
            .Records(checkpoints.Add, ct)
            .Take(expectedCount)
            .ToListAsync(ct);

        Fixture.Logger.LogInformation(
            "Projected {Records} records; handler received {Checkpoints} checkpoints",
            records.Count, checkpoints.Count);

        // Assert — records are the target's, and the scan progress reached the handler
        await Assert.That(records.Count).IsEqualTo(expectedCount);

        foreach (var record in records)
            await Assert.That(record.Stream).IsEqualTo(target.Stream);

        await Assert.That(checkpoints.Count).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async ValueTask subscribe_records_supports_async_checkpoint_handler(CancellationToken ct) {
        // Arrange — same shape, asynchronous handler (the checkpoint-store persistence shape)
        var anchor = await Fixture.LatestRecordPosition(ct);
        var barren = await Fixture.SeedTestStream(10, ct);
        var target = await Fixture.SeedTestStream(5, ct);

        var expectedCount = target.Records.Length;
        var watermarks    = new List<LogPosition>();

        // Act
        var records = await Fixture.Client.Streams
            .Subscribe(new SubscribeOptions {
                Start           = anchor,
                Filter          = RecordFilter.ByExpression($"stream LIKE '{target.Stream}%'"),
                MaxSearchWindow = 1,
                SkipDecoding    = true
            }, ct)
            .Records(checkpoint => {
                watermarks.Add(checkpoint.Positions[0].LogPosition);
                return ValueTask.CompletedTask;
            }, ct)
            .Take(expectedCount)
            .ToListAsync(ct);

        // Assert — handler ran, and watermarks never go backwards
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(watermarks.Count).IsGreaterThanOrEqualTo(1);

        for (var i = 1; i < watermarks.Count; i++)
            await Assert.That(watermarks[i].Value).IsGreaterThanOrEqualTo(watermarks[i - 1].Value);
    }
}
