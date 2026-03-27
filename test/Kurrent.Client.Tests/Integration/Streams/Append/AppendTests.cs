using System.Text.Json;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Testing.ArcadeRacer;
using Kurrent.Testkit;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
public class AppendTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.None)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask creates_stream(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(ct);

        var expectedRevision = new StreamRevision(data.Stream, data.Records.Count - 1);

        // Act
        var result = await Fixture.Client.Streams
            .AppendRecords(data.AppendRequest, ct);

        // Assert
        await Assert.That(result.Revisions[0]).IsEqualTo(expectedRevision);
    }

    [Test]
    public async ValueTask appends_to_existing_stream(CancellationToken ct) {
        // Arrange
        var data             = await Fixture.TestData.GenerateSet(ct);
        var expectedRevision = new StreamRevision(data.Stream, data.Records.Count - 1);

        var created = await Fixture.Client.Streams
            .AppendRecords(data.Stream, ExpectedStreamCondition.NoStream, data.Records.Take(1), ct);

        // Act — append to existing stream
        var result = await Fixture.Client.Streams
            .AppendRecords(data.Stream, created.Revisions[0].Revision, data.Records.Skip(1), ct);

        // Assert
        await Assert.That(result.Position).IsGreaterThan(created.Position);
        await Assert.That(result.Revisions[0]).IsEqualTo(expectedRevision);
    }

    [Test]
    public async ValueTask appends_to_existing_stream_with_expected_revision(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(ct);

        var expectedRevision = new StreamRevision(data.Stream, data.Records.Count - 1);

        var created = await Fixture.Client.Streams
            .AppendRecords(data.Stream, ExpectedStreamCondition.NoStream, data.Records.Take(3), ct);

        // Act
        var result = await Fixture.Client.Streams
            .AppendRecords(data.Stream, created.Revisions[0].Revision, data.Records.Skip(3), ct);

        // Assert
        await Assert.That(result.Position).IsGreaterThan(created.Position);
        await Assert.That(result.Revisions[0]).IsEqualTo(expectedRevision);
    }

    [Test]
    public async ValueTask appends_to_existing_stream_with_any_state(CancellationToken ct) {
        // Arrange
        var seededData = await Fixture.SeedData(6, ct);
        //var data         = await Fixture.TestDataSource.GenerateSet(ct);

        var expectedRevision = new StreamRevision(seededData.Stream, seededData.Records.Count - 1);

        await Fixture.Client.Streams
            .AppendRecords(seededData.Stream, ExpectedStreamCondition.NoStream, seededData.Records.Take(3), ct);

        // Act
        var result = await Fixture.Client.Streams
            .AppendRecords(seededData.Stream, ExpectedStreamCondition.Exists, seededData.Records.Skip(3), ct);

        // Assert
        await Assert.That(result.Position).IsGreaterThan(LogPosition.Earliest);
        await Assert.That(result.Revisions[0]).IsEqualTo(expectedRevision);
    }

    [Test]
    public async ValueTask append_with_nostream_fails_when_stream_exists(CancellationToken ct) {
        // Arrange
        var seeded = await Fixture.SeedData(ct);
        var data   = await Fixture.TestData.GenerateSet(ct);

        // Act & Assert
        var result = await Assert.That(async () => await Fixture.Client.Streams
            .AppendRecords(seeded.Stream, ExpectedStreamCondition.NoStream, data.Records, ct))
            .Throws<AppendConsistencyViolationException>();
    }

    // [Test]
    // public async ValueTask append_with_streamexists_fails_when_stream_does_not_exist(CancellationToken ct) {
    //     // Arrange
    //     var data = ArcadeRacerTestData.SimulateRace(1);
    //
    //     // Act & Assert
    //     await Assert.That(async () => await Fixture.Client.Streams
    //         .AppendRecords(data.Stream, ExpectedStreamCondition.Exists, data.Records, ct))
    //         .Throws<AppendConsistencyViolationException>();
    // }

    [Test]
    public async ValueTask throws_StreamDeletedException_when_stream_is_soft_deleted(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(ct);

        // Act & Assert
        await Assert
            .That(Action)
            .Throws<StreamDeletedException>();

        Func<Task<AppendRecordsSuccess?>> Action() => async () =>
            await Fixture.Client.Streams.AppendRecords(
                data.Stream, ExpectedStreamCondition.Exists, data.Records, ct);
    }

    [Test]
    public async ValueTask throws_StreamTombstonedException_when_stream_is_tombstoned(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(ct);

        // Act & Assert
        await Assert
            .That(async () => await Fixture.Client.Streams.AppendRecords(
                data.Stream, ExpectedStreamCondition.Exists, data.Records, ct))
            .Throws<StreamTombstonedException>();
    }

    [Test]
    public async ValueTask throws_StreamNotFoundException_when_expected_to_exist(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(ct);

        // Act & Assert
        await Assert
            .That(async() => await Fixture.Client.Streams.AppendRecords(
                data.Stream, ExpectedStreamCondition.Exists, data.Records, ct))
            .Throws<StreamNotFoundException>();
    }

    [Test]
    public async ValueTask
    throws_AppendConsistencyViolationException_when_on_revision_mismatch(CancellationToken
        ct) {
        // Arrange
        var seeded        = await Fixture.SeedData(3, ct);
        var wrongRevision = Revision.From(seeded.Revision + 3);
        var data          = await Fixture.TestData.GenerateSet(ct);

        // Act
        var ex = await Assert.ThrowsAsync<AppendConsistencyViolationException>(async () => await Fixture.Client.Streams.AppendRecords(seeded.Stream, wrongRevision, data.Records, ct));

        // Assert
        await Assert.That(ex).IsNotNull();
        var violation = ex.Violations[0].AsStreamStateViolation;
        await Assert.That(violation.ExpectedState).IsEqualTo((ExpectedStreamState)wrongRevision);
        await Assert.That(violation.ActualState).IsEqualTo((ActualStreamState)seeded.Revision);
    }

    // public class CreatesMultipleStreamsTransactionallyState : TestCaseGenerator<int> {
    //     protected override IEnumerable<int> Data() => [
    //         2, 5, 6, 8, 10, 15, 20, 25, 30, 50, 100
    //     ];
    // }
    //
    // [Test, CreatesMultipleStreamsTransactionallyState]
    // public async Task creates_multiple_streams_transactionally(int streams, CancellationToken ct) {
    //     // Arrange
    //     var activities = ArcadeRacerTestData.SimulateRaces();
    //     var builder    = AppendRecordsRequest.New();
    //
    //     foreach (var act in activities) {
    //         builder.WithRecords(act.Records.ToArray());
    //         builder.WithCheck(ConsistencyCheck.Stream(act.Stream).DoesNotExist());
    //     }
    //
    //     var request = builder.Build();
    //
    //     // Act
    //     var result = await Fixture.Client.Streams
    //         .AppendRecords(request, ct);
    //
    //     // Assert
    //     await Assert.That(result.Position).IsGreaterThan(LogPosition.Earliest);
    // }

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
            .AppendRecords(data.Stream, ExpectedStreamCondition.NoStream, data.Records, ct);

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
            .AppendRecords(data.Stream, ExpectedStreamCondition.NoStream, data.Records, ct);

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
            .AppendRecords(data.Stream, ExpectedStreamCondition.NoStream, data.Records, ct);

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

    #region . Metadata Preservation .

    [Test]
    public async ValueTask append_preserves_custom_metadata(CancellationToken ct) {
        // Arrange
        var expectedTenant = "acme";
        var customJson     = JsonDocument.Parse("""{"tenant": "acme", "region": "us-west"}""");
        var data           = await Fixture.TestData.GenerateSet(5, ct);

        await Fixture.Client.Streams
            .SetStreamMetadata(data.Stream, new StreamMetadata { CustomMetadata = customJson }, ct);

        // Act
        await Fixture.Client.Streams
            .AppendRecords(data.Stream, ExpectedStreamCondition.NoStream, data.Records, ct);

        // Assert
        var info = await Fixture.Client.Streams.GetStreamInfo(data.Stream, ct);
        await Assert.That(info.Metadata.CustomMetadata).IsNotNull();

        var tenant = info.Metadata.CustomMetadata.RootElement.GetProperty("tenant").GetString();
        await Assert.That(tenant).IsEqualTo(expectedTenant);
    }

    [Test]
    public async ValueTask append_preserves_cachecontrol_metadata(CancellationToken ct) {
        // Arrange
        var data = await Fixture.TestData.GenerateSet(5, ct);

        var expectedDuration = TimeSpan.FromHours(2);

        await Fixture.Client.Streams
            .SetStreamMetadata(data.Stream, new StreamMetadata {
                CacheControl = expectedDuration
            }, ct);

        // Act
        await Fixture.Client.Streams
            .AppendRecords(data.Stream, ExpectedStreamCondition.NoStream, data.Records, ct);

        // Assert
        var info = await Fixture.Client.Streams.GetStreamInfo(data.Stream, ct);
        await Assert.That(info.Metadata.CacheControl).IsEqualTo(expectedDuration);
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
            .AppendRecords(data.Stream, ExpectedStreamCondition.NoStream, data.Records, ct);

        // Assert
        await Assert.That(result.Revisions[0]).IsEqualTo(expectedRevision);

        var info = await Fixture.Client.Streams.GetStreamInfo(data.Stream, ct);
        await Assert.That(info.Metadata.MaxCount).IsEqualTo(maxCount);
    }

    #endregion
}
