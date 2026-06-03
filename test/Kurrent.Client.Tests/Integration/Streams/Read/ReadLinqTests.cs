using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Read")]
[Category("Integration")]
[Timeout(10_000)]
public class ReadLinqTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask take(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        // Act
        var records = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .Take(5)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(5);
    }

    [Test]
    public async ValueTask first_async(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        // Act
        var first = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .FirstAsync(ct);

        // Assert
        await Assert.That(first.Position.Stream).IsEqualTo(data.Stream);
        await Assert.That(first.Position.Revision).IsEqualTo(Revision.From(0));
    }

    [Test]
    public async ValueTask take_while(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        // Act
        var records = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .TakeWhile(r => r.Position.Revision < Revision.From(5))
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(5);
        await Assert.That(records.Last().Position.Revision).IsEqualTo(Revision.From(4));
    }

    [Test]
    public async ValueTask skip_and_take(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        // Act
        var records = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct)
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
    public async ValueTask count_async(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(7, ct);

        // Act — ReadRecords is finite, so CountAsync works without Take
        var count = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct)
            .Where(msg => msg.IsRecord)
            .CountAsync(ct);

        // Assert
        await Assert.That(count).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask any_async(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        // Act
        var hasRecords = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct)
            .Where(msg => msg.IsRecord)
            .AnyAsync(ct);

        // Assert
        await Assert.That(hasRecords).IsTrue();
    }

    [Test]
    public async ValueTask all_async(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        // Act — finite stream, AllAsync terminates naturally
        var allMatch = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .AllAsync(r => r.Position.Stream == data.Stream, ct);

        // Assert
        await Assert.That(allMatch).IsTrue();
    }

    [Test]
    public async ValueTask last_async(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        // Act — finite stream, LastAsync works
        var last = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .LastAsync(ct);

        // Assert
        await Assert.That(last.Position.Stream).IsEqualTo(data.Stream);
        await Assert.That(last.Position.Revision).IsEqualTo(Revision.From(data.Records.Length - 1));
    }

    [Test]
    public async ValueTask select_projection(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        // Act
        var ids = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord.Id)
            .ToListAsync(ct);

        // Assert
        await Assert.That(ids.Count).IsEqualTo(data.Records.Length);
        await Assert.That(ids.Distinct().Count()).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask aggregate_async(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        // Act — finite stream, AggregateAsync terminates naturally
        var revisions = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .AggregateAsync(
                new List<long>(),
                (list, record) => { list.Add(record.Position.Revision.Value); return list; },
                ct
            );

        // Assert
        await Assert.That(revisions.Count).IsEqualTo(data.Records.Length);
        await Assert.That(revisions.First()).IsEqualTo(0L);
        await Assert.That(revisions.Last()).IsEqualTo(data.Records.Length - 1);
    }

    [Test]
    public async ValueTask to_list_async_materializes_all(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        // Act — finite stream, ToListAsync terminates naturally (unlike Subscribe)
        var records = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(data.Records.Length);
    }
}
