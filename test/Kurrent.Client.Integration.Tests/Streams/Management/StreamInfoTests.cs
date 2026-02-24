using System.Text.Json;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Client.Tests.Tools;
using Kurrent.Testing.Shouldly;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("StreamInfo")]
[Category("Integration")]
public class StreamInfoTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask returns_existing_stream_info(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var customMetadata = JsonDocument.Parse("""{"key1":"value1"}""");

        var meta = new StreamMetadata {
            MaxAge         = TimeSpan.FromDays(1),
            TruncateBefore = null,
            CacheControl   = TimeSpan.FromHours(1),
            MaxCount       = 100,
            CustomMetadata = customMetadata
        };

        await Fixture.Client.Streams
            .SetStreamMetadata(simulation.Stream, meta, ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Streams
            .GetStreamInfo(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync(info => {
                info.Metadata.ShouldBeEquivalentTo(meta, x => x.Using<JsonDocument>((left, right) => left.ToString() == right.ToString()));

                // Other properties
                info.MetadataRevision.ShouldBe(0);
                info.LastStreamPosition.ShouldBeGreaterThan(LogPosition.Earliest);
                info.LastRevision.ShouldBe(simulation.Revision);
                info.LastStreamAppendTime.ShouldBeGreaterThan(DateTime.MinValue);
                info.State.ShouldBe(StreamState.Active);
            });
    }

    [Test]
    public async ValueTask returns_stream_info_with_empty_metadata_when_no_metadata_exists(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .GetStreamInfo(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync(info => {
                info.HasMetadata.ShouldBeFalse();
                info.MetadataRevision.ShouldBe(Revision.Unset);
                info.LastStreamPosition.ShouldBeGreaterThan(LogPosition.Earliest);
                info.LastRevision.ShouldBe(simulation.Revision);
                info.LastStreamAppendTime.ShouldBeGreaterThan(DateTime.MinValue);
                info.State.ShouldBe(StreamState.Active);
            });
    }

    [Test]
    public async ValueTask returns_stream_info_when_stream_does_not_exist(CancellationToken ct) {
        await Fixture.Client.Streams
            .GetStreamInfo("does_not_exist", ct)
            .ShouldNotThrowOrFailAsync(info => {
                info.HasMetadata.ShouldBeFalse();
                info.State.ShouldBe(StreamState.Missing);
            });
    }

    [Test]
    public async ValueTask returns_stream_info_when_stream_is_deleted(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Delete(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Streams
            .GetStreamInfo(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync(info => {
                info.HasMetadata.ShouldBeTrue();
                info.State.ShouldBe(StreamState.Deleted);
            });
    }

    [Test]
    public async ValueTask returns_stream_info_when_stream_is_tombstoned(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Streams
            .GetStreamInfo(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync(info => {
                info.HasMetadata.ShouldBeFalse();
                info.State.ShouldBe(StreamState.Tombstoned);
            });
    }

    [Test]
    public async ValueTask sets_stream_metadata_when_stream_exists(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var customMetadata = JsonDocument.Parse("""{ "key1": "value1" }""");

        var meta = new StreamMetadata {
            MaxAge         = TimeSpan.FromDays(1),
            TruncateBefore = null,
            CacheControl   = TimeSpan.FromHours(1),
            MaxCount       = 100,
            CustomMetadata = customMetadata
        };

        await Fixture.Client.Streams
            .SetStreamMetadata(simulation.Stream, meta, ct)
            .ShouldNotThrowOrFailAsync(revision => revision.ShouldBe(0));
    }

    [Test]
    public async ValueTask sets_stream_metadata_when_stream_does_not_exist(CancellationToken ct) {
        var simulation = HomeAutomationTestData.SimulateHomeActivity(1);

        var stream = simulation.Stream;

        var meta = new StreamMetadata {
            MaxAge         = TimeSpan.FromDays(1),
            TruncateBefore = null,
            CacheControl   = TimeSpan.FromHours(1),
            MaxCount       = 100,
            CustomMetadata = JsonDocument.Parse("""{"key1":"value1"}""")
        };

        await Fixture.Client.Streams
            .SetStreamMetadata(stream, meta, ct)
            .ShouldNotThrowOrFailAsync(revision => revision.ShouldBe(0));

        await Fixture.Client.Streams
            .GetStreamInfo(stream, ct)
            .ShouldNotThrowOrFailAsync(info => {
                info.Metadata.ShouldBeEquivalentTo(meta, x => x.Using<JsonDocument>((l, r) => l.ToString() == r.ToString()));
                info.State.ShouldBe(StreamState.Missing);
            });
    }

    [Test]
    public async ValueTask sets_stream_metadata_when_stream_is_deleted(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Delete(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        var meta = new StreamMetadata {
            MaxAge = TimeSpan.FromDays(1),
        };

        await Fixture.Client.Streams
            .SetStreamMetadata(simulation.Stream, meta, ct)
            .ShouldNotThrowOrFailAsync(revision => revision.ShouldBe(1));
    }

    [Test]
    public async ValueTask fails_to_set_stream_metadata_when_stream_is_tombstoned(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        var meta = new StreamMetadata {
            MaxAge         = TimeSpan.FromDays(1),
            TruncateBefore = null,
            CacheControl   = TimeSpan.FromHours(1),
            MaxCount       = 100
        };

        await Fixture.Client.Streams
            .SetStreamMetadata(simulation.Stream, meta, ct)
            .ShouldFailAsync(error => error.Value.ShouldBeOfType<StreamTombstonedException>());
    }
}
