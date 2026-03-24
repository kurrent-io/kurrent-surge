using System.Text.Json;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Testing.ArcadeRacer;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("StreamInfo")]
[Category("Integration")]
public class StreamInfoTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask returns_existing_stream_info(CancellationToken ct) {
        var simulation = await Fixture.SeedRace(ct);

        var customMetadata = JsonDocument.Parse("""{"key1":"value1"}""");

        var meta = new StreamMetadata {
            MaxAge         = TimeSpan.FromDays(1),
            TruncateBefore = null,
            CacheControl   = TimeSpan.FromHours(1),
            MaxCount       = 100,
            CustomMetadata = customMetadata
        };

        await Fixture.Client.Streams
            .SetStreamMetadata(simulation.Stream, meta, ct);

        var info = await Fixture.Client.Streams
            .GetStreamInfo(simulation.Stream, ct);

        await Assert.That(info.Metadata.MaxAge).IsEqualTo(meta.MaxAge);
        await Assert.That(info.Metadata.TruncateBefore).IsEqualTo(meta.TruncateBefore);
        await Assert.That(info.Metadata.CacheControl).IsEqualTo(meta.CacheControl);
        await Assert.That(info.Metadata.MaxCount).IsEqualTo(meta.MaxCount);
        await Assert.That(info.Metadata.CustomMetadata?.RootElement.GetRawText()).IsEqualTo(meta.CustomMetadata?.RootElement.GetRawText());

        // Other properties
        await Assert.That(info.MetadataRevision).IsEqualTo(0);
        await Assert.That(info.LastStreamPosition).IsGreaterThan(LogPosition.Earliest);
        await Assert.That(info.LastRevision).IsEqualTo(simulation.Revision);
        await Assert.That(info.LastStreamAppendTime).IsGreaterThan(DateTime.MinValue);
        await Assert.That(info.State).IsEqualTo(StreamState.Active);
    }

    [Test]
    public async ValueTask returns_stream_info_with_empty_metadata_when_no_metadata_exists(CancellationToken ct) {
        var simulation = await Fixture.SeedRace(ct);

        var info = await Fixture.Client.Streams
            .GetStreamInfo(simulation.Stream, ct);

        await Assert.That(info.HasMetadata).IsFalse();
        await Assert.That(info.MetadataRevision).IsEqualTo(Revision.Unset);
        await Assert.That(info.LastStreamPosition).IsGreaterThan(LogPosition.Earliest);
        await Assert.That(info.LastRevision).IsEqualTo(simulation.Revision);
        await Assert.That(info.LastStreamAppendTime).IsGreaterThan(DateTime.MinValue);
        await Assert.That(info.State).IsEqualTo(StreamState.Active);
    }

    [Test]
    public async ValueTask returns_stream_info_when_stream_does_not_exist(CancellationToken ct) {
        var info = await Fixture.Client.Streams
            .GetStreamInfo("does_not_exist", ct);

        await Assert.That(info.HasMetadata).IsFalse();
        await Assert.That(info.State).IsEqualTo(StreamState.Missing);
    }

    [Test]
    public async ValueTask returns_stream_info_when_stream_is_deleted(CancellationToken ct) {
        var simulation = await Fixture.SeedRace(ct);

        await Fixture.Client.Streams
            .Delete(simulation.Stream, ct);

        var info = await Fixture.Client.Streams
            .GetStreamInfo(simulation.Stream, ct);

        await Assert.That(info.HasMetadata).IsTrue();
        await Assert.That(info.State).IsEqualTo(StreamState.Deleted);
    }

    [Test]
    public async ValueTask returns_stream_info_when_stream_is_tombstoned(CancellationToken ct) {
        var simulation = await Fixture.SeedRace(ct);

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, ct);

        var info = await Fixture.Client.Streams
            .GetStreamInfo(simulation.Stream, ct);

        await Assert.That(info.HasMetadata).IsFalse();
        await Assert.That(info.State).IsEqualTo(StreamState.Tombstoned);
    }

    [Test]
    public async ValueTask sets_stream_metadata_when_stream_exists(CancellationToken ct) {
        var simulation = await Fixture.SeedRace(ct);

        var customMetadata = JsonDocument.Parse("""{ "key1": "value1" }""");

        var meta = new StreamMetadata {
            MaxAge         = TimeSpan.FromDays(1),
            TruncateBefore = null,
            CacheControl   = TimeSpan.FromHours(1),
            MaxCount       = 100,
            CustomMetadata = customMetadata
        };

        var revision = await Fixture.Client.Streams
            .SetStreamMetadata(simulation.Stream, meta, ct);

        await Assert.That(revision).IsEqualTo(0);
    }

    [Test]
    public async ValueTask sets_stream_metadata_when_stream_does_not_exist(CancellationToken ct) {
        var simulation = ArcadeRacerTestData.SimulateRace(1);

        var stream = simulation.Stream;

        var meta = new StreamMetadata {
            MaxAge         = TimeSpan.FromDays(1),
            TruncateBefore = null,
            CacheControl   = TimeSpan.FromHours(1),
            MaxCount       = 100,
            CustomMetadata = JsonDocument.Parse("""{"key1":"value1"}""")
        };

        var revision = await Fixture.Client.Streams
            .SetStreamMetadata(stream, meta, ct);

        await Assert.That(revision).IsEqualTo(0);

        var info = await Fixture.Client.Streams
            .GetStreamInfo(stream, ct);

        await Assert.That(info.Metadata.MaxAge).IsEqualTo(meta.MaxAge);
        await Assert.That(info.Metadata.TruncateBefore).IsEqualTo(meta.TruncateBefore);
        await Assert.That(info.Metadata.CacheControl).IsEqualTo(meta.CacheControl);
        await Assert.That(info.Metadata.MaxCount).IsEqualTo(meta.MaxCount);
        await Assert.That(info.Metadata.CustomMetadata?.RootElement.GetRawText()).IsEqualTo(meta.CustomMetadata?.RootElement.GetRawText());
        await Assert.That(info.State).IsEqualTo(StreamState.Missing);
    }

    [Test]
    public async ValueTask sets_stream_metadata_when_stream_is_deleted(CancellationToken ct) {
        var simulation = await Fixture.SeedRace(ct);

        await Fixture.Client.Streams
            .Delete(simulation.Stream, ct);

        var meta = new StreamMetadata {
            MaxAge = TimeSpan.FromDays(1),
        };

        var revision = await Fixture.Client.Streams
            .SetStreamMetadata(simulation.Stream, meta, ct);

        await Assert.That(revision).IsEqualTo(1);
    }

    [Test]
    public async ValueTask fails_to_set_stream_metadata_when_stream_is_tombstoned(CancellationToken ct) {
        var simulation = await Fixture.SeedRace(ct);

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, ct);

        var meta = new StreamMetadata {
            MaxAge         = TimeSpan.FromDays(1),
            TruncateBefore = null,
            CacheControl   = TimeSpan.FromHours(1),
            MaxCount       = 100
        };

        await Assert.That(async () => await Fixture.Client.Streams
            .SetStreamMetadata(simulation.Stream, meta, ct))
            .Throws<StreamTombstonedException>();
    }
}
