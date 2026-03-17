using System.Text.Json;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Client.Tests.Tools;
using Kurrent.Testing.Assertions;
using Kurrent.Testing.TUnit;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
public class AppendTests {

    static AppendTests() => Environment.SetEnvironmentVariable("TestContainers:KurrentDB:Enabled", "false");

    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.None)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask appends_to_single_stream(CancellationToken ct) {
        var simulation = HomeAutomationTestData.SimulateHomeActivity();

        var success = await Fixture.Client.Streams
            .AppendRecords(simulation.AppendRequest, ct);

        await Assert.That(success.Position).IsGreaterThan(LogPosition.Earliest);
        await Assert.That(success.Revisions[0].Stream).IsEqualTo(simulation.Stream);
        await Assert.That(success.Revisions[0].Revision).IsEqualTo(simulation.Records.Count() - 1);
    }

    [Test]
    public async ValueTask creates_stream(CancellationToken ct) {
        var simulation = HomeAutomationTestData.SimulateHomeActivity();

        var success = await Fixture.Client.Streams
            .AppendRecords(simulation.AppendRequest, ct);

        await Assert.That(success.Revisions[0].Stream).IsEqualTo(simulation.Stream);
        await Assert.That(success.Revisions[0].Revision).IsEqualTo(simulation.Records.Count() - 1);
        await Assert.That(success.Position).IsGreaterThan(LogPosition.Earliest);
    }

    [Test]
    public async ValueTask appends_to_existing_stream(CancellationToken ct) {
        var smartHomeActivity = await Fixture.SeedSmartHomeActivity(6, ct); // Ensure at least 6 records for Take(3) + Skip(3)

        // create stream
        var result = await Fixture.Client.Streams
            .AppendRecords(smartHomeActivity.Stream, ExpectedStreamCondition.NoStream, smartHomeActivity.Records.Take(3), ct);

        // append to existing stream
        var success = await Fixture.Client.Streams
            .AppendRecords(smartHomeActivity.Stream, result.Revisions[0].Revision, smartHomeActivity.Records.Skip(3), ct);

        await Assert.That(success.Revisions[0].Stream).IsEqualTo(smartHomeActivity.Stream);
        await Assert.That(success.Revisions[0].Revision).IsEqualTo(smartHomeActivity.Records.Count() - 1);
        await Assert.That(success.Position).IsGreaterThan(LogPosition.Earliest);
    }

    public class AppendToStreamInAnyState : TestCaseGenerator<ExpectedStreamState> {
        protected override IEnumerable<ExpectedStreamState> Data() => [
            ExpectedStreamCondition.NoStream,
            ExpectedStreamCondition.Exists
        ];
    }

    [Test, AppendToStreamInAnyState]
    public async Task appends_to_stream_in_any_state(ExpectedStreamState expectedStreamState, CancellationToken ct) {
        var smartHomeActivity = await Fixture.SeedSmartHomeActivity(6, ct); // Ensure at least 6 records for Take(3) + Skip(3)

        if (expectedStreamState == (ExpectedStreamState)ExpectedStreamCondition.NoStream) {
            var expectedRevision = Revision.From(smartHomeActivity.Records.Count() - 1);

            // should create the stream
            var success = await Fixture.Client.Streams
                .AppendRecords(smartHomeActivity.Stream, ExpectedStreamState.Any, smartHomeActivity.Records, ct);

            await Assert.That(success.Revisions[0].Stream).IsEqualTo(smartHomeActivity.Stream);
            await Assert.That(success.Revisions[0].Revision).IsEqualTo(expectedRevision);
            await Assert.That(success.Position).IsGreaterThan(LogPosition.Earliest);
        }
        else if(expectedStreamState == (ExpectedStreamState)ExpectedStreamCondition.Exists) {
            // create stream
            await Fixture.Client.Streams
                .AppendRecords(smartHomeActivity.Stream, ExpectedStreamCondition.NoStream, smartHomeActivity.Records.Take(3), ct);

            // append to existing stream
            await Fixture.Client.Streams
                .AppendRecords(smartHomeActivity.Stream, ExpectedStreamCondition.Exists, smartHomeActivity.Records.Skip(3), ct);
        }
    }

    public class CreatesMultipleStreamsTransactionallyState : TestCaseGenerator<int> {
        protected override IEnumerable<int> Data() => [
            2, 5, 6, 8, 10, 15, 20, 25, 30, 50, 100
        ];
    }

    [Test, CreatesMultipleStreamsTransactionallyState]
    public async Task creates_multiple_streams_transactionally(int streams, CancellationToken ct) {
        var activities = HomeAutomationTestData.SimulateHousingComplexActivity();

        var builder = AppendRecordsRequest.New();

        foreach (var act in activities) {
            builder.WithRecords(act.Records.ToArray());
            builder.WithCheck(ConsistencyCheck.Stream(act.Stream).DoesNotExist());
        }

        var request = builder.Build();

        var success = await Fixture.Client.Streams
            .AppendRecords(request, ct);

        await Assert.That(success.Position).IsGreaterThan(LogPosition.Earliest);
    }

    #region . Retention Policies .

    [Test]
    public async ValueTask append_to_stream_with_maxcount_retention(CancellationToken ct) {
        // Arrange
        var streamName = $"stream-maxcount-{Guid.NewGuid():N}";
        var maxCount = 5;

        // Configure retention before appending
        await Fixture.Client.Streams
            .ConfigureStreamDataRetention(streamName, new() { MaxCount = maxCount }, ct);

        // Act - Append 10 records
        var activity = HomeAutomationTestData.SimulateHomeActivity(10);
        await Fixture.Client.Streams
            .AppendRecords(streamName, ExpectedStreamCondition.NoStream, activity.Records, ct);

        // Assert - Verify retention enforced (only last 5 records should be kept)
        var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct);
        await Assert.That(info.Metadata.MaxCount).IsEqualTo(maxCount);

        // Read all records - should only get last 5
        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(streamName, ct);

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        await Assert.That(records.Count).IsLessThanOrEqualTo(maxCount + 1); // +1 for potential timing
    }

    [Test]
    public async ValueTask append_to_stream_with_maxage_retention(CancellationToken ct) {
        // Arrange
        var streamName = $"stream-maxage-{Guid.NewGuid():N}";
        var maxAge = TimeSpan.FromHours(1);

        // Configure retention with MaxAge
        await Fixture.Client.Streams
            .ConfigureStreamDataRetention(streamName, new() { MaxAge = maxAge }, ct);

        // Act - Append records
        var activity = HomeAutomationTestData.SimulateHomeActivity(5);
        await Fixture.Client.Streams
            .AppendRecords(streamName, ExpectedStreamCondition.NoStream, activity.Records, ct);

        // Assert - Verify MaxAge retention policy is set
        var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct);
        await Assert.That(info.Metadata.HasMaxAge).IsTrue();
        await Assert.That(info.Metadata.MaxAge).IsEqualTo(maxAge);

        // Verify records are readable (retention doesn't block append/read)
        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(streamName, ct);

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // All records should be present (retention hasn't kicked in yet)
        await Assert.That(records.Count).IsEqualTo(5);
    }

    [Test]
    public async ValueTask append_to_stream_with_combined_retention_policies(CancellationToken ct) {
        // Arrange
        var streamName = $"stream-combined-{Guid.NewGuid():N}";
        var maxCount = 10;
        var maxAge = TimeSpan.FromMinutes(5);

        // Configure both retention policies
        await Fixture.Client.Streams
            .ConfigureStreamDataRetention(streamName, new() {
                MaxCount = maxCount,
                MaxAge = maxAge
            }, ct);

        // Act - Append more than MaxCount
        var activity = HomeAutomationTestData.SimulateHomeActivity(20);
        await Fixture.Client.Streams
            .AppendRecords(streamName, ExpectedStreamCondition.NoStream, activity.Records, ct);

        // Assert - MaxCount should take precedence (immediate effect)
        var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct);
        await Assert.That(info.Metadata.MaxCount).IsEqualTo(maxCount);
        await Assert.That(info.Metadata.MaxAge).IsEqualTo(maxAge);

        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(streamName, ct);

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        await Assert.That(records.Count).IsLessThanOrEqualTo(maxCount + 1);
    }

    #endregion

    #region . ExpectedStreamState Validation .

    [Test]
    public async ValueTask append_with_nostream_fails_when_stream_exists(CancellationToken ct) {
        // Arrange
        var streamName = $"stream-exists-{Guid.NewGuid():N}";

        // Create stream first
        var activity = HomeAutomationTestData.SimulateHomeActivity(1);
        await Fixture.Client.Streams
            .AppendRecords(streamName, ExpectedStreamCondition.NoStream, activity.Records, ct);

        // Act & Assert - Attempt to append with NoStream expectation
        var activity2 = HomeAutomationTestData.SimulateHomeActivity(1);
        await Assert.That(async () => await Fixture.Client.Streams
            .AppendRecords(streamName, ExpectedStreamCondition.NoStream, activity2.Records, ct))
            .Throws<AppendConsistencyViolationException>();
    }

    [Test]
    public async ValueTask append_with_streamexists_fails_when_stream_does_not_exist(CancellationToken ct) {
        // Arrange
        var streamName = $"stream-missing-{Guid.NewGuid():N}";

        // Act & Assert - Attempt to append with StreamExists expectation
        var activity = HomeAutomationTestData.SimulateHomeActivity(1);
        await Assert.That(async () => await Fixture.Client.Streams
            .AppendRecords(streamName, ExpectedStreamCondition.Exists, activity.Records, ct))
            .Throws<AppendConsistencyViolationException>();
    }

    [Test]
    public async ValueTask append_with_specific_revision_fails_on_mismatch(CancellationToken ct) {
        // Arrange
        var streamName = $"stream-revision-{Guid.NewGuid():N}";

        // Create stream with 3 records (revision will be 2)
        var activity1 = HomeAutomationTestData.SimulateHomeActivity(3);
        await Fixture.Client.Streams
            .AppendRecords(streamName, ExpectedStreamCondition.NoStream, activity1.Records, ct);

        // Act & Assert - Attempt to append expecting wrong revision
        var activity2 = HomeAutomationTestData.SimulateHomeActivity(1);
        var ex = await Assert.ThrowsAsync<AppendConsistencyViolationException>(
            async () => await Fixture.Client.Streams
                .AppendRecords(streamName, Revision.From(5), activity2.Records, ct));

        await Assert.That(ex).IsNotNull();
        var violation = ex.Violations[0].AsStreamStateViolation;
        await Assert.That(violation.ExpectedState).IsEqualTo((ExpectedStreamState)Revision.From(5));
        await Assert.That(violation.ActualState).IsEqualTo((ActualStreamState)Revision.From(2));
    }

    #endregion

    #region . Metadata Preservation .

    [Test]
    public async ValueTask append_preserves_custom_metadata(CancellationToken ct) {
        // Arrange
        var streamName = $"stream-custommeta-{Guid.NewGuid():N}";
        var customJson = JsonDocument.Parse("""{"tenant": "acme", "region": "us-west"}""");

        // Set metadata with custom data
        await Fixture.Client.Streams
            .SetStreamMetadata(streamName, new StreamMetadata { CustomMetadata = customJson }, ct);

        // Act - Append records
        var activity = HomeAutomationTestData.SimulateHomeActivity(5);
        await Fixture.Client.Streams
            .AppendRecords(streamName, ExpectedStreamCondition.NoStream, activity.Records, ct);

        // Assert - Custom metadata should still exist
        var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct);
        await Assert.That(info.Metadata.CustomMetadata).IsNotNull();

        var tenant = info.Metadata.CustomMetadata.RootElement.GetProperty("tenant").GetString();
        await Assert.That(tenant).IsEqualTo("acme");
    }

    [Test]
    public async ValueTask append_preserves_cachecontrol_metadata(CancellationToken ct) {
        // Arrange
        var streamName = $"stream-cachecontrol-{Guid.NewGuid():N}";
        var cacheControl = TimeSpan.FromHours(2);

        // Set CacheControl metadata
        await Fixture.Client.Streams
            .SetStreamMetadata(streamName, new StreamMetadata {
                CacheControl = cacheControl
            }, ct);

        // Act - Append records
        var activity = HomeAutomationTestData.SimulateHomeActivity(5);
        await Fixture.Client.Streams
            .AppendRecords(streamName, ExpectedStreamCondition.NoStream, activity.Records, ct);

        // Assert - CacheControl should be preserved
        var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct);
        await Assert.That(info.Metadata.CacheControl).IsEqualTo(cacheControl);
    }

    #endregion

    #region . Pre-Configured Streams .

    [Test]
    public async ValueTask create_stream_via_append_when_retention_preconfigured(CancellationToken ct) {
        // Arrange
        var streamName = $"stream-precfg-{Guid.NewGuid():N}";
        var maxCount = 5;

        // Pre-configure retention on non-existent stream
        await Fixture.Client.Streams
            .ConfigureStreamDataRetention(streamName, new() { MaxCount = maxCount }, ct);

        // Act - Create stream via first append (using NoStream)
        var activity = HomeAutomationTestData.SimulateHomeActivity(3);
        var result = await Fixture.Client.Streams
            .AppendRecords(streamName, ExpectedStreamCondition.NoStream, activity.Records, ct);

        await Assert.That(result.Revisions[0].Revision).IsEqualTo(Revision.From(2)); // 3 records = revision 2

        // Assert - Stream created with retention already active
        var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct);
        await Assert.That(info.Metadata.MaxCount).IsEqualTo(maxCount);
    }

    #endregion
}
