using Bogus;
using Kurrent.Client.Streams;
using Kurrent.Sdk.Testkit.Fixtures;
using Serilog;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Read")]
[Category("Filters")]
[Category("Integration")]
[Timeout(30_000)]
public class ReadWithFilterTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    #region . Stream Filter .

    [Test]
    public async ValueTask reads_log_filtered_by_stream(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var simulation   = await Fixture.SeedTestStream(ct);

        // Act
        var options = ReadRecordsRequest.New
            .FromPosition(lastPosition.LogPosition)
            .WithFilterExpression($"stream = '{simulation.Stream}'")
            .WithMaxRecords(simulation.Records.Length)
            .SkipDecoding()
            .Build();

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
        var options = ReadRecordsRequest.New
            .FromPosition(lastPosition.LogPosition)
            .WithFilterExpression($"stream LIKE '{category}-%'")
            .WithMaxRecords(simulation.Records.Length)
            .SkipDecoding()
            .Build();

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
    
    
    [Test]
    [Skip("LINQ .Take() over IAsyncEnumerable does not cancel the underlying gRPC server stream; need to figure out the right pattern for limiting a filtered $all read client-side.")]
    public async ValueTask reads_log_filtered_by_stream_pattern_using_take_instead_of_max_records(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var simulation   = await Fixture.SeedTestStream(ct);
        var category     = simulation.Stream.Category;

        var limit = simulation.Records.Length + Random.Shared.Next(100, 1000);

        // Act
        var options = ReadRecordsRequest.New
            .FromPosition(lastPosition.LogPosition)
            .WithFilterExpression($"stream LIKE '{category}-%'")
            .SkipDecoding()
            .Build();

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

        Log.Information("Category filter targets: {TargetStream}", filter.TargetStream);
        await Assert.That(filter.TargetStream).IsEqualTo($"$idx-ce-{category}");

        // Poll until the secondary index catches up (eventually consistent)
        var ownCount = 0;
        var records  = new List<Record>();

        for (var attempt = 0; attempt < 10 && ownCount < simulation.Records.Length; attempt++) {
            if (attempt > 0)
                await Task.Delay(500, ct);

            using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

            records.Clear();
            ownCount = 0;

            var options = ReadRecordsRequest.New
                .FromPosition(lastPosition.LogPosition)
                .WithFilter(filter)
                .SkipDecoding()
                .Build();

            await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, cancellator.Token)) {
                if (msg.Value is Record record) {
                    records.Add(record);

                    if (record.Stream == simulation.Stream)
                        ownCount++;

                    if (ownCount >= simulation.Records.Length)
                        await cancellator.CancelAsync();
                }
            }
        }

        // Assert
        await Assert.That(ownCount).IsEqualTo(simulation.Records.Length);

        foreach (var record in records)
            await Assert.That(record.Stream.Category).IsEqualTo(category);
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

        var expectedSchema = firstRecord.Schema.SchemaName;

        // Poll until the secondary index catches up (eventually consistent)
        var records = new List<Record>();

        for (var attempt = 0; attempt < 10 && records.Count == 0; attempt++) {
            if (attempt > 0)
                await Task.Delay(500, ct);

            using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

            records.Clear();

            var options = ReadRecordsRequest.New
                .FromPosition(lastPosition.LogPosition)
                .WithFilterExpression($"category = '{category}' AND schema_name = '{expectedSchema}'")
                .SkipDecoding()
                .Build();

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

        foreach (var record in records) {
            await Assert.That(record.Stream.Category).IsEqualTo(category);
            await Assert.That(record.Schema.SchemaName).IsEqualTo(expectedSchema);
        }
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

        var expectedSchema = firstRecord.Schema.SchemaName;

        // Act — routes to $all with prefix filter $idx-et-{schema}
        var filter = RecordFilter.ByExpression($"schema_name = '{expectedSchema}'");

        Log.Information("Schema filter targets: {TargetStream}", filter.TargetStream);
        await Assert.That(filter.TargetStream).IsEqualTo($"$idx-et-{expectedSchema}");

        // Poll until the secondary index catches up (eventually consistent)
        var records = new List<Record>();

        for (var attempt = 0; attempt < 10 && records.Count == 0; attempt++) {
            if (attempt > 0)
                await Task.Delay(500, ct);

            using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

            records.Clear();

            var options = ReadRecordsRequest.New
                .FromPosition(lastPosition.LogPosition)
                .WithFilter(filter)
                .SkipDecoding()
                .Build();

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
            await Assert.That(record.Schema.SchemaName).IsEqualTo(expectedSchema);
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

        var expectedSchema = firstRecord.Schema.SchemaName;

        // Act
        var options = ReadRecordsRequest.New
            .FromPosition(lastPosition.LogPosition)
            .WithFilterExpression($"stream = '{simulation.Stream}' AND schema_name = '{expectedSchema}'")
            .WithMaxRecords(simulation.Records.Length)
            .SkipDecoding()
            .Build();

        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, ct).Take(simulation.Records.Length).WithCancellation(ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        // Assert
        await Assert.That(records.Count).IsGreaterThanOrEqualTo(1);

        foreach (var record in records) {
            await Assert.That(record.Stream).IsEqualTo(simulation.Stream);
            await Assert.That(record.Schema.SchemaName).IsEqualTo(expectedSchema);
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
        var options = ReadRecordsRequest.New
            .FromPosition(lastPosition.LogPosition)
            .WithFilterExpression($"stream = '{simulation.Stream}' AND log_position >= {startPos} AND log_position <= {endPos}")
            .WithMaxRecords(simulation.Records.Length)
            .SkipDecoding()
            .Build();

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
        var options = ReadRecordsRequest.New
            .FromPosition(lastPosition.LogPosition)
            .WithFilterExpression($"stream = '{simulation.Stream}' AND created_at >= {sinceMillis}")
            .WithMaxRecords(simulation.Records.Length)
            .SkipDecoding()
            .Build();

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

    [Test]
    public async ValueTask reads_log_with_or_expression_falls_back_to_all(CancellationToken ct) {
        // Arrange
        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var sim1         = await Fixture.SeedTestStream(ct);
        var sim2         = await Fixture.SeedTestStream(ct);

        var expectedCount = sim1.Records.Length + sim2.Records.Length;

        // Act — OR expressions cannot be pushed to a single index, fall back to $all
        // with no server-side filter, so we must use CTS to stop reading
        var filter = RecordFilter.ByExpression($"stream = '{sim1.Stream}' OR stream = '{sim2.Stream}'");

        await Assert.That(filter.TargetStream).IsEqualTo("$all");

        var options = ReadRecordsRequest.New
            .FromPosition(lastPosition.LogPosition)
            .WithFilter(filter)
            .SkipDecoding()
            .Build();

        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, cancellator.Token)) {
            if (msg.Value is Record record) {
                records.Add(record);
                if (record.Position.LogPosition >= sim2.Position)
                    await cancellator.CancelAsync();
            }
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(expectedCount);

        var streams = records.Select(r => r.Stream).Distinct().ToList();
        await Assert.That(streams).Contains(sim1.Stream);
        await Assert.That(streams).Contains(sim2.Stream);
    }

    [Test]
    public async ValueTask reads_log_with_in_expression(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var sim1         = await Fixture.SeedTestStream(ct);
        var sim2         = await Fixture.SeedTestStream(ct);

        var expectedCount = sim1.Records.Length + sim2.Records.Length;

        // Act
        var options = ReadRecordsRequest.New
            .FromPosition(lastPosition.LogPosition)
            .WithFilterExpression($"stream IN ('{sim1.Stream}', '{sim2.Stream}')")
            .WithMaxRecords(expectedCount)
            .SkipDecoding()
            .Build();

        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(expectedCount);
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
        var options = ReadRecordsRequest.New
            .FromPosition(lastPosition.LogPosition)
            .WithFilterExpression($"stream LIKE '{category}-%' AND stream != '{excluded.Stream}'")
            .WithMaxRecords(expectedCount)
            .SkipDecoding()
            .Build();

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
