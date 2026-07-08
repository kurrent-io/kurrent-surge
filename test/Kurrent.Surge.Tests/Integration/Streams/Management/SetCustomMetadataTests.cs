using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using static System.TimeSpan;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("StreamInfo")]
[Category("Integration")]
public class SetCustomMetadataTests {
	[ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask sets_custom_metadata_when_stream_exists(CancellationToken ct) {
		var data = await Fixture.SeedTestStream(ct);

		var customMetadata = new Metadata().With("key1", "value1");

		var revision = await Fixture.Client.Streams
			.SetCustomMetadata(data.Stream, customMetadata, ct);

		await Assert.That(revision).IsEqualTo(0);

		var info = await Fixture.Client.Streams
			.GetStreamInfo(data.Stream, ct);

		await Assert.That(info.Metadata.GetValueOrDefault<string>("key1")).IsEqualTo("value1");
	}

	[Test]
	public async ValueTask sets_custom_metadata_when_stream_does_not_exist(CancellationToken ct) {
		var data = await Fixture.TestData.GenerateSet(ct);

		var customMetadata = new Metadata().With("owner", "team-a");

		var revision = await Fixture.Client.Streams
			.SetCustomMetadata(data.Stream, customMetadata, ct);

		await Assert.That(revision).IsEqualTo(Revision.Min);
	}

	[Test]
	public async ValueTask sets_retention_when_stream_is_deleted(CancellationToken ct) {
		var data = await Fixture.SeedTestStream(ct);

		await Fixture.Client.Streams.Delete(data.Stream, ct);

		var revision = await Fixture.Client.Streams
			.ConfigureDataRetention(data.Stream, maxAge: FromDays(1), ct);

		await Assert.That(revision).IsNotEqualTo(Revision.Unset);
	}

	[Test]
	public async ValueTask updates_retention_at_expected_revision(CancellationToken ct) {
		var data = await Fixture.SeedTestStream(ct);

		var revision = await Fixture.Client.Streams
			.ConfigureDataRetention(data.Stream, maxCount: 100, ct);

		await Assert.That(revision).IsEqualTo(0);

		// Update at the returned revision
		var revision2 = await Fixture.Client.Streams
			.ConfigureDataRetention(data.Stream, new() { MaxCount = 200 }, revision, ct);

		await Assert.That(revision2).IsEqualTo(1);

		var info = await Fixture.Client.Streams
			.GetStreamInfo(data.Stream, ct);

		await Assert.That(info.Retention.MaxCount).IsEqualTo(200);
		await Assert.That(info.InfoRevision).IsEqualTo(1);
	}

	[Test]
	public async ValueTask fails_when_revision_does_not_match(CancellationToken ct) {
		var data = await Fixture.SeedTestStream(ct);

		var revision = await Fixture.Client.Streams
			.ConfigureDataRetention(data.Stream, maxCount: 100, ct);

        var badRevision = revision + 10;

		// Try to update at a stale revision
		await Assert.That(async () => await Fixture.Client.Streams
			.ConfigureDataRetention(data.Stream, new() { MaxCount = 200 }, revision: badRevision, ct))
			.Throws<StreamRevisionConflictException>();
	}

	[Test]
	public async ValueTask fails_when_stream_is_tombstoned(CancellationToken ct) {
		var data = await Fixture.SeedTestStream(ct);

		await Fixture.Client.Streams.Tombstone(data.Stream, ct);

		await Assert.That(async () => await Fixture.Client.Streams
			.ConfigureDataRetention(data.Stream, maxCount: 100, ct))
			.Throws<StreamTombstonedException>();
	}
}
