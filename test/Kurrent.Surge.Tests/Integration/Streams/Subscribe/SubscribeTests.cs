using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.Logging;

namespace Kurrent.Surge.Tests.Streams;

[Category("Streams")]
[Category("Subscribe")]
[Category("Integration")]
[Timeout(30_000)]
public class SubscribeTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask consumes_stream_records(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(ct);

        var records = new List<Record>();

        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByStream(data.Stream),
            Start        = RecordPosition.Earliest,
            SkipDecoding = true
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        await foreach (var msg in Fixture.Client.Streams.Subscribe(options, cts.Token)) {
            if (msg.Value is Connected connected) {
                Fixture.Logger.LogInformation("Connected {SessionId}", connected.SessionId);
                continue;
            }

            if (msg.Value is not Record record)
                continue;

            records.Add(record);

            if (record.Position.LogPosition >= data.Position)
                await cts.CancelAsync();
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask consumes_log_records(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(ct);

        var records = new List<Record>();

        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByExpression($"stream LIKE '{data.Stream}%'"),
            Start        = lastPosition,
            SkipDecoding = true
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        await foreach (var msg in Fixture.Client.Streams.Subscribe(options, cts.Token)) {
            if (msg.Value is not Record record)
                continue;

            records.Add(record);

            if (record.Position.LogPosition >= data.Position)
                await cts.CancelAsync();
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask consumes_index_records(CancellationToken ct) {
        // Arrange — keep an explicit count: this scans the secondary index over $all, so a
        // larger, stable volume keeps the index path exercised regardless of the random default.
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(50, ct);

        var records = new List<Record>();

        // Known server-side flake: KurrentDB has thrown TFChunk.InvalidReadException
        // ("Error while reading log record forwards ... Ticks must be between DateTime.MinValue.Ticks
        // and DateTime.MaxValue.Ticks") at certain log positions while scanning the index.
        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByExpression("category = 'race'"),
            Start        = lastPosition,
            SkipDecoding = true
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        await foreach (var msg in Fixture.Client.Streams.Subscribe(options, cts.Token)) {
            if (msg.Value is not Record record || record.Stream != data.Stream)
                continue;

            records.Add(record);

            if (records.Count == data.Records.Length)
                await cts.CancelAsync();
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask yields_connected_with_session_id(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(ct);

        Connected? connected = null;

        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByStream(data.Stream),
            Start        = RecordPosition.Earliest,
            SkipDecoding = true
        };

        // Act
        await foreach (var msg in Fixture.Client.Streams.Subscribe(options, ct)) {
            if (msg.Value is not Connected c)
                continue;

            connected = c;
            break;
        }

        // Assert
        await Assert.That(connected).IsNotNull();
        await Assert.That(connected!.Value.SessionId.IsUnset).IsFalse();
    }

    [Test]
    public async ValueTask yields_caught_up_checkpoint_when_subscribing_to_stream(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(ct);

        CaughtUpCheckpoint? caughtUp = null;

        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByStream(data.Stream),
            Start        = RecordPosition.Earliest,
            SkipDecoding = true
        };
        
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        await foreach (var msg in Fixture.Client.Streams.Subscribe(options, cts.Token)) {
            if (msg.Value is not CaughtUpCheckpoint cu)
                continue;

            caughtUp = cu;
            await cts.CancelAsync();
        }

        // Assert
        await Assert.That(caughtUp).IsNotNull();
        await Assert.That(caughtUp.Positions).HasSingleItem();

        var pos = caughtUp.Positions[0];
        await Assert.That(pos.LogPosition).IsNotEqualTo(LogPosition.Unset);
        await Assert.That(pos.Stream).IsEqualTo(data.Stream);
        await Assert.That(pos.Revision).IsEqualTo(data.Revision);
    }

    [Test]
    public async ValueTask yields_caught_up_checkpoint_when_subscribing_to_log(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(ct);
        
        CaughtUpCheckpoint? caughtUp = null;

        var options = new SubscribeOptions {
            Start        = RecordPosition.From(lastPosition.LogPosition),
            SkipDecoding = true
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        
        // Act
        await foreach (var msg in Fixture.Client.Streams.Subscribe(options, cts.Token)) {
            if (msg.Value is not CaughtUpCheckpoint cu)
                continue;

            caughtUp = cu;
            await cts.CancelAsync();
        }

        // Assert
        await Assert.That(caughtUp).IsNotNull();
        await Assert.That(caughtUp.Positions).HasSingleItem();

        var pos = caughtUp.Positions[0];
        await Assert.That(pos.LogPosition).IsNotEqualTo(LogPosition.Unset);
        await Assert.That(pos.Stream).IsEqualTo(StreamName.Unset);
        await Assert.That(pos.Revision).IsEqualTo(Revision.Unset);
    }

    [Test]
    public async ValueTask yields_filter_checkpoint_when_subscribing_to_log(CancellationToken ct) {
        // Arrange — a small search window forces the server to emit filter checkpoints after
        // scanning just 10 records; the explicit 50-record seed guarantees at least one
        // checkpoint arrives before all matching records are consumed.
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(50, ct);

        FilterCheckpoint? filter = null;

        var options = new SubscribeOptions {
            Filter          = RecordFilter.ByExpression($"stream LIKE '{data.Stream}%'"),
            Start           = lastPosition,
            SkipDecoding    = true,
            MaxSearchWindow = 10
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        await foreach (var msg in Fixture.Client.Streams.Subscribe(options, cts.Token)) {
            if (msg.Value is FilterCheckpoint fc) {
                filter = fc;
                continue;
            }

            if (msg.Value is Record record && record.Position.LogPosition >= data.Position && filter is not null)
                await cts.CancelAsync();
        }

        // Assert
        await Assert.That(filter).IsNotNull();
        await Assert.That(filter!.Positions).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(filter.Positions[0].LogPosition).IsNotEqualTo(LogPosition.Unset);
    }

    [Test]
    public async ValueTask subscribes_to_non_existing_stream_and_receives_records_after_creation(CancellationToken ct) {
        // Arrange — generate but do not seed: the stream must not exist when the subscription starts.
        var data = await Fixture.TestData.GenerateSet(ct);

        var records = new List<Record>();

        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByStream(data.Stream),
            Start        = RecordPosition.Earliest,
            SkipDecoding = true
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var subscription = Fixture.Client.Streams.Subscribe(options, cts.Token);

        // Act — append after the subscription is established
        _ = Task.Run(async () => {
            await Task.Delay(500, ct);

            await Fixture.Client.Streams.AppendStream(data.Stream, data.Records, ct);
        }, ct);

        await foreach (var msg in subscription) {
            if (msg.Value is not Record record)
                continue;

            records.Add(record);

            if (records.Count >= data.Records.Length)
                await cts.CancelAsync();
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(data.Records.Length);
    }
    
    [Test]
    public async ValueTask throws_stream_tombstoned_for_tombstoned_stream(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(ct);
        await Fixture.Client.Streams.Tombstone(data.Stream, ct);

        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByStream(data.Stream),
            Start        = RecordPosition.Earliest,
            SkipDecoding = true
        };

        // Act & Assert
        await Assert.That(async () => {
            await foreach (var _ in Fixture.Client.Streams.Subscribe(options, ct)) { }
        }).Throws<StreamTombstonedException>();
    }

    [Test]
    public async ValueTask cancellation_stops_subscription(CancellationToken ct) {
        // Arrange — seed more than the cancel threshold so we can prove the subscription
        // stopped early instead of draining the whole stream.
        var data = await Fixture.SeedTestStream(10, ct);

        var recordCount = 0;

        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByStream(data.Stream),
            Start        = RecordPosition.Earliest,
            SkipDecoding = true
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        await foreach (var msg in Fixture.Client.Streams.Subscribe(options, cts.Token)) {
            if (msg.Value is not Record)
                continue;

            recordCount++;

            if (recordCount >= 3)
                await cts.CancelAsync();
        }

        // Assert
        await Assert.That(recordCount).IsGreaterThanOrEqualTo(3);
        await Assert.That(recordCount).IsLessThan(data.Records.Length);
    }

    [Test]
    public async ValueTask applies_client_side_filter(CancellationToken ct) {
        // Arrange — seed a target stream plus a noise stream the server-side filter must exclude.
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var target       = await Fixture.SeedTestStream(ct);
        var noise        = await Fixture.SeedTestStream(ct);

        var records = new List<Record>();

        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByExpression($"stream LIKE '{target.Stream}%'"),
            Start        = lastPosition,
            SkipDecoding = true
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        await foreach (var msg in Fixture.Client.Streams.Subscribe(options, cts.Token)) {
            if (msg.Value is not Record record)
                continue;

            records.Add(record);

            if (record.Position.LogPosition >= target.Position)
                await cts.CancelAsync();
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(target.Records.Length);
        await Assert.That(records.All(r => r.Position.Stream == target.Stream)).IsTrue();
    }
}
