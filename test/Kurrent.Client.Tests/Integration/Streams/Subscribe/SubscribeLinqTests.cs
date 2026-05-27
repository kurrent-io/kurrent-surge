using Kurrent.Client.Streams;
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
            RecordFilter.ByStream(data.Stream),
            RecordPosition.From(LogPosition.Earliest, data.Stream, Revision.Min),
            skipDecoding: true,
            ct: ct
        );

    IAsyncEnumerable<SubscribeMessage> SubscribeToLog(SeededTestStream data, LogPosition after, CancellationToken ct) =>
        Fixture.Client.Streams.Subscribe(
            RecordFilter.ByExpression($"stream LIKE '{data.Stream}%'"),
            RecordPosition.From(after),
            skipDecoding: true,
            ct: ct
        );

    IAsyncEnumerable<SubscribeMessage> SubscribeToIndex(LogPosition after, CancellationToken ct) =>
        Fixture.Client.Streams.Subscribe(
            RecordFilter.ByExpression("category = 'race'"),
            RecordPosition.From(after),
            skipDecoding: true,
            ct: ct
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
        var records = await SubscribeToLog(data, lastPosition.LogPosition, ct)
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
        var first = await SubscribeToLog(data, lastPosition.LogPosition, ct)
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
        var records = await SubscribeToLog(data, lastPosition.LogPosition, ct)
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
        var records = await SubscribeToLog(data, lastPosition.LogPosition, ct)
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
        var count = await SubscribeToLog(data, lastPosition.LogPosition, ct)
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
        var ids = await SubscribeToLog(data, lastPosition.LogPosition, ct)
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
        var records = await SubscribeToLog(data, lastPosition.LogPosition, ct)
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
        var hasRecords = await SubscribeToLog(data, lastPosition.LogPosition, ct)
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
        var allMatch = await SubscribeToLog(data, lastPosition.LogPosition, ct)
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
        var positions = await SubscribeToLog(data, lastPosition.LogPosition, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord.Position)
            .Take(data.Records.Length)
            .ToListAsync(ct);

        // Assert
        await Assert.That(positions.Count).IsEqualTo(data.Records.Length);
        await Assert.That(positions.All(p => p.Stream == data.Stream)).IsTrue();
    }

    #endregion

    #region ->> Index Subscription <<-

    [Test]
    public async ValueTask index_take(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(10, ct);

        // Act — subscribe via expression filter that resolves to category index
        var records = await SubscribeToIndex(lastPosition.LogPosition, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .Take(5)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(5);
    }

    [Test]
    public async ValueTask index_aggregate(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data = await Fixture.SeedTestStream(5, ct);

        // Act
        var streams = await SubscribeToIndex(lastPosition.LogPosition, ct)
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
}
