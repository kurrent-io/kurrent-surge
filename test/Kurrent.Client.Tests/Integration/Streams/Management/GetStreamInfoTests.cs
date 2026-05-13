using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("StreamInfo")]
[Category("Integration")]
public class GetStreamInfoTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    #region Stream State

    [Test]
    public async ValueTask returns_missing_when_stream_does_not_exist(CancellationToken ct) {
        var info = await Fixture.Client.Streams
            .GetStreamInfo($"missing-{Guid.NewGuid():N}", ct);

        await Assert.That(info.State).IsEqualTo(StreamState.Missing);
        await Assert.That(info.InfoRevision).IsEqualTo(Revision.Unset);
        await Assert.That(info.LastStreamRevision).IsEqualTo(Revision.Unset);
        await Assert.That(info.LastStreamPosition).IsEqualTo(LogPosition.Unset);
        await Assert.That(info.FirstStreamRevision).IsEqualTo(Revision.Unset);
        await Assert.That(info.FirstStreamPosition).IsEqualTo(LogPosition.Unset);
    }

    [Test]
    public async ValueTask returns_active_when_stream_exists(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        var info = await Fixture.Client.Streams.GetStreamInfo(data.Stream, ct);

        await Assert.That(info.State).IsEqualTo(StreamState.Active);
        await Assert.That(info.Stream).IsEqualTo(data.Stream);
        await Assert.That(info.InfoRevision).IsEqualTo(Revision.Unset);

        await Assert.That(info.FirstStreamRevision).IsEqualTo(Revision.Min);
        await Assert.That(info.FirstStreamPosition).IsGreaterThan(LogPosition.Earliest);
        await Assert.That(info.FirstStreamAppendedAt).IsGreaterThan(DateTime.MinValue);
        await Assert.That(info.FirstStreamPosition).IsLessThanOrEqualTo(info.LastStreamPosition);
        await Assert.That(info.LastStreamRevision).IsEqualTo(data.Revision);
        await Assert.That(info.LastStreamPosition).IsGreaterThan(LogPosition.Earliest);
        await Assert.That(info.LastStreamAppendedAt).IsGreaterThan(DateTime.MinValue);
    }

    [Test]
    public async ValueTask returns_deleted_when_stream_is_soft_deleted(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        await Fixture.Client.Streams.Delete(data.Stream, ct);

        var info = await Fixture.Client.Streams
            .GetStreamInfo(data.Stream, ct);

        await Assert.That(info.State).IsEqualTo(StreamState.Deleted);
        await Assert.That(info.InfoRevision).IsEqualTo(Revision.Min);
    }

    [Test]
    public async ValueTask returns_tombstoned_when_stream_is_tombstoned(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        await Fixture.Client.Streams.Tombstone(data.Stream, ct);

        var info = await Fixture.Client.Streams
            .GetStreamInfo(data.Stream, ct);

        await Assert.That(info.State).IsEqualTo(StreamState.Tombstoned);
    }

    #endregion

    #region Configuration

    [Test]
    public async ValueTask returns_retention_when_stream_has_retention_configured(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        var maxAge   = TimeSpan.FromDays(10);
        var maxCount = 10000;

        var options = new StreamRetention() {
            MaxAge   = TimeSpan.FromDays(10),
            MaxCount = 10000
        };

        var revision = await Fixture.Client.Streams
            .ConfigureDataRetention(data.Stream, options, ct);

        var info = await Fixture.Client.Streams
            .GetStreamInfo(data.Stream, ct);

        await Assert.That(info.State).IsEqualTo(StreamState.Active);
        await Assert.That(info.InfoRevision).IsEqualTo(revision);
        await Assert.That(info.Retention.MaxAge).IsEqualTo(maxAge);
        await Assert.That(info.Retention.MaxCount).IsEqualTo(maxCount);
        await Assert.That(info.Retention).IsEquivalentTo(options);
    }

    [Test]
    public async ValueTask returns_custom_metadata_when_stream_has_custom_metadata(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        var customMetadata = new Metadata().With("key1", "value1");

        var revision = await Fixture.Client.Streams
            .SetCustomMetadata(data.Stream, customMetadata, ct);

        var info = await Fixture.Client.Streams
            .GetStreamInfo(data.Stream, ct);

        await Assert.That(info.State).IsEqualTo(StreamState.Active);
        await Assert.That(info.InfoRevision).IsEqualTo(revision);
        await Assert.That(info.Metadata).IsEquivalentTo(customMetadata);
    }

    [Test]
    public async ValueTask returns_missing_with_retention_when_only_metadata_exists(CancellationToken ct) {
        var data = await Fixture.TestData.GenerateSet(ct);

        var options = new StreamRetention() {
            MaxAge   = TimeSpan.FromDays(1),
            MaxCount = 100
        };

        var revision = await Fixture.Client.Streams
            .ConfigureDataRetention(data.Stream, options, ct);

        var info = await Fixture.Client.Streams
            .GetStreamInfo(data.Stream, ct);

        // Stream has retention configured but no data — Missing with configuration preserved
        await Assert.That(info.State).IsEqualTo(StreamState.Missing);
        await Assert.That(info.InfoRevision).IsEqualTo(revision);
        await Assert.That(info.Retention.MaxAge).IsEqualTo(options.MaxAge);
        await Assert.That(info.Retention.MaxCount).IsEqualTo(options.MaxCount);
        await Assert.That(info.Retention).IsEquivalentTo(options);
    }

    #endregion
}
