using System.Text.Json;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Client.Tests.Tools;
using Kurrent.Testing.Shouldly;
using Kurrent.Testing.TUnit;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
public class AppendTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask appends_to_single_stream(CancellationToken ct) {
        var simulation = HomeAutomationTestData.SimulateHomeActivity();

        await Fixture.Client.Streams
            .AppendSession([simulation],ct)
            .ShouldNotThrowOrFailAsync(success => {
                success.Position.ShouldBeGreaterThan(LogPosition.Earliest);

                var appendStreamSuccess = success[0];

                appendStreamSuccess.Stream.ShouldBe(simulation.Stream);
                appendStreamSuccess.StreamRevision.ShouldBe(simulation.Messages.Count() - 1);
                appendStreamSuccess.Position.ShouldBeGreaterThan(LogPosition.Earliest);

                appendStreamSuccess.Position.ShouldBeEquivalentTo(success.Position);
            });
    }

    [Test]
    public async ValueTask creates_stream(CancellationToken ct) {
        var simulation = HomeAutomationTestData.SimulateHomeActivity();

        await Fixture.Client.Streams
            .Append(simulation, ct)
            .ShouldNotThrowOrFailAsync(success => {
                success.Stream.ShouldBe(simulation.Stream);
                success.StreamRevision.ShouldBe(simulation.Messages.Count() - 1);
                success.Position.ShouldBeGreaterThan(LogPosition.Earliest);
            });
    }

    [Test]
    public async ValueTask appends_to_existing_stream(CancellationToken ct) {
        var smartHomeActivity = await Fixture.SeedSmartHomeActivity(6, ct); // Ensure at least 6 records for Take(3) + Skip(3)

        // create stream
        var result = await Fixture.Client.Streams
            .Append(smartHomeActivity.Stream, ExpectedStreamState.NoStream, smartHomeActivity.Records.Take(3), ct)
            .ShouldNotThrowOrFailAsync();

        // append to existing stream
        await Fixture.Client.Streams
            .Append(smartHomeActivity.Stream, result.StreamRevision, smartHomeActivity.Records.Skip(3), ct)
            .ShouldNotThrowOrFailAsync(success => {
                success.Stream.ShouldBe(smartHomeActivity.Stream);
                success.StreamRevision.ShouldBe(smartHomeActivity.Records.Count() - 1);
                success.Position.ShouldBeGreaterThan(LogPosition.Earliest);
            });
    }

    public class AppendToStreamInAnyState : TestCaseGenerator<ExpectedStreamState> {
        protected override IEnumerable<ExpectedStreamState> Data() => [
            ExpectedStreamState.NoStream,
            ExpectedStreamState.StreamExists
        ];
    }

    [Test, AppendToStreamInAnyState]
    public async Task appends_to_stream_in_any_state(ExpectedStreamState expectedStreamState, CancellationToken ct) {
        var smartHomeActivity = await Fixture.SeedSmartHomeActivity(6, ct); // Ensure at least 6 records for Take(3) + Skip(3)

        if (ExpectedStreamState.NoStream == expectedStreamState) {
            var expectedRevision = StreamRevision.From(smartHomeActivity.Records.Count() - 1);

            // should create the stream
            await Fixture.Client.Streams
                .Append(smartHomeActivity.Stream, ExpectedStreamState.Any, smartHomeActivity.Records, ct)
                .ShouldNotThrowOrFailAsync(success => {
                    success.Stream.ShouldBe(smartHomeActivity.Stream);
                    success.StreamRevision.ShouldBe(expectedRevision);
                    success.Position.ShouldBeGreaterThan(LogPosition.Earliest);
                });
        }
        else if(ExpectedStreamState.StreamExists == expectedStreamState) {
            // create stream
            await Fixture.Client.Streams
                .Append(smartHomeActivity.Stream, ExpectedStreamState.NoStream, smartHomeActivity.Records.Take(3), ct)
                .ShouldNotThrowOrFailAsync();

            // append to existing stream
            await Fixture.Client.Streams
                .Append(smartHomeActivity.Stream, ExpectedStreamState.StreamExists, smartHomeActivity.Records.Skip(3), ct)
                .ShouldNotThrowOrFailAsync();
        }
    }

    public class CreatesMultipleStreamsTransactionallyState : TestCaseGenerator<int> {
        protected override IEnumerable<int> Data() => [
            2, 5, 6, 8, 10, 15, 20, 25, 30, 50, 100
        ];
    }

    [Test, CreatesMultipleStreamsTransactionallyState]
    public async Task creates_multiple_streams_transactionally(int streams, CancellationToken ct) {
        var activity = HomeAutomationTestData.SimulateHousingComplexActivity();

        var requests = activity
            .Select(act => act.AppendRequest)
            .ToList();

        await Fixture.Client.Streams
            .AppendSession(requests, ct)
            .ShouldNotThrowAsync()
            // .InspectErrorAsync(failure => {
            //     failure. .Count.ShouldBeGreaterThan(0);
            //     Should.NotThrow(() => failures..FirstOrDefault().Error.Throw());
            // })
            .InspectAsync(success => {
                success.Count.ShouldBe(requests.Count);

                for (var i = 0; i < requests.Count; i++) {
                    var request = requests[i];
                    var result  = success[i];

                    var expectedRevision = StreamRevision.From(request.Messages.Count() - 1);

                    result.Stream.ShouldBe(request.Stream);
                    result.StreamRevision.ShouldBe(expectedRevision);
                    result.Position.ShouldBeGreaterThan(LogPosition.Earliest);
                }
            });
    }

    #region . Retention Policies .

    [Test]
    public async ValueTask append_to_stream_with_maxcount_retention(CancellationToken ct) {
        // Arrange
        var streamName = $"stream-maxcount-{Guid.NewGuid():N}";
        var maxCount = 5;

        // Configure retention before appending
        await Fixture.Client.Streams
            .ConfigureStreamDataRetention(streamName, new() { MaxCount = maxCount }, ct)
            .ShouldNotThrowOrFailAsync();

        // Act - Append 10 records
        var activity = HomeAutomationTestData.SimulateHomeActivity(10);
        await Fixture.Client.Streams
            .Append(streamName, ExpectedStreamState.NoStream, activity.Messages, ct)
            .ShouldNotThrowOrFailAsync();

        // Assert - Verify retention enforced (only last 5 records should be kept)
        var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct).UnwrapAsync();
        await Assert.That(info.Metadata.MaxCount).IsEqualTo(maxCount);

        // Read all records - should only get last 5
        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(streamName, ct)
            .ShouldNotThrowOrFailAsync();

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
            .ConfigureStreamDataRetention(streamName, new() { MaxAge = maxAge }, ct)
            .ShouldNotThrowOrFailAsync();

        // Act - Append records
        var activity = HomeAutomationTestData.SimulateHomeActivity(5);
        await Fixture.Client.Streams
            .Append(streamName, ExpectedStreamState.NoStream, activity.Messages, ct)
            .ShouldNotThrowOrFailAsync();

        // Assert - Verify MaxAge retention policy is set
        var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct).UnwrapAsync();
        info.Metadata.HasMaxAge.ShouldBeTrue();
        info.Metadata.MaxAge.ShouldBe(maxAge);

        // Verify records are readable (retention doesn't block append/read)
        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(streamName, ct)
            .ShouldNotThrowOrFailAsync();

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // All records should be present (retention hasn't kicked in yet)
        records.Count.ShouldBe(5);
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
            }, ct)
            .ShouldNotThrowOrFailAsync();

        // Act - Append more than MaxCount
        var activity = HomeAutomationTestData.SimulateHomeActivity(20);
        await Fixture.Client.Streams
            .Append(streamName, ExpectedStreamState.NoStream, activity.Messages, ct)
            .ShouldNotThrowOrFailAsync();

        // Assert - MaxCount should take precedence (immediate effect)
        var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct).UnwrapAsync();
        await Assert.That(info.Metadata.MaxCount).IsEqualTo(maxCount);
        await Assert.That(info.Metadata.MaxAge).IsEqualTo(maxAge);

        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(streamName, ct)
            .ShouldNotThrowOrFailAsync();

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
            .Append(streamName, ExpectedStreamState.NoStream, activity.Messages, ct)
            .ShouldNotThrowOrFailAsync();

        // Act & Assert - Attempt to append with NoStream expectation
        var activity2 = HomeAutomationTestData.SimulateHomeActivity(1);
        await Fixture.Client.Streams
            .Append(streamName, ExpectedStreamState.NoStream, activity2.Messages, ct)
            .ShouldFailAsync(error => {
                error.Value.ShouldBeOfType<StreamRevisionConflictException>();
            });
    }

    [Test]
    public async ValueTask append_with_streamexists_fails_when_stream_does_not_exist(CancellationToken ct) {
        // Arrange
        var streamName = $"stream-missing-{Guid.NewGuid():N}";

        // Act & Assert - Attempt to append with StreamExists expectation
        // Note: ExpectedStreamState.StreamExists on missing stream throws StreamRevisionConflictException, not StreamNotFoundException
        var activity = HomeAutomationTestData.SimulateHomeActivity(1);
        await Fixture.Client.Streams
            .Append(streamName, ExpectedStreamState.StreamExists, activity.Messages, ct)
            .ShouldFailAsync(error => {
                error.Value.ShouldBeOfType<StreamRevisionConflictException>();
            });
    }

    [Test]
    public async ValueTask append_with_specific_revision_fails_on_mismatch(CancellationToken ct) {
        // Arrange
        var streamName = $"stream-revision-{Guid.NewGuid():N}";

        // Create stream with 3 records (revision will be 2)
        var activity1 = HomeAutomationTestData.SimulateHomeActivity(3);
        await Fixture.Client.Streams
            .Append(streamName, ExpectedStreamState.NoStream, activity1.Messages, ct)
            .ShouldNotThrowOrFailAsync();

        // Act & Assert - Attempt to append expecting wrong revision
        var activity2 = HomeAutomationTestData.SimulateHomeActivity(1);
        await Fixture.Client.Streams
            .Append(streamName, StreamRevision.From(5), activity2.Messages, ct)
            .ShouldFailAsync(error => {
                var ex = error.Value as StreamRevisionConflictException;
                ex.ShouldNotBeNull();
                ex.ExpectedRevision.ShouldBe(StreamRevision.From(5));
                ex.ActualRevision.ShouldBe(StreamRevision.From(2));
            });
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
            .SetStreamMetadata(streamName, new StreamMetadata { CustomMetadata = customJson }, ct)
            .ShouldNotThrowOrFailAsync();

        // Act - Append records
        var activity = HomeAutomationTestData.SimulateHomeActivity(5);
        await Fixture.Client.Streams
            .Append(streamName, ExpectedStreamState.NoStream, activity.Messages, ct)
            .ShouldNotThrowOrFailAsync();

        // Assert - Custom metadata should still exist
        var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct).UnwrapAsync();
        info.Metadata.CustomMetadata.ShouldNotBeNull();

        var tenant = info.Metadata.CustomMetadata.RootElement.GetProperty("tenant").GetString();
        tenant.ShouldBe("acme");
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
            }, ct)
            .ShouldNotThrowOrFailAsync();

        // Act - Append records
        var activity = HomeAutomationTestData.SimulateHomeActivity(5);
        await Fixture.Client.Streams
            .Append(streamName, ExpectedStreamState.NoStream, activity.Messages, ct)
            .ShouldNotThrowOrFailAsync();

        // Assert - CacheControl should be preserved
        var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct).UnwrapAsync();
        info.Metadata.CacheControl.ShouldBe(cacheControl);
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
            .ConfigureStreamDataRetention(streamName, new() { MaxCount = maxCount }, ct)
            .ShouldNotThrowOrFailAsync();

        // Act - Create stream via first append (using NoStream)
        var activity = HomeAutomationTestData.SimulateHomeActivity(3);
        await Fixture.Client.Streams
            .Append(streamName, ExpectedStreamState.NoStream, activity.Messages, ct)
            .ShouldNotThrowOrFailAsync(result => {
                result.StreamRevision.ShouldBe(StreamRevision.From(2)); // 3 records = revision 2
            });

        // Assert - Stream created with retention already active
        var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct).UnwrapAsync();
        info.Metadata.MaxCount.ShouldBe(maxCount);
    }

    #endregion
}
