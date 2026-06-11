using Kurrent.Client.Streams;
using Serilog;

namespace Kurrent.Client.Tests.Streams;


public static class Extensions {

    public static IAsyncEnumerable<Record> Records(this IAsyncEnumerable<ReadRecordsResult> source) =>
        source.Where(msg => msg.Value is Record).Select(msg => msg.AsRecord);
    
    public static IAsyncEnumerable<Record> TakeRecordsUpTo(this IAsyncEnumerable<ReadRecordsResult> source, RecordPosition position) =>
        source.Where(msg => msg.Value is Record).Select(msg => msg.AsRecord)
            .TakeWhile(rec => rec.Position.LogPosition <= position.LogPosition);
    
    public static IAsyncEnumerable<Record> TakeUntilCaughtUp(this IAsyncEnumerable<ReadRecordsResult> source) =>
        source
            .TakeWhile(msg => msg.Value is not CaughtUpCheckpoint)
            .Select(msg => msg.AsRecord);
}

[Category("Streams")]
[Category("Read")]
[Category("Filters")]
[Category("Integration")]
[Timeout(30_000)]
public class ReadWithFilterTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    // Secondary indexes are eventually consistent; observed catch-up lag has exceeded the
    // 5s the old hand-rolled poll loops allowed, so the budget is 15s at the same interval.
    static EventuallyOptions IndexCatchUp(CancellationToken ct) => new() {
        Timeout           = TimeSpan.FromSeconds(15),
        PollingInterval   = TimeSpan.FromMilliseconds(500),
        CancellationToken = ct
    };

    #region . Stream Filter .

    [Test]
    public async ValueTask reads_log_filtered_by_stream(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var simulation   = await Fixture.SeedTestStream(ct);

        // Act
        var options = new ReadRecordsOptions {
            Start        = lastPosition.LogPosition,
            Filter       = RecordFilter.ByExpression($"stream = '{simulation.Stream}'"),
            Limit        = simulation.Records.Length,
            SkipDecoding = true
        };

        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(simulation.Records.Length);

        foreach (var record in records)
            await Assert.That(record.Stream).IsEqualTo(simulation.Stream);
    }

    [Test]
    public async ValueTask reads_log_filtered_by_stream_pattern(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var simulation   = await Fixture.SeedTestStream(ct);
        var category     = simulation.Stream.Category;

        // Act
        var options = new ReadRecordsOptions {
            Start        = lastPosition.LogPosition,
            Filter       = RecordFilter.ByExpression($"stream LIKE '{category}-%'"),
            Limit        = simulation.Records.Length,
            SkipDecoding = true
        };

        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(simulation.Records.Length);

        foreach (var record in records)
            await Assert.That(record.Stream.Value).StartsWith($"{category}-");
    }
    
    
    // Start semantics are pinned by StartSemanticsTests ($all reads are INCLUSIVE of the record
    // at the position — server contract) and honored across filter re-routing by StartRoutingTests.
    [Test]
    // [Skip("LINQ .Take() over IAsyncEnumerable does not cancel the underlying gRPC server stream; need to figure out the right pattern for limiting a filtered $all read client-side.")]
    public async ValueTask reads_log_filtered_by_stream_pattern_using_take_instead_of_max_records(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var simulation   = await Fixture.SeedTestStream(ct);

        // The stream name minus its last character: still a real LIKE prefix match, but
        // unique by construction — a category prefix would match every concurrently
        // seeded stream of the same category on the shared container.
        var streamPrefix = ((string)simulation.Stream)[..^1];

        var limit = simulation.Records.Length + Random.Shared.Next(100, 1000);

        // Act
        var options = new ReadRecordsOptions {
            Start        = lastPosition.LogPosition,
            Filter       = RecordFilter.ByExpression($"stream LIKE '{streamPrefix}%'"),
            SkipDecoding = true
        };

        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, ct).Take(limit).WithCancellation(ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(simulation.Records.Length);
        foreach (var record in records)
            await Assert.That(simulation.Records).Contains(x => x.RecordId == record.Id);
           
    }

    #endregion

    #region . Category Index .

    [Test]
    public async ValueTask reads_log_filtered_by_category(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var simulation   = await Fixture.SeedTestStream(ct);
        var category     = simulation.Stream.Category;

        // Act — routes to $all with prefix filter $idx-ce-{category}
        var filter = RecordFilter.ByExpression($"category = '{category}'");

        var exprFilter = (ExpressionFilter)filter;
        Log.Information("Category filter server filter: {ServerFilter}", exprFilter.ServerFilter);
        await Assert.That(exprFilter.ServerFilter).IsTypeOf<CategoryFilter>();
        await Assert.That(((CategoryFilter)exprFilter.ServerFilter).IndexName).IsEqualTo($"$idx-ce-{category}");

        // Assert — poll until the secondary index catches up (eventually consistent)
        await Assert.That(async () => {
            using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var ownCount = 0;
            var records  = new List<Record>();

            var options = new ReadRecordsOptions {
                Start        = lastPosition.LogPosition,
                Filter       = filter,
                SkipDecoding = true
            };

            await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, cancellator.Token)) {
                if (msg.Value is Record record) {
                    records.Add(record);

                    if (record.Stream == simulation.Stream)
                        ownCount++;

                    if (ownCount >= simulation.Records.Length)
                        await cancellator.CancelAsync();
                }
            }

            await Assert.That(ownCount).IsEqualTo(simulation.Records.Length);

            foreach (var record in records)
                await Assert.That(record.Stream.Category).IsEqualTo(category);
        }).EventuallyPasses(IndexCatchUp(ct));
    }

    [Test]
    public async ValueTask reads_log_filtered_by_category_with_schema_predicate(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var simulation   = await Fixture.SeedTestStream(ct);
        var category     = simulation.Stream.Category;

        var firstRecord = await Fixture.Client.Streams
            .ReadStreamRecords(simulation.Stream, ct)
            .Where(m => m.IsRecord).Select(m => m.AsRecord)
            .FirstAsync(ct);

        var expectedSchema = firstRecord.Schema.Schema;

        // Assert — poll until the secondary index catches up (eventually consistent)
        await Assert.That(async () => {
            using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var records = new List<Record>();

            var options = new ReadRecordsOptions {
                Start        = lastPosition.LogPosition,
                Filter       = RecordFilter.ByExpression($"category = '{category}' AND schema_name = '{expectedSchema}'"),
                SkipDecoding = true
            };

            await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, cancellator.Token)) {
                if (msg.Value is Record record) {
                    records.Add(record);
                    if (record.Position.LogPosition >= simulation.Position)
                        await cancellator.CancelAsync();
                }
            }

            await Assert.That(records.Count).IsGreaterThanOrEqualTo(1);

            foreach (var record in records) {
                await Assert.That(record.Stream.Category).IsEqualTo(category);
                await Assert.That(record.Schema.Schema).IsEqualTo(expectedSchema);
            }
        }).EventuallyPasses(IndexCatchUp(ct));
    }

    #endregion

    #region . Schema Index .

    [Test]
    public async ValueTask reads_log_filtered_by_schema_name(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var simulation   = await Fixture.SeedTestStream(ct);

        var firstRecord = await Fixture.Client.Streams
            .ReadStreamRecords(simulation.Stream, ct)
            .Where(m => m.IsRecord).Select(m => m.AsRecord)
            .FirstAsync(ct);

        var expectedSchema = firstRecord.Schema.Schema;

        // Act — routes to $all with prefix filter $idx-et-{schema}
        var filter = RecordFilter.ByExpression($"schema_name = '{expectedSchema}'");

        var exprFilter = (ExpressionFilter)filter;
        Log.Information("Schema filter server filter: {ServerFilter}", exprFilter.ServerFilter);
        await Assert.That(exprFilter.ServerFilter).IsTypeOf<SchemaFilter>();
        await Assert.That(((SchemaFilter)exprFilter.ServerFilter).IndexName).IsEqualTo($"$idx-et-{expectedSchema}");

        // Poll until the secondary index catches up (eventually consistent)
        var records = new List<Record>();

        for (var attempt = 0; attempt < 10 && records.Count == 0; attempt++) {
            if (attempt > 0)
                await Task.Delay(500, ct);

            using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

            records.Clear();

            var options = new ReadRecordsOptions {
                Start        = lastPosition.LogPosition,
                Filter       = filter,
                SkipDecoding = true
            };

            await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, cancellator.Token)) {
                if (msg.Value is Record record) {
                    records.Add(record);
                    if (record.Position.LogPosition >= simulation.Position)
                        await cancellator.CancelAsync();
                }
            }
        }

        // Assert
        await Assert.That(records.Count).IsGreaterThanOrEqualTo(1);

        foreach (var record in records)
            await Assert.That(record.Schema.Schema).IsEqualTo(expectedSchema);
    }

    [Test]
    public async ValueTask reads_log_filtered_by_stream_and_schema(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var simulation   = await Fixture.SeedTestStream(ct);

        // stream = 'X' has higher priority than schema_name in extraction,
        // so stream takes priority and routes to the stream directly
        var firstRecord = await Fixture.Client.Streams
            .ReadStreamRecords(simulation.Stream, ct)
            .Where(m => m.IsRecord)
            .Select(m => m.AsRecord)
            .FirstAsync(ct);

        var expectedSchema = firstRecord.Schema.Schema;

        // Act
        var options = new ReadRecordsOptions {
            Start        = lastPosition.LogPosition,
            Filter       = RecordFilter.ByExpression($"stream = '{simulation.Stream}' AND schema_name = '{expectedSchema}'"),
            Limit        = simulation.Records.Length,
            SkipDecoding = true
        };

        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, ct).Take(simulation.Records.Length).WithCancellation(ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        // Assert
        await Assert.That(records.Count).IsGreaterThanOrEqualTo(1);

        foreach (var record in records) {
            await Assert.That(record.Stream).IsEqualTo(simulation.Stream);
            await Assert.That(record.Schema.Schema).IsEqualTo(expectedSchema);
        }
    }

    #endregion

    #region . Position and Timestamp Filters .

    [Test]
    public async ValueTask reads_log_filtered_by_position_range(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var simulation   = await Fixture.SeedTestStream(ct);

        var startPos = lastPosition.LogPosition.Value;
        var endPos   = simulation.Position.Value;

        // Act
        var options = new ReadRecordsOptions {
            Start        = lastPosition.LogPosition,
            Filter       = RecordFilter.ByExpression($"stream = '{simulation.Stream}' AND log_position >= {startPos} AND log_position <= {endPos}"),
            Limit        = simulation.Records.Length,
            SkipDecoding = true
        };

        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(simulation.Records.Length);

        foreach (var record in records) {
            await Assert.That(record.Position.LogPosition.Value).IsGreaterThanOrEqualTo(startPos);
            await Assert.That(record.Position.LogPosition.Value).IsLessThanOrEqualTo(endPos);
        }
    }

    [Test]
    public async ValueTask reads_log_filtered_by_created_at_millis(CancellationToken ct) {
        // Arrange
        var before       = DateTimeOffset.UtcNow;
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var simulation   = await Fixture.SeedTestStream(ct);
        var sinceMillis  = before.ToUnixTimeMilliseconds();

        // Act
        var options = new ReadRecordsOptions {
            Start        = lastPosition.LogPosition,
            Filter       = RecordFilter.ByExpression($"stream = '{simulation.Stream}' AND created_at >= {sinceMillis}"),
            Limit        = simulation.Records.Length,
            SkipDecoding = true
        };

        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(simulation.Records.Length);
    }

    #endregion

    #region . Complex Expressions .

    // broken! its returning records in repetition?! makes no sense...
    [Test]
    public async ValueTask reads_log_with_or_expression_falls_back_to_all(CancellationToken ct) {
        // Arrange
        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var startPos = await Fixture.LatestRecordPosition(ct);
        var sim1     = await Fixture.SeedTestStream(5, ct);
        var sim2     = await Fixture.SeedTestStream(5, ct);

        var expectedCount = sim1.Records.Length + sim2.Records.Length;

        // Act
        var filter = RecordFilter
            .ByExpression($"stream = '{sim1.Stream}' OR stream = '{sim2.Stream}'");

        await Assert.That(filter.As<ExpressionFilter>().ServerFilter).IsTypeOf<StreamRegexFilter>();

        var options = new ReadRecordsOptions {
            Start        = startPos.LogPosition,
            Filter       = filter,
            SkipDecoding = true
        };

        var temp = await Fixture.Client.Streams
            .ReadRecords(options, cancellator.Token)
            .Where(msg => msg.Value is Record)
            .Select(msg => msg.AsRecord)
            .TakeWhile(rec => rec.Position.LogPosition <= sim2.Position)
            .ToListAsync(cancellator.Token);
        
        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, cancellator.Token)) {
            if (msg.Value is not Record record)
                continue;

            records.Add(record);
            
            // cancel when position its the last sim
            if (record.Position.LogPosition == sim2.Position)
                await cancellator.CancelAsync();
        }

        // Assert
        var actualRecords = records
            .Where(r => r.Stream == sim1.Stream || r.Stream == sim2.Stream)
            .ToList();
        
        await Assert.That(actualRecords).Count().IsEqualTo(expectedCount);
    }

    [Test]
    public async ValueTask reads_log_with_in_expression(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var sim1         = await Fixture.SeedTestStream(ct);
        var sim2         = await Fixture.SeedTestStream(ct);

        var expectedCount = sim1.Records.Length + sim2.Records.Length;

        var filter = RecordFilter
            .ByExpression($"stream IN ('{sim1.Stream}', '{sim2.Stream}')");
        
        await Assert.That(filter.UnwrapFilter()).IsTypeOf<StreamRegexFilter>();
        
        // Act
        var options = new ReadRecordsOptions {
            Start        = lastPosition.LogPosition,
            Filter       = filter,
            Limit        = expectedCount,
            SkipDecoding = true
        };

        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        // Assert
        var actualRecords = records
            .Where(r => r.Stream == sim1.Stream || r.Stream == sim2.Stream)
            .ToList();
        
        await Assert.That(actualRecords).Count().IsEqualTo(expectedCount);
    }

    [Test]
    public async ValueTask reads_log_with_not_expression_excludes_matching(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var simulation   = await Fixture.SeedTestStream(ct);
        var excluded     = await Fixture.SeedTestStream(ct);

        var category      = simulation.Stream.Category;
        var expectedCount = simulation.Records.Length + excluded.Records.Length;

        // Act — stream filter with NOT to exclude a stream, scoped by LIKE to our test streams
        var options = new ReadRecordsOptions {
            Start        = lastPosition.LogPosition,
            Filter       = RecordFilter.ByExpression($"stream LIKE '{category}-%' AND stream != '{excluded.Stream}'"),
            Limit        = expectedCount,
            SkipDecoding = true
        };

        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        // Assert
        foreach (var record in records)
            await Assert.That(record.Stream).IsNotEqualTo(excluded.Stream);

        await Assert.That(records.Count).IsGreaterThanOrEqualTo(simulation.Records.Length);
    }

    #endregion
}
