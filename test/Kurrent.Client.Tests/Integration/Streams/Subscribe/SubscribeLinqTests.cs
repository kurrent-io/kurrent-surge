using Kurrent.Client.Indexes;
using Kurrent.Client.Streams;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Testing.ArcadeRacer;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Subscribe")]
[Category("Integration")]
[Timeout(30_000)]
public class SubscribeLinqTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    IAsyncEnumerable<SubscribeMessage> SubscribeToStream(SeededTestStream data, CancellationToken ct) =>
        Fixture.Client.Streams.Subscribe(
            new() {
                Filter       = RecordFilter.ByStream(data.Stream),
                Start        = RecordPosition.Earliest,
                SkipDecoding = true
            },
            ct
        );

    IAsyncEnumerable<SubscribeMessage> SubscribeToLog(SeededTestStream data, RecordPosition after, CancellationToken ct) =>
        Fixture.Client.Streams.Subscribe(
            new() {
                Filter       = RecordFilter.ByExpression($"stream LIKE '{data.Stream}%'"),
                Start        = after,
                SkipDecoding = true
            },
            ct
        );

    IAsyncEnumerable<SubscribeMessage> SubscribeToCategory(RecordPosition after, CancellationToken ct) =>
        Fixture.Client.Streams.Subscribe(
            new() {
                Filter       = RecordFilter.ByCategory("race"),
                Start        = after,
                SkipDecoding = true
            },
            ct
        );

    IAsyncEnumerable<SubscribeMessage> SubscribeToUserIndex(string indexName, RecordPosition after, CancellationToken ct) =>
        Fixture.Client.Streams.Subscribe(
            new() {
                Filter       = RecordFilter.ByUserIndex(indexName),
                Start        = after,
                SkipDecoding = true
            },
            ct
        );

    #region ->> Stream Subscription <<-

    [Test]
    public async ValueTask stream_take(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        // Act
        var records = await SubscribeToStream(data, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .Take(5)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(5);
    }

    [Test]
    public async ValueTask stream_first_async(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        // Act
        var first = await SubscribeToStream(data, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .FirstAsync(ct);

        // Assert
        await Assert.That(first.Position.Stream).IsEqualTo(data.Stream);
        await Assert.That(first.Position.Revision).IsEqualTo(Revision.From(0));
    }

    [Test]
    public async ValueTask stream_take_while(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        // Act
        var records = await SubscribeToStream(data, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .TakeWhile(r => r.Position.Revision < Revision.From(5))
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(5);
        await Assert.That(records.Last().Position.Revision).IsEqualTo(Revision.From(4));
    }

    [Test]
    public async ValueTask stream_skip_and_take(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        // Act
        var records = await SubscribeToStream(data, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .Skip(3)
            .Take(4)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(4);
        await Assert.That(records.First().Position.Revision).IsEqualTo(Revision.From(3));
        await Assert.That(records.Last().Position.Revision).IsEqualTo(Revision.From(6));
    }

    [Test]
    public async ValueTask stream_skip_while(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        // Act
        var records = await SubscribeToStream(data, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .SkipWhile(r => r.Position.Revision < Revision.From(5))
            .Take(3)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(3);
        await Assert.That(records.First().Position.Revision).IsEqualTo(Revision.From(5));
    }

    [Test]
    public async ValueTask stream_count_async(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(7, ct);

        // Act
        var count = await SubscribeToStream(data, ct)
            .Where(msg => msg.IsRecord)
            .Take(data.Records.Length)
            .CountAsync(ct);

        // Assert
        await Assert.That(count).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask stream_any_async(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        // Act
        var hasRecords = await SubscribeToStream(data, ct)
            .Where(msg => msg.IsRecord)
            .AnyAsync(ct);

        // Assert
        await Assert.That(hasRecords).IsTrue();
    }

    [Test]
    public async ValueTask stream_all_async(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        // Act
        var allMatch = await SubscribeToStream(data, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .Take(data.Records.Length)
            .AllAsync(r => r.Position.Stream == data.Stream, ct);

        // Assert
        await Assert.That(allMatch).IsTrue();
    }

    [Test]
    public async ValueTask stream_select_projection(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        // Act
        var positions = await SubscribeToStream(data, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord.Position)
            .Take(data.Records.Length)
            .ToListAsync(ct);

        // Assert
        await Assert.That(positions.Count).IsEqualTo(data.Records.Length);
        await Assert.That(positions.All(p => p.Stream == data.Stream)).IsTrue();
    }

    [Test]
    public async ValueTask stream_aggregate_async(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        // Act
        var ids = await SubscribeToStream(data, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .Take(data.Records.Length)
            .AggregateAsync(
                new HashSet<Guid>(),
                (set, record) => { set.Add(record.Id); return set; },
                ct
            );

        // Assert
        await Assert.That(ids.Count).IsEqualTo(data.Records.Length);
    }

    #endregion

    #region ->> Log Subscription <<-

    [Test]
    public async ValueTask log_take(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(10, ct);

        // Act
        var records = await SubscribeToLog(data, lastPosition, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .Take(5)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(5);
    }

    [Test]
    public async ValueTask log_first_async(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(10, ct);

        // Act
        var first = await SubscribeToLog(data, lastPosition, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .FirstAsync(ct);

        // Assert
        await Assert.That(first.Position.Stream).IsEqualTo(data.Stream);
    }

    [Test]
    public async ValueTask log_take_while(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(10, ct);

        // Act
        var records = await SubscribeToLog(data, lastPosition, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .TakeWhile(r => r.Position.LogPosition < data.Position)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(data.Records.Length - 1);
    }

    [Test]
    public async ValueTask log_skip_and_take(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(10, ct);

        // Act
        var records = await SubscribeToLog(data, lastPosition, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .Skip(3)
            .Take(4)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(4);
    }

    [Test]
    public async ValueTask log_count_async(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(7, ct);

        // Act
        var count = await SubscribeToLog(data, lastPosition, ct)
            .Where(msg => msg.IsRecord)
            .Take(data.Records.Length)
            .CountAsync(ct);

        // Assert
        await Assert.That(count).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask log_aggregate_async(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(5, ct);

        // Act
        var ids = await SubscribeToLog(data, lastPosition, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .Take(data.Records.Length)
            .AggregateAsync(
                new HashSet<Guid>(),
                (set, record) => { set.Add(record.Id); return set; },
                ct
            );

        // Assert
        await Assert.That(ids.Count).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask log_skip_while(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(10, ct);

        // Act
        var records = await SubscribeToLog(data, lastPosition, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .SkipWhile(r => r.Position.LogPosition < data.Position)
            .Take(1)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(1);
        await Assert.That(records.First().Position.LogPosition).IsEqualTo(data.Position);
    }

    [Test]
    public async ValueTask log_any_async(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(5, ct);

        // Act
        var hasRecords = await SubscribeToLog(data, lastPosition, ct)
            .Where(msg => msg.IsRecord)
            .AnyAsync(ct);

        // Assert
        await Assert.That(hasRecords).IsTrue();
    }

    [Test]
    public async ValueTask log_all_async(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(5, ct);

        // Act
        var allMatch = await SubscribeToLog(data, lastPosition, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .Take(data.Records.Length)
            .AllAsync(r => r.Position.Stream == data.Stream, ct);

        // Assert
        await Assert.That(allMatch).IsTrue();
    }

    [Test]
    public async ValueTask log_select_projection(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(5, ct);

        // Act
        var positions = await SubscribeToLog(data, lastPosition, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord.Position)
            .Take(data.Records.Length)
            .ToListAsync(ct);

        // Assert
        await Assert.That(positions.Count).IsEqualTo(data.Records.Length);
        await Assert.That(positions.All(p => p.Stream == data.Stream)).IsTrue();
    }

    #endregion

    #region ->> Category Subscription <<-

    [Test]
    public async ValueTask category_take(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(10, ct);

        // Act — subscribe via the built-in $ce-race category index
        var records = await SubscribeToCategory(lastPosition, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .Take(5)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(5);
    }

    [Test]
    public async ValueTask category_aggregate(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(5, ct);

        // Act
        var streams = await SubscribeToCategory(lastPosition, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .Take(data.Records.Length)
            .AggregateAsync(
                new HashSet<string>(),
                (set, record) => { set.Add(record.Position.Stream); return set; },
                ct
            );

        // Assert — all records belong to race-* streams
        await Assert.That(streams.All(s => s.StartsWith("race-"))).IsTrue();
    }

    #endregion

    #region ->> User Index Subscription <<-

    /// <summary>
    /// User-index ingestion is asynchronous (DuckDB) and lags under parallel suite load —
    /// enough to blow the test timeout if the live subscription races it. Gate on a BOUNDED
    /// index read catching up to the seeded records first: reads terminate at the log end,
    /// so the probe retries cheaply until the index has the records, and only then does the
    /// subscription assertion run, deterministically.
    /// </summary>
    async ValueTask WaitForUserIndexCatchUp(string indexName, RecordPosition after, int expectedCount, CancellationToken ct) {
        var options = RetryOnTransients(ct);
        // A fresh user index ingests from the BEGINNING of the log — on the long-lived
        // shared container that is days of accumulated records, so catch-up time grows
        // with container age and degrades further under parallel suite load.
        options.Timeout         = TimeSpan.FromSeconds(45);
        options.PollingInterval = TimeSpan.FromMilliseconds(500);
        await Assert.That(async () => {
            var indexed = await Fixture.Client.Streams
                .ReadRecords(new() {
                    Start        = after.LogPosition,
                    Filter       = RecordFilter.ByUserIndex(indexName),
                    Limit        = expectedCount,
                    SkipDecoding = true
                }, ct)
                .Where(msg => msg.Value is Record)
                .CountAsync(ct);

            await Assert.That(indexed).IsEqualTo(expectedCount);
        }).EventuallyPasses(options);
    }

    [Test, Timeout(60_000)]
    public async ValueTask user_index_take(CancellationToken ct) {
        // Arrange — create a user index over LapCompleted events, then seed a full race so the
        // index has plenty of matching records. Seeding happens AFTER the index is started so the
        // records flow through it. The wire schema name is the type's full name (see MessageTypeMapper).
        var schemaName   = typeof(LapCompleted).FullName!;
        var indexName    = $"lap-completed-{Guid.NewGuid():N}";
        var lastPosition = await Fixture.LatestRecordPosition(ct);

        // The server currently allows at most one index field, and field names must be lowercase
        // (alphanumeric, underscores, dashes); the selector still uses the camelCase JSON path.
        await Fixture.Client.Indexes.Create(
            indexName,
            $"rec => rec.schema.name == '{schemaName}'",
            [new IndexField("car_id", "rec => rec.value.carId", IndexFieldType.String)],
            start: true,
            ct
        );

        await Fixture.SeedTestStream(ct);

        await WaitForUserIndexCatchUp(indexName, lastPosition, 5, ct);

        // Act
        var records = await SubscribeToUserIndex(indexName, lastPosition, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .Take(5)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(5);
    }

    [Test, Timeout(60_000)]
    public async ValueTask user_index_filters_to_indexed_schema(CancellationToken ct) {
        // Arrange — a user index scoped to LapCompleted must yield ONLY LapCompleted records,
        // unlike a category index which surfaces every event type in the category.
        var schemaName   = typeof(LapCompleted).FullName!;
        var indexName    = $"lap-completed-{Guid.NewGuid():N}";
        var lastPosition = await Fixture.LatestRecordPosition(ct);

        await Fixture.Client.Indexes.Create(
            indexName,
            $"rec => rec.schema.name == '{schemaName}'",
            [new IndexField("car_id", "rec => rec.value.carId", IndexFieldType.String)],
            start: true,
            ct
        );

        await Fixture.SeedTestStream(ct);

        await WaitForUserIndexCatchUp(indexName, lastPosition, 5, ct);

        // Act — collect the distinct schema names that flow through the user index
        var schemas = await SubscribeToUserIndex(indexName, lastPosition, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord.Schema.Schema.Value)
            .Take(5)
            .AggregateAsync(
                new HashSet<string>(),
                (set, name) => { set.Add(name); return set; },
                ct
            );

        // Assert — the index filter selected only the indexed schema
        await Assert.That(schemas.Count).IsEqualTo(1);
        await Assert.That(schemas).Contains(schemaName);
    }

    #endregion
}
