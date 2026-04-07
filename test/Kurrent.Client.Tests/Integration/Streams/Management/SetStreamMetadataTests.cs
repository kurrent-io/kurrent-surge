using System.Text.Json;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("StreamInfo")]
[Category("Integration")]
public class SetStreamMetadataTests {
	[ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask sets_metadata_when_stream_exists(CancellationToken ct) {
		var data = await Fixture.SeedTestStream(ct);

		var meta = new StreamMetadata {
			MaxAge         = TimeSpan.FromDays(1),
			CacheControl   = TimeSpan.FromHours(1),
			MaxCount       = 100,
			CustomMetadata = JsonDocument.Parse("""{"key1":"value1"}""")
		};

		var revision = await Fixture.Client.Streams
			.SetStreamMetadata(data.Stream, meta, ct);

		await Assert.That(revision).IsEqualTo(0);
	}

	[Test]
	public async ValueTask sets_metadata_when_stream_does_not_exist(CancellationToken ct) {
		var data = await Fixture.TestData.GenerateSet(ct);

		var meta = new StreamMetadata {
			MaxAge   = TimeSpan.FromDays(1),
			MaxCount = 100
		};

		var revision = await Fixture.Client.Streams
			.SetStreamMetadata(data.Stream, meta, ct);

		await Assert.That(revision).IsEqualTo(Revision.Min);
	}

	[Test]
	public async ValueTask sets_metadata_when_stream_is_deleted(CancellationToken ct) {
		var data = await Fixture.SeedTestStream(ct);

		await Fixture.Client.Streams.Delete(data.Stream, ct);

		var meta = new StreamMetadata { MaxAge = TimeSpan.FromDays(1) };

		var revision = await Fixture.Client.Streams
			.SetStreamMetadata(data.Stream, meta, ct);

		await Assert.That(revision).IsNotEqualTo(Revision.Unset);
	}

	[Test]
	public async ValueTask updates_metadata_at_expected_revision(CancellationToken ct) {
		var data = await Fixture.SeedTestStream(ct);

		var meta1 = new StreamMetadata { MaxCount = 100 };

		var revision = await Fixture.Client.Streams
			.SetStreamMetadata(data.Stream, meta1, ct);

		await Assert.That(revision).IsEqualTo(0);

		// Update at the returned revision
		var meta2 = new StreamMetadata { MaxCount = 200 };

		var revision2 = await Fixture.Client.Streams
			.SetStreamMetadata(data.Stream, meta2, revision, ct);

		await Assert.That(revision2).IsEqualTo(1);

		var info = await Fixture.Client.Streams
			.GetStreamInfo(data.Stream, ct);

		await Assert.That(info.Metadata.MaxCount).IsEqualTo(200);
		await Assert.That(info.MetadataRevision).IsEqualTo(1);
	}

	[Test]
	public async ValueTask fails_when_revision_does_not_match(CancellationToken ct) {
		var data = await Fixture.SeedTestStream(ct);

		var meta = new StreamMetadata { MaxCount = 100 };

		await Fixture.Client.Streams
			.SetStreamMetadata(data.Stream, meta, ct);

		// Try to update at a stale revision
		var meta2 = new StreamMetadata { MaxCount = 200 };

		await Assert.That(async () => await Fixture.Client.Streams
			.SetStreamMetadata(data.Stream, meta2, Revision.From(99), ct))
			.Throws<StreamRevisionConflictException>();
	}

	[Test]
	public async ValueTask fails_when_stream_is_tombstoned(CancellationToken ct) {
		var data = await Fixture.SeedTestStream(ct);

		await Fixture.Client.Streams.Tombstone(data.Stream, ct);

		var meta = new StreamMetadata {
			MaxAge       = TimeSpan.FromDays(1),
			CacheControl = TimeSpan.FromHours(1),
			MaxCount     = 100
		};

		await Assert.That(async () => await Fixture.Client.Streams
			.SetStreamMetadata(data.Stream, meta, ct))
			.Throws<StreamTombstonedException>();
	}
}
