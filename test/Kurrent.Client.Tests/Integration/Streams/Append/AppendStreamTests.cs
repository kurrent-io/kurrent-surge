using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using static System.TimeSpan;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
public class AppendStreamTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask creates_multiple_streams(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(ct);

        var expectedRevision = data.Records.Length - 1;

        // Act
        var result = await Fixture.Client.Streams
            .AppendStream(AppendStreamRequest.For(data.Stream)
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
            .AppendStream(AppendStreamRequest.For(data.Stream)
                .WithRecords(data.Records.Take(1))
                .Build(), ct);

        var expectedRevision = data.Records.Length - 1;

        // Act
        var result = await Fixture.Client.Streams
            .AppendStream(AppendStreamRequest.For(data.Stream)
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
            .AppendStream(AppendStreamRequest.For(data.Stream)
                .WithRecords(data.Records.Take(1))
                .Build(), ct);

        var expectedRevision = data.Records.Length - 1;

        // Act
        var result = await Fixture.Client.Streams
            .AppendStream(AppendStreamRequest.For(data.Stream)
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
            .AppendStream(data.Stream, data.Records.Take(1), ct);

        var expectedRevision = data.Records.Length - 1;

        // Act
        var result = await Fixture.Client.Streams
            .AppendStream(data.Stream, ExpectedStreamCondition.Any, data.Records.Skip(1), ct);

        // Assert
        await Assert.That(result.Position).IsGreaterThan(created.Position);
        await Assert.That(result.Revision).IsEqualTo(expectedRevision);
    }

    [Test]
    public async ValueTask appends_to_deleted_stream(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(ct);

        var created = await Fixture.Client.Streams.AppendStream(data.Stream, data.Records.Take(1), ct);

        var expectedRevision = data.Records.Length - 1;

        // Act
        var result = await Fixture.Client.Streams.AppendStream(data.Stream, data.Records.Skip(1), ct);

        // Assert
        await Assert.That(result.Position).IsGreaterThan(created.Position);
        await Assert.That(result.Revision).IsEqualTo(expectedRevision);
    }

    [Test]
    [Skip("Something is off because it does not respect the NoStream check, investigate the DB")]
    public async ValueTask throws_StreamAlreadyExistsException_when_stream_is_not_suposed_to_exist(CancellationToken ct) {
        // Arrange
        var seed = await Fixture.SeedTestStream(ct);

        // Act & Assert
        var request = AppendStreamRequest.For(seed.Stream)
            .ExpectingState(ExpectedStreamCondition.NoStream)
            .WithRecords(seed.Records)
            .Build();

        await Assert
            .That(async () => await Fixture.Client.Streams.AppendStream(request, ct))
            .Throws<StreamAlreadyExistsException>();

        // FIXME SS: Fix Appends with ExpectedStreamCondition.NoStream not returning consistency violations.
        // This is clearly a bug because when we pass the no stream, it is gracefully returning that everything is okay
        // when in reality it didn't actually append anything, but it also didn't throw the expected exception.
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
        ExpectedStreamState streamState, CancellationToken ct
    ) {
        // Arrange
        var seeded = await Fixture.SeedTestStream(ct);

        await Fixture.Client.Streams.Tombstone(seeded.Stream, ct);

        // Act & Assert
        await Assert
            .That(async () => await Fixture.Client.Streams.AppendStream(
                AppendStreamRequest.For(seeded.Stream)
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
            .That(async () => await Fixture.Client.Streams.AppendStream(
                AppendStreamRequest.For(data.Stream)
                    .ExpectingState(ExpectedStreamCondition.Exists)
                    .WithRecords(data.Records)
                    .Build(), ct))
            .Throws<StreamNotFoundException>();
    }

    [Test]
    public async ValueTask throws_StreamRevisionConflictException_when_revision_mismatch(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(ct);

        var created = await Fixture.Client.Streams.AppendStream(
            AppendStreamRequest.For(data.Stream)
                .WithRecords(data.Records.Take(1))
                .Build(), ct);

        Revision wrongRevision = created.Revision + Random.Shared.Next(5, 100);

        var req = AppendStreamRequest.For(data.Stream)
            .ExpectingState(wrongRevision)
            .WithRecords(data.Records.Skip(1))
            .Build();

        // Act
        var ex = await Assert.ThrowsAsync<StreamRevisionConflictException>(
            async () => await Fixture.Client.Streams.AppendStream(req , ct));

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
            .ConfigureDataRetention(data.Stream, maxCount, ct);

        // Act
        await Fixture.Client.Streams.AppendStream(
            data.Stream,
            ExpectedStreamState.NoStream,
            data.Records,
            ct
        );

        // Assert
        var info = await Fixture.Client.Streams.GetStreamInfo(data.Stream, ct);
        await Assert.That(info.Retention.MaxCount).IsEqualTo(maxCount);

        var messages = Fixture.Client.Streams
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
        var maxAge = FromHours(1);
        var data   = await Fixture.TestData.GenerateSet(5, ct);

        await Fixture.Client.Streams
            .ConfigureDataRetention(data.Stream, maxAge, ct);

        // Act
        await Fixture.Client.Streams
            .AppendStream(AppendStreamRequest.For(data.Stream)
                .ExpectingState(ExpectedStreamCondition.NoStream)
                .WithRecords(data.Records)
                .Build(), ct);

        // Assert
        var info = await Fixture.Client.Streams.GetStreamInfo(data.Stream, ct);
        await Assert.That(info.Retention.HasMaxAge).IsTrue();
        await Assert.That(info.Retention.MaxAge).IsEqualTo(maxAge);

        var messages = Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct);

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        await Assert.That(records.Count).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask append_to_stream_with_combined_retention_policies(CancellationToken ct) {
        // Arrange

        var options = new StreamRetention() {
            MaxCount = 10,
            MaxAge   = FromMinutes(5)
        };

        var data = await Fixture.TestData.GenerateSet(options.MaxCount.Value, ct);

        await Fixture.Client.Streams
            .ConfigureDataRetention(data.Stream, options, ct);

        // Act
        await Fixture.Client.Streams
            .AppendStream(AppendStreamRequest.For(data.Stream)
                .ExpectingState(ExpectedStreamCondition.NoStream)
                .WithRecords(data.Records)
                .Build(), ct);

        // Assert
        var info = await Fixture.Client.Streams.GetStreamInfo(data.Stream, ct);
        await Assert.That(info.Retention).IsEqualTo(options);

        var messages = Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct);

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        await Assert.That(records.Count).IsLessThanOrEqualTo(options.MaxCount.Value + 1);
    }

    #endregion

    #region . Pre-Configured Streams .

    [Test]
    public async ValueTask create_stream_via_append_when_retention_preconfigured(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(3, ct);

        var options = new StreamRetention() { MaxCount = 5 };

        var expectedRevision = new StreamPosition(data.Stream, data.Records.Length - 1);

        await Fixture.Client.Streams
            .ConfigureDataRetention(data.Stream, options, ct);

        // Act
        var request = AppendStreamRequest.For(data.Stream)
            .ExpectingState(ExpectedStreamState.NoStream)
            .WithRecords(data.Records)
            .Build();

        var result = await Fixture.Client.Streams.AppendStream(request, ct);

        // Assert
        await Assert.That(new StreamPosition(result.Stream, result.Revision)).IsEqualTo(expectedRevision);

        var info = await Fixture.Client.Streams.GetStreamInfo(data.Stream, ct);

        await Assert.That(info.Retention.MaxCount).IsEqualTo(options.MaxCount);
        await Assert.That(info.Retention.MaxAge).IsEqualTo(options.MaxAge);
        await Assert.That(info.Retention).IsEquivalentTo(options);
    }

    #endregion
}
