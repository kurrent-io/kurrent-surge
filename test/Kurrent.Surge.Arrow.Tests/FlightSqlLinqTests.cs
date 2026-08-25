using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Arrow.Tests;

[Category("Arrow")]
[Category("Integration")]
[Timeout(60_000)]

public class FlightSqlLinqTests {
    [ClassDataSource<ArrowFixture>(Shared = SharedType.PerTestSession)]
    public required ArrowFixture Fixture { get; [UsedImplicitly] init; }

    KurrentArrowClient NewQueries() => KurrentArrowClient.Create(Fixture.ConnectionString);

    // SELECT * so the full Record can be reconstructed; ordered by revision so the positional LINQ
    // operators (Take/Skip/TakeWhile/SkipWhile) are deterministic.
    static string SelectStream(string stream) =>
        $"SELECT * FROM kdb.records WHERE stream = '{stream}' ORDER BY stream_revision";

    // The DuckDB-backed secondary index is eventually consistent, so wait until all appended records
    // are visible before exercising the operators (bounded by the test timeout).
    static async Task WaitUntilIndexed(KurrentArrowClient queries, string stream, int expected, CancellationToken ct) {
        while (true) {
            ct.ThrowIfCancellationRequested();

            // Count via the generic mapper so the readiness probe doesn't depend on full Record mapping.
            var count = await queries
                .Query($"SELECT stream FROM kdb.records WHERE stream = '{stream}'", _ => 0, ct)
                .CountAsync(ct);

            if (count >= expected)
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }

    #region ->> Record mapping <<-

    [Test]
    public async ValueTask maps_columns_to_record(CancellationToken ct) {
        // Arrange
        await using var queries = NewQueries();
        var data   = await Fixture.SeedTestStream(5, ct);
        var stream = data.Stream;
        await WaitUntilIndexed(queries, stream.ToString(), data.Records.Length, ct);

        // Act
        var record = await queries.Query(SelectStream(stream.ToString()), ct).FirstAsync(ct);

        // Assert — the kdb.records row reconstructs a full SDK Record (Data is raw bytes).
        await Assert.That(record.Id).IsNotEqualTo(RecordId.None);
        await Assert.That(record.Stream).IsEqualTo(stream);
        await Assert.That(record.Revision).IsEqualTo(Revision.From(0));
        await Assert.That(record.Timestamp).IsNotEqualTo(default(DateTime));
        await Assert.That(record.Schema.Schema.Value).IsNotEmpty();
        await Assert.That(record.Data.Length).IsGreaterThan(0);
    }

    #endregion

    #region ->> Positional operators <<-

    [Test]
    public async ValueTask take(CancellationToken ct) {
        // Arrange
        await using var queries = NewQueries();
        var data   = await Fixture.SeedTestStream(10, ct);
        var stream = data.Stream.ToString();
        await WaitUntilIndexed(queries, stream, data.Records.Length, ct);

        // Act
        var records = await queries.Query(SelectStream(stream), ct)
            .Take(5)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(5);
    }

    [Test]
    public async ValueTask take_while(CancellationToken ct) {
        // Arrange
        await using var queries = NewQueries();
        var data   = await Fixture.SeedTestStream(10, ct);
        var stream = data.Stream.ToString();
        await WaitUntilIndexed(queries, stream, data.Records.Length, ct);

        // Act
        var records = await queries.Query(SelectStream(stream), ct)
            .TakeWhile(r => r.Revision < Revision.From(5))
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(5);
        await Assert.That(records[^1].Revision).IsEqualTo(Revision.From(4));
    }

    [Test]
    public async ValueTask skip_and_take(CancellationToken ct) {
        // Arrange
        await using var queries = NewQueries();
        var data   = await Fixture.SeedTestStream(10, ct);
        var stream = data.Stream.ToString();
        await WaitUntilIndexed(queries, stream, data.Records.Length, ct);

        // Act
        var records = await queries.Query(SelectStream(stream), ct)
            .Skip(3)
            .Take(4)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(4);
        await Assert.That(records[0].Revision).IsEqualTo(Revision.From(3));
        await Assert.That(records[^1].Revision).IsEqualTo(Revision.From(6));
    }

    [Test]
    public async ValueTask skip_while(CancellationToken ct) {
        // Arrange
        await using var queries = NewQueries();
        var data   = await Fixture.SeedTestStream(10, ct);
        var stream = data.Stream.ToString();
        await WaitUntilIndexed(queries, stream, data.Records.Length, ct);

        // Act
        var records = await queries.Query(SelectStream(stream), ct)
            .SkipWhile(r => r.Revision < Revision.From(5))
            .Take(3)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(3);
        await Assert.That(records[0].Revision).IsEqualTo(Revision.From(5));
    }

    #endregion

    #region ->> Element operators <<-

    [Test]
    public async ValueTask first_async(CancellationToken ct) {
        // Arrange
        await using var queries = NewQueries();
        var data   = await Fixture.SeedTestStream(10, ct);
        var stream = data.Stream;
        await WaitUntilIndexed(queries, stream.ToString(), data.Records.Length, ct);

        // Act
        var first = await queries.Query(SelectStream(stream.ToString()), ct).FirstAsync(ct);

        // Assert
        await Assert.That(first.Stream).IsEqualTo(stream);
        await Assert.That(first.Revision).IsEqualTo(Revision.From(0));
    }

    [Test]
    public async ValueTask first_or_default_async_returns_null_when_no_match(CancellationToken ct) {
        // Arrange
        await using var queries = NewQueries();
        var missingStream = $"missing-{Guid.NewGuid():N}";

        // Act
        var record = await queries.Query(SelectStream(missingStream), ct).FirstOrDefaultAsync(ct);

        // Assert
        await Assert.That(record).IsNull();
    }

    [Test]
    public async ValueTask single_or_default_async_returns_the_one_match(CancellationToken ct) {
        // Arrange
        await using var queries = NewQueries();
        var data   = await Fixture.SeedTestStream(10, ct);
        var stream = data.Stream.ToString();
        await WaitUntilIndexed(queries, stream, data.Records.Length, ct);

        var singleRevisionSql = $"SELECT * FROM kdb.records WHERE stream = '{stream}' AND stream_revision = 0";

        // Act
        var record = await queries.Query(singleRevisionSql, ct).SingleOrDefaultAsync(ct);

        // Assert
        await Assert.That(record).IsNotNull();
        await Assert.That(record!.Revision).IsEqualTo(Revision.From(0));
    }

    #endregion

    #region ->> Aggregation operators <<-

    [Test]
    public async ValueTask count_async(CancellationToken ct) {
        // Arrange
        await using var queries = NewQueries();
        var data     = await Fixture.SeedTestStream(7, ct);
        var stream   = data.Stream.ToString();
        var expected = data.Records.Length;
        await WaitUntilIndexed(queries, stream, expected, ct);

        // Act
        var count = await queries.Query(SelectStream(stream), ct).CountAsync(ct);

        // Assert
        await Assert.That(count).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask any_async(CancellationToken ct) {
        // Arrange
        await using var queries = NewQueries();
        var data   = await Fixture.SeedTestStream(5, ct);
        var stream = data.Stream.ToString();
        await WaitUntilIndexed(queries, stream, data.Records.Length, ct);

        // Act
        var hasRecords = await queries.Query(SelectStream(stream), ct).AnyAsync(ct);

        // Assert
        await Assert.That(hasRecords).IsTrue();
    }

    [Test]
    public async ValueTask all_async(CancellationToken ct) {
        // Arrange
        await using var queries = NewQueries();
        var data   = await Fixture.SeedTestStream(5, ct);
        var stream = data.Stream;
        await WaitUntilIndexed(queries, stream.ToString(), data.Records.Length, ct);

        // Act
        var allOnStream = await queries.Query(SelectStream(stream.ToString()), ct)
            .AllAsync(r => r.Stream == stream, ct);

        // Assert
        await Assert.That(allOnStream).IsTrue();
    }

    [Test]
    public async ValueTask aggregate_async(CancellationToken ct) {
        // Arrange
        await using var queries = NewQueries();
        var data     = await Fixture.SeedTestStream(5, ct);
        var stream   = data.Stream.ToString();
        var expected = data.Records.Length;
        await WaitUntilIndexed(queries, stream, expected, ct);

        // Act — revisions are unique per stream, so the set size equals the record count.
        var revisions = await queries.Query(SelectStream(stream), ct)
            .AggregateAsync(
                new HashSet<Revision>(),
                (set, record) => { set.Add(record.Revision); return set; },
                ct);

        // Assert
        await Assert.That(revisions.Count).IsEqualTo(expected);
    }

    #endregion

    #region ->> Projection <<-

    [Test]
    public async ValueTask select_projection(CancellationToken ct) {
        // Arrange
        await using var queries = NewQueries();
        var data     = await Fixture.SeedTestStream(5, ct);
        var stream   = data.Stream;
        var expected = data.Records.Length;
        await WaitUntilIndexed(queries, stream.ToString(), expected, ct);

        // Act
        var streams = await queries.Query(SelectStream(stream.ToString()), ct)
            .Select(r => r.Stream)
            .ToListAsync(ct);

        // Assert
        await Assert.That(streams.Count).IsEqualTo(expected);
        await Assert.That(streams.All(s => s == stream)).IsTrue();
    }

    [Test]
    public async ValueTask custom_projection_with_mapper(CancellationToken ct) {
        // Arrange — the generic Query<T> overload is the AOT-safe stand-in for Dapper's Query<T>.
        await using var queries = NewQueries();
        var data     = await Fixture.SeedTestStream(5, ct);
        var stream   = data.Stream.ToString();
        var expected = data.Records.Length;
        await WaitUntilIndexed(queries, stream, expected, ct);

        // Act — map straight into a long via an explicit reader mapper.
        var revisions = await queries
            .Query(SelectStream(stream), reader => Convert.ToInt64(reader["stream_revision"]), ct)
            .ToListAsync(ct);

        // Assert — ordered by revision, so the projected longs are 0..expected-1 in order.
        await Assert.That(revisions.Count).IsEqualTo(expected);
        await Assert.That(revisions.SequenceEqual(Enumerable.Range(0, expected).Select(i => (long)i))).IsTrue();
    }

    #endregion
}
