using System.Text.Json;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("StreamInfo")]
[Category("Integration")]
public class GetStreamInfoTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    #region Stream State

    [Test]
    public async ValueTask returns_missing_when_stream_does_not_exist(CancellationToken ct) {
        var info = await Fixture.Client.Streams
            .GetStreamInfo($"missing-{Guid.NewGuid():N}", ct);

        await Assert.That(info.State).IsEqualTo(StreamState.Missing);
        await Assert.That(info.HasMetadata).IsFalse();
        await Assert.That(info.MetadataRevision).IsEqualTo(Revision.Unset);
        await Assert.That(info.LastRevision).IsEqualTo(Revision.Unset);
        await Assert.That(info.LastStreamPosition).IsEqualTo(LogPosition.Unset);
    }

    [Test]
    public async ValueTask returns_active_when_stream_exists(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        var info = await Fixture.Client.Streams
            .GetStreamInfo(data.Stream, ct);

        await Assert.That(info.State).IsEqualTo(StreamState.Active);
        await Assert.That(info.HasMetadata).IsFalse();
        await Assert.That(info.MetadataRevision).IsEqualTo(Revision.Unset);
        await Assert.That(info.LastRevision).IsEqualTo(data.Revision);
        await Assert.That(info.LastStreamPosition).IsGreaterThan(LogPosition.Earliest);
        await Assert.That(info.LastStreamAppendTime).IsGreaterThan(DateTime.MinValue);
    }

    [Test]
    public async ValueTask returns_deleted_when_stream_is_soft_deleted(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        await Fixture.Client.Streams.Delete(data.Stream, ct);

        var info = await Fixture.Client.Streams
            .GetStreamInfo(data.Stream, ct);

        await Assert.That(info.State).IsEqualTo(StreamState.Deleted);
        await Assert.That(info.HasMetadata).IsTrue();
        await Assert.That(info.MetadataRevision).IsNotEqualTo(Revision.Unset);
    }

    [Test]
    public async ValueTask returns_tombstoned_when_stream_is_tombstoned(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        await Fixture.Client.Streams.Tombstone(data.Stream, ct);

        var info = await Fixture.Client.Streams
            .GetStreamInfo(data.Stream, ct);

        await Assert.That(info.State).IsEqualTo(StreamState.Tombstoned);
        await Assert.That(info.HasMetadata).IsFalse();
    }

    #endregion

    #region Metadata

    [Test]
    public async ValueTask returns_metadata_when_stream_has_metadata(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        var customMetadata = JsonDocument.Parse("""{"key1":"value1"}""");

        var meta = new StreamMetadata {
            MaxAge         = TimeSpan.FromDays(1),
            TruncateBefore = null,
            CacheControl   = TimeSpan.FromHours(1),
            MaxCount       = 100,
            CustomMetadata = customMetadata
        };

        await Fixture.Client.Streams
            .SetStreamMetadata(data.Stream, meta, ct);

        var info = await Fixture.Client.Streams
            .GetStreamInfo(data.Stream, ct);

        await Assert.That(info.State).IsEqualTo(StreamState.Active);
        await Assert.That(info.HasMetadata).IsTrue();
        await Assert.That(info.MetadataRevision).IsEqualTo(0);
        await Assert.That(info.Metadata.MaxAge).IsEqualTo(meta.MaxAge);
        await Assert.That(info.Metadata.CacheControl).IsEqualTo(meta.CacheControl);
        await Assert.That(info.Metadata.MaxCount).IsEqualTo(meta.MaxCount);
        await Assert.That(info.Metadata.CustomMetadata?.RootElement.GetRawText())
            .IsEqualTo(meta.CustomMetadata?.RootElement.GetRawText());
    }

    [Test]
    public async ValueTask returns_missing_with_metadata_when_only_metadata_exists(CancellationToken ct) {
        var data = await Fixture.TestData.GenerateSet(ct);

        var meta = new StreamMetadata {
            MaxAge   = TimeSpan.FromDays(1),
            MaxCount = 100
        };

        await Fixture.Client.Streams
            .SetStreamMetadata(data.Stream, meta, ct);

        var info = await Fixture.Client.Streams
            .GetStreamInfo(data.Stream, ct);

        // Stream has metadata but no data — Missing with metadata preserved
        await Assert.That(info.State).IsEqualTo(StreamState.Missing);
        await Assert.That(info.HasMetadata).IsTrue();
        await Assert.That(info.MetadataRevision).IsEqualTo(0);
        await Assert.That(info.Metadata.MaxAge).IsEqualTo(meta.MaxAge);
        await Assert.That(info.Metadata.MaxCount).IsEqualTo(meta.MaxCount);
    }

    #endregion
}
