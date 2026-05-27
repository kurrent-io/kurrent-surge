using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Serilog;

namespace Kurrent.Client.Tests.Streams;

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
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var data = await Fixture.SeedTestStream(10, ct);
        var records = new List<Record>();

        // Act
        await foreach (var msg in Fixture.Client.Streams.Subscribe(
            RecordFilter.ByStream(data.Stream),
            RecordPosition.From(LogPosition.Earliest, data.Stream, Revision.Min),
            skipDecoding: true,
            ct: cts.Token
        )) {
            msg.Switch(
                connected => Log.Information("Connected {SessionId}", connected.SessionId),
                record => {
                    records.Add(record);

                    if (record.Position.LogPosition >= data.Position)
                        cts.Cancel();
                },
                checkpoint => { }
            );
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
        await foreach (var msg in Fixture.Client.Streams.Subscribe(
            RecordFilter.ByExpression($"stream LIKE '{data.Stream}%'"),
            RecordPosition.From(lastPosition.LogPosition),
            skipDecoding: true,
            ct: cts.Token
        )) {
            msg.Switch(
                connected => { },
                record => {
                    records.Add(record);

                    if (record.Position.LogPosition >= data.Position)
                        cts.Cancel();
                },
                checkpoint => { }
            );
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(data.Records.Length);
    }
    
    [Test]
    public async ValueTask consumes_index_records(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(50, ct);
        var records      = new List<Record>();

        //[50:19.716 INF] 00000000-0000-0000-0000-000000000000 (015) KurrentDB.Resources.kurrentdb-01 | 1079: "2026-05-22T16:06:53.5919980Z KurrentDB.Core.TransactionLog.Chunks.TFChunk.InvalidReadException: Error while reading log record forwards at actual position 80972856. Ticks must be between DateTime.MinValue.Ticks and DateTime.MaxValue.Ticks. (Parameter 'ticks')"
        
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        
        // Act
        var subscription = Fixture.Client.Streams.Subscribe(
            RecordFilter.ByExpression("category = 'race'"),
            RecordPosition.Earliest,
            // RecordPosition.From(lastPosition.LogPosition),
            // lastPosition, 
            skipDecoding: true,
            ct: cts.Token);
        
        await foreach (var msg in subscription) {
            if (msg.Value is Record record && record.Stream == data.Stream) {
                records.Add(record);

                if (records.Count == data.Records.Length)
                    await cts.CancelAsync();
            }
            
            // if (msg.Value is Record record && record.Position.LogPosition >= data.Position) {
            //     records.Add(record);
            //
            //     if (records.Count >= data.Records.Length)
            //         await cts.CancelAsync();
            // }
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask yields_connected_with_session_id(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(ct);
        Connected? connected = null;

        // Act
        await foreach (var msg in Fixture.Client.Streams.Subscribe(
            RecordFilter.ByStream(data.Stream),
            RecordPosition.From(LogPosition.Earliest, data.Stream, Revision.Min),
            skipDecoding: true,
            ct: ct
        )) {
            msg.Switch(
                c => connected = c,
                _ => { },
                _ => { }
            );

            break;
        }

        // Assert
        await Assert.That(connected).IsNotNull();
        await Assert.That(connected!.Value.SessionId.IsUnset).IsFalse();
    }

    [Test]
    public async ValueTask yields_caught_up_checkpoint_when_subscribing_to_stream(CancellationToken ct) {
        // Arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var data = await Fixture.SeedTestStream(5, ct);
        CaughtUpCheckpoint? caughtUp = null;
        var recordCount = 0;

        // Act
        await foreach (var msg in Fixture.Client.Streams.Subscribe(
            RecordFilter.ByStream(data.Stream),
            RecordPosition.From(LogPosition.Earliest, data.Stream, Revision.Min),
            skipDecoding: true,
            ct: cts.Token
        )) {
            msg.Switch(
                connected => { },
                record => recordCount++,
                checkpoint => {
                    if (checkpoint is CaughtUpCheckpoint cu) {
                        caughtUp = cu;
                        cts.Cancel();
                    }
                }
            );
        }

        // Assert
        await Assert.That(caughtUp).IsNotNull();
        await Assert.That(caughtUp!.Positions).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(caughtUp.Positions[0].Revision).IsNotEqualTo(Revision.Unset);
        await Assert.That(recordCount).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask yields_caught_up_checkpoint_when_subscribing_to_log(CancellationToken ct) {
        // Arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(5, ct);
        CaughtUpCheckpoint? caughtUp = null;

        // Act
        await foreach (var msg in Fixture.Client.Streams.Subscribe(
            RecordFilter.ByExpression($"stream LIKE '{data.Stream}%'"),
            RecordPosition.From(lastPosition.LogPosition),
            skipDecoding: true,
            ct: cts.Token
        )) {
            msg.Switch(
                connected => { },
                record => { },
                checkpoint => {
                    if (checkpoint is CaughtUpCheckpoint cu) {
                        caughtUp = cu;
                        cts.Cancel();
                    }
                }
            );
        }

        // Assert
        await Assert.That(caughtUp).IsNotNull();
        await Assert.That(caughtUp!.Positions).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(caughtUp.Positions[0].LogPosition).IsNotEqualTo(LogPosition.Unset);
    }

    [Test]
    public async ValueTask yields_interval_checkpoint_when_subscribing_to_log(CancellationToken ct) {
        // Arrange — small search window forces the server to emit interval checkpoints
        // after scanning just 10 records, guaranteeing at least one arrives before
        // all 50 seeded records are consumed.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(50, ct);
        IntervalCheckpoint? interval = null;

        // Act
        await foreach (var msg in Fixture.Client.Streams.Subscribe(
            RecordFilter.ByExpression($"stream LIKE '{data.Stream}%'"),
            RecordPosition.From(lastPosition.LogPosition),
            skipDecoding: true,
            maxSearchWindow: 10,
            ct: cts.Token
        )) {
            msg.Switch(
                connected => { },
                record => {
                    if (record.Position.LogPosition >= data.Position && interval is not null)
                        cts.Cancel();
                },
                checkpoint => {
                    if (checkpoint is IntervalCheckpoint ic)
                        interval = ic;
                }
            );
        }

        // Assert
        await Assert.That(interval).IsNotNull();
        await Assert.That(interval!.Positions).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(interval.Positions[0].LogPosition).IsNotEqualTo(LogPosition.Unset);
    }

    [Test]
    public async ValueTask subscribes_to_non_existing_stream_and_receives_records_after_creation(CancellationToken ct) {
        // Arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var data = await Fixture.TestData.GenerateSet(ct);
        var records = new List<Record>();

        var subscription = Fixture.Client.Streams.Subscribe(
            RecordFilter.ByStream(data.Stream),
            RecordPosition.From(LogPosition.Earliest, data.Stream, Revision.Min),
            skipDecoding: true,
            ct: cts.Token
        );

        // Act — append after subscription is established
        _ = Task.Run(async () => {
            await Task.Delay(500, ct);

            var req = AppendStreamRequest.For(data.Stream).AddRecords(data.Records).Build();
            await Fixture.Client.Streams.AppendStream(req, ct);
        }, ct);

        await foreach (var msg in subscription) {
            msg.Switch(
                connected => { },
                record => {
                    records.Add(record);

                    if (records.Count >= data.Records.Length)
                        cts.Cancel();
                },
                checkpoint => { }
            );
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
            await foreach (var _ in Fixture.Client.Streams.Subscribe(
                RecordFilter.ByStream(data.Stream),
                RecordPosition.From(LogPosition.Earliest, data.Stream, Revision.Min),
                skipDecoding: true,
                ct: ct
            )) { }
        }).Throws<StreamTombstonedException>();
    }

    [Test]
    public async ValueTask cancellation_stops_subscription(CancellationToken ct) {
        // Arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var data = await Fixture.SeedTestStream(10, ct);
        var recordCount = 0;

        // Act
        await foreach (var msg in Fixture.Client.Streams.Subscribe(
            RecordFilter.ByStream(data.Stream),
            RecordPosition.From(LogPosition.Earliest, data.Stream, Revision.Min),
            skipDecoding: true,
            ct: cts.Token
        )) {
            msg.Switch(
                connected => { },
                record => {
                    recordCount++;

                    if (recordCount >= 3)
                        cts.Cancel();
                },
                checkpoint => { }
            );
        }

        // Assert
        await Assert.That(recordCount).IsGreaterThanOrEqualTo(3);
        await Assert.That(recordCount).IsLessThan(data.Records.Length);
    }

    [Test]
    public async ValueTask applies_client_side_filter(CancellationToken ct) {
        // Arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var target = await Fixture.SeedTestStream(5, ct);
        var noise  = await Fixture.SeedTestStream(5, ct);

        var records = new List<Record>();

        // Act
        await foreach (var msg in Fixture.Client.Streams.Subscribe(
            RecordFilter.ByExpression($"stream LIKE '{target.Stream}%'"),
            RecordPosition.From(lastPosition.LogPosition),
            skipDecoding: true,
            ct: cts.Token
        )) {
            msg.Switch(
                connected => { },
                record => {
                    records.Add(record);

                    if (record.Position.LogPosition >= target.Position)
                        cts.Cancel();
                },
                checkpoint => { }
            );
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(target.Records.Length);
        await Assert.That(records.All(r => r.Position.Stream == target.Stream)).IsTrue();
    }
}
