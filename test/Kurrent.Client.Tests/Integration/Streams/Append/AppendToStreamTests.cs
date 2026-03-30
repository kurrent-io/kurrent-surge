using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testkit;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
public class AppendToStreamTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.None)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask creates_multiple_streams(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(ct);

        var expectedRevision = data.Records.Count - 1;

        // Act
        var result = await Fixture.Client.Streams
            .AppendToStream(AppendToStreamRequest.For(data.Stream)
                .WithRecords(data.Records)
                .Build(), ct);

        // Assert
        await Assert.That(result.Revision).IsEqualTo(expectedRevision);
    }

    [Test]
    public async ValueTask appends_to_existing_stream(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(ct);

        var created = await Fixture.Client.Streams
            .AppendToStream(AppendToStreamRequest.For(data.Stream)
                .WithRecords(data.Records.Take(1))
                .Build(), ct);

        var expectedRevision = data.Records.Count - 1;

        // Act
        var result = await Fixture.Client.Streams
            .AppendToStream(AppendToStreamRequest.For(data.Stream)
                .ExpectingState(created.Revision)
                .WithRecords(data.Records.Skip(1))
                .Build(), ct);

        // Assert
        await Assert.That(result.Position).IsGreaterThan(created.Position);
        await Assert.That(result.Revision).IsEqualTo(expectedRevision);
    }

    [Test]
    public async ValueTask appends_to_existing_stream_with_expected_revision(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(ct);

        var created = await Fixture.Client.Streams
            .AppendToStream(AppendToStreamRequest.For(data.Stream)
                .WithRecords(data.Records.Take(1))
                .Build(), ct);

        var expectedRevision = data.Records.Count - 1;

        // Act
        var result = await Fixture.Client.Streams
            .AppendToStream(AppendToStreamRequest.For(data.Stream)
                .ExpectingState(created.Revision)
                .WithRecords(data.Records.Skip(1))
                .Build(), ct);

        // Assert
        await Assert.That(result.Position).IsGreaterThan(created.Position);
        await Assert.That(result.Revision).IsEqualTo(expectedRevision);
    }

    [Test]
    public async ValueTask appends_to_existing_stream_with_any_state(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(ct);

        var created = await Fixture.Client.Streams
            .AppendToStream(data.Stream, data.Records.Take(1), ct);

        var expectedRevision = data.Records.Count - 1;

        // Act
        var result = await Fixture.Client.Streams
            .AppendToStream(data.Stream, ExpectedStreamCondition.Any, data.Records.Skip(1), ct);

        // Assert
        await Assert.That(result.Position).IsGreaterThan(created.Position);
        await Assert.That(result.Revision).IsEqualTo(expectedRevision);
    }

    [Test]
    public async ValueTask appends_to_deleted_stream(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(ct);

        var created = await Fixture.Client.Streams.AppendToStream(data.Stream, data.Records.Take(1), ct);

        var expectedRevision = data.Records.Count - 1;

        // Act
        var result = await Fixture.Client.Streams.AppendToStream(data.Stream, data.Records.Skip(1), ct);

        // Assert
        await Assert.That(result.Position).IsGreaterThan(created.Position);
        await Assert.That(result.Revision).IsEqualTo(expectedRevision);
    }

    [Test]
    public async ValueTask throws_StreamAlreadyExistsException_when_stream_is_not_suposed_to_exist(CancellationToken ct) {
        // Arrange
        var seeded = await Fixture.SeedTestStream(ct);

        // Act & Assert
        var result = await Assert
            .That(async () => await Fixture.Client.Streams.AppendToStream(
                AppendToStreamRequest.For(seeded.Stream)
                    .ExpectingState(ExpectedStreamCondition.NoStream)
                    .WithRecords(seeded.Records.Take(1)) // just append the first record again...
                    .Build(),
                ct))
            .Throws<StreamAlreadyExistsException>();
    }


    public class ExpectedStatesWhenStreamIsTombstoned : TestCaseGenerator<ExpectedStreamState> {
        protected override IEnumerable<ExpectedStreamState> Data() {
            yield return ExpectedStreamCondition.Any;
            yield return ExpectedStreamCondition.NoStream;
            yield return ExpectedStreamCondition.Exists;
            yield return ExpectedStreamCondition.Deleted;
            yield return ExpectedStreamCondition.Tombstoned;
        }
    }

    [Test]
    [ExpectedStatesWhenStreamIsTombstoned]
    public async ValueTask throws_StreamTombstonedException_when_stream_is_tombstoned(
        ExpectedStreamState streamState,
        CancellationToken   ct
    ) {
        // Arrange
        var seeded = await Fixture.SeedTestStream(ct);

        await Fixture.Client.Streams.Tombstone(seeded.Stream, ct);

        // Act & Assert
        var result = await Assert
            .That(async () => await Fixture.Client.Streams.AppendToStream(
                AppendToStreamRequest.For(seeded.Stream)
                    .ExpectingState(streamState)
                    .WithRecords(seeded.Records.Take(1))
                    .Build(),
                ct))
            .Throws<StreamTombstonedException>();
    }

    [Test]
    public async ValueTask throws_StreamNotFoundException_when_expected_to_exist(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(ct);

        // Act & Assert
        await Assert
            .That(async () => await Fixture.Client.Streams.AppendToStream(
                AppendToStreamRequest.For(data.Stream)
                    .ExpectingState(ExpectedStreamCondition.Exists)
                    .WithRecords(data.Records)
                    .Build(), ct))
            .Throws<StreamNotFoundException>();
    }

    [Test]
    public async ValueTask throws_StreamRevisionConflictException_when_revision_mismatch(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(ct);

        var created = await Fixture.Client.Streams.AppendToStream(
            AppendToStreamRequest.For(data.Stream)
                .WithRecords(data.Records.Take(1))
                .Build(), ct);

        Revision wrongRevision = created.Revision + Random.Shared.Next(5, 100);

        var req = AppendToStreamRequest.For(data.Stream)
            .ExpectingState(wrongRevision)
            .WithRecords(data.Records.Skip(1))
            .Build();

        // Act
        var ex = await Assert.ThrowsAsync<StreamRevisionConflictException>(
            async () => await Fixture.Client.Streams.AppendToStream(req , ct));

        // Assert
        await Assert.That(ex).IsNotNull()
            .And.Member(x => x.ExpectedRevision, x => x.IsEqualTo(wrongRevision))
            .And.Member(x => x.ActualRevision, x => x.IsEqualTo(created.Revision));
    }

    #region . Retention Policies .

    [Test]
    public async ValueTask append_to_stream_with_maxcount_retention(CancellationToken ct) {
        // Arrange
        var maxCount = 5;
        var data     = await Fixture.TestData.GenerateSet(10, ct);

        await Fixture.Client.Streams
            .ConfigureStreamDataRetention(data.Stream, new() { MaxCount = maxCount }, ct);

        // Act
        await Fixture.Client.Streams
            .AppendToStream(AppendToStreamRequest.For(data.Stream)
                .ExpectingState(ExpectedStreamCondition.NoStream)
                .WithRecords(data.Records)
                .Build(), ct);

        // Assert
        var info = await Fixture.Client.Streams.GetStreamInfo(data.Stream, ct);
        await Assert.That(info.Metadata.MaxCount).IsEqualTo(maxCount);

        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct);

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        await Assert.That(records.Count).IsLessThanOrEqualTo(maxCount + 1); // +1 for potential timing
    }

    [Test]
    public async ValueTask append_to_stream_with_maxage_retention(CancellationToken ct) {
        // Arrange
        var maxAge = TimeSpan.FromHours(1);
        var data   = await Fixture.TestData.GenerateSet(5, ct);

        await Fixture.Client.Streams
            .ConfigureStreamDataRetention(data.Stream, new() { MaxAge = maxAge }, ct);

        // Act
        await Fixture.Client.Streams
            .AppendToStream(AppendToStreamRequest.For(data.Stream)
                .ExpectingState(ExpectedStreamCondition.NoStream)
                .WithRecords(data.Records)
                .Build(), ct);

        // Assert
        var info = await Fixture.Client.Streams.GetStreamInfo(data.Stream, ct);
        await Assert.That(info.Metadata.HasMaxAge).IsTrue();
        await Assert.That(info.Metadata.MaxAge).IsEqualTo(maxAge);

        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct);

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        await Assert.That(records.Count).IsEqualTo(data.Records.Count);
    }

    [Test]
    public async ValueTask append_to_stream_with_combined_retention_policies(CancellationToken ct) {
        // Arrange
        var maxCount = 10;
        var maxAge   = TimeSpan.FromMinutes(5);
        var data     = await Fixture.TestData.GenerateSet(10, ct);

        await Fixture.Client.Streams
            .ConfigureStreamDataRetention(data.Stream, new() {
                MaxCount = maxCount,
                MaxAge   = maxAge
            }, ct);

        // Act
        await Fixture.Client.Streams
            .AppendToStream(AppendToStreamRequest.For(data.Stream)
                .ExpectingState(ExpectedStreamCondition.NoStream)
                .WithRecords(data.Records)
                .Build(), ct);

        // Assert
        var info = await Fixture.Client.Streams.GetStreamInfo(data.Stream, ct);
        await Assert.That(info.Metadata.MaxCount).IsEqualTo(maxCount);
        await Assert.That(info.Metadata.MaxAge).IsEqualTo(maxAge);

        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct);

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        await Assert.That(records.Count).IsLessThanOrEqualTo(maxCount + 1);
    }

    #endregion

    #region . Pre-Configured Streams .

    [Test]
    public async ValueTask create_stream_via_append_when_retention_preconfigured(CancellationToken ct) {
        // Arrange
        var maxCount         = 5;
        var data             = await Fixture.TestData.GenerateSet(3, ct);

        var expectedRevision = new StreamRevision(data.Stream, data.Records.Count - 1);

        await Fixture.Client.Streams
            .ConfigureStreamDataRetention(data.Stream, new() { MaxCount = maxCount }, ct);

        // Act
        var result = await Fixture.Client.Streams
            .AppendToStream(AppendToStreamRequest.For(data.Stream)
                .ExpectingState(ExpectedStreamCondition.NoStream)
                .WithRecords(data.Records)
                .Build(), ct);

        // Assert
        await Assert.That(new StreamRevision(result.Stream, result.Revision)).IsEqualTo(expectedRevision);

        var info = await Fixture.Client.Streams.GetStreamInfo(data.Stream, ct);
        await Assert.That(info.Metadata.MaxCount).IsEqualTo(maxCount);
    }

    #endregion
}
