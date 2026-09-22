using Kurrent.Surge.Processors;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Tests.Fakes;

namespace Kurrent.Surge.Streaming.Tests;

[Category("Processors")]
public class ProgressTrackerTests {
	static readonly RecordFaker Records = new();

	// The uncommitted accounting moved into ProgressTracker with the partition slots — DroneProgress
	// now carries the result rather than deriving it, so these drive the tracker directly.
	static ProgressTracker NewTracker(int partitionCount = 1) =>
		new(TimeProvider.System, partitionCount);

	static RecordPosition At(long position, int partition) =>
		RecordPosition.From(LogPosition.From(position), Partition.From(partition));

	[Test]
	public async ValueTask uncommitted_reflects_tracked_minus_committed() {
		// Arrange — commit at 200, then dispatch 7 records and track out to 500
		var tracker = NewTracker();
		var record  = Records.Generate();

		tracker.TrackCheckpoint([At(200, 0)]);

		for (var i = 0; i < 7; i++)
			tracker.CountThroughput(record);

		tracker.TrackPosition(At(500, 0));

		// Act
		var progress = tracker.Progress;

		// Assert — records since the commit, bytes from the tracked-to-committed gap
		await Assert.That(progress.UncommittedRecords).IsEqualTo(7L);
		await Assert.That(progress.UncommittedBytes).IsEqualTo(300L);
	}

	[Test]
	public async ValueTask uncommitted_bytes_sums_across_partitions() {
		// Arrange — two partitions with gaps 300 (500−200) and 50 (150−100)
		var tracker = NewTracker(partitionCount: 2);

		tracker.TrackCheckpoint([At(200, 0), At(100, 1)]);
		tracker.TrackPosition(At(500, 0));
		tracker.TrackPosition(At(150, 1));

		// Act & Assert
		await Assert.That(tracker.Progress.UncommittedBytes).IsEqualTo(350L);
	}

	[Test]
	public async ValueTask uncommitted_bytes_measures_from_the_resume_point_before_any_commit() {
		// Arrange — the whole point of seeding: a catch-up that has tracked 500 but never checkpointed
		// has 300 bytes at risk from its resume point at 200. Reporting zero here would say "nothing to
		// replay" while an entire catch-up is pending.
		var tracker = NewTracker();

		tracker.Initialize([At(200, 0)]);
		tracker.TrackPosition(At(500, 0));

		// Act & Assert
		await Assert.That(tracker.Progress.UncommittedBytes).IsEqualTo(300L);
	}

	[Test]
	public async ValueTask uncommitted_bytes_is_zero_when_the_run_starts_at_the_live_edge() {
		// Arrange — Latest is a sentinel, not a position: there is no resolved edge to measure from
		// until the first checkpoint lands, so nothing is claimed.
		var tracker = NewTracker();

		tracker.Initialize([RecordPosition.From(LogPosition.Latest, Partition.From(0))]);
		tracker.TrackPosition(At(500, 0));

		// Act & Assert
		await Assert.That(tracker.Progress.UncommittedBytes).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask uncommitted_bytes_is_zero_when_committed_overtakes_tracked() {
		// Arrange — a committed scan watermark can advance past the last tracked record (the no-match
		// region folds into the checkpointer, never into per-record tracking). The gap floors at zero.
		var tracker = NewTracker();

		tracker.TrackPosition(At(200, 0));
		tracker.TrackCheckpoint([At(500, 0)]);

		// Act & Assert
		await Assert.That(tracker.Progress.UncommittedBytes).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask committed_watermarks_span_the_least_and_most_advanced_partitions() {
		// Arrange — two partitions committed at different positions. The spread between the watermarks
		// is the commit skew; the lowest is where a restart resumes from.
		var tracker = NewTracker(partitionCount: 2);

		tracker.TrackCheckpoint([At(900, 0), At(300, 1)]);

		// Act
		var progress = tracker.Progress;

		// Assert
		await Assert.That(progress.LowestCommittedPosition).IsEqualTo(At(300, 1));
		await Assert.That(progress.HighestCommittedPosition).IsEqualTo(At(900, 0));
	}

	[Test]
	public async ValueTask committed_watermarks_ignore_partitions_that_never_committed() {
		// Arrange — one of two partitions has committed. An Unset slot must not drag the lowest to zero,
		// which would read as "durable from the start of the log".
		var tracker = NewTracker(partitionCount: 2);

		tracker.TrackCheckpoint([At(400, 0)]);

		// Act
		var progress = tracker.Progress;

		// Assert
		await Assert.That(progress.LowestCommittedPosition).IsEqualTo(At(400, 0));
		await Assert.That(progress.HighestCommittedPosition).IsEqualTo(At(400, 0));
	}
}
