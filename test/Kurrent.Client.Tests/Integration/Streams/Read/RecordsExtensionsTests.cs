using Kurrent.Client.Streams;
using Microsoft.Extensions.Logging;

namespace Kurrent.Client.Tests.Streams;

/// <summary>
/// The record-only projections: reads project losslessly (V1 reads carry no filter
/// checkpoints), subscriptions must acknowledge the checkpoint protocol via a handler —
/// progress can never be dropped by accident.
/// </summary>
[Category("Streams")]
[Category("Read")]
[Category("Integration")]
[Timeout(30_000)]
public class RecordsExtensionsTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask read_records_projects_only_records(CancellationToken ct) {
        // Arrange
        var anchor     = await Fixture.LatestRecordPosition(ct);
        var simulation = await Fixture.SeedTestStream(5, ct);

        var expectedCount = simulation.Records.Length;

        // Act
        var records = await Fixture.Client.Streams
            .ReadRecords(new ReadRecordsOptions {
                Start        = anchor.LogPosition,
                Filter       = RecordFilter.ByExpression($"stream = '{simulation.Stream}'"),
                SkipDecoding = true
            }, ct)
            .Records(ct)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(expectedCount);

        foreach (var record in records)
            await Assert.That(record.Stream).IsEqualTo(simulation.Stream);
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
