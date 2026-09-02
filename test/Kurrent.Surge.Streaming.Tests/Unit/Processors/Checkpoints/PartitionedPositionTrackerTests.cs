using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors.Checkpointing;

namespace Kurrent.Surge.Streaming.Tests.Checkpoints;

[Category("Processors")]
public class PartitionedPositionTrackerTests {
    #region ->> HasPendingPositions <<-

    [Test]
    public async ValueTask has_pending_positions_is_false_when_empty() {
        // Arrange
        using var tracker = new PartitionedPositionTracker();

        // Act & Assert
        await Assert.That(tracker.HasPendingPositions).IsFalse();
    }

    [Test]
    public async ValueTask has_pending_positions_is_false_after_reset_with_no_tracks() {
        // Arrange
        var positions = new[] { RecordPosition.From(LogPosition.Earliest, Partition.From(0)) };
        using var tracker = new PartitionedPositionTracker(positions);

        // Act & Assert
        await Assert.That(tracker.HasPendingPositions).IsFalse();
    }

    [Test]
    public async ValueTask has_pending_positions_is_true_after_track() {
        // Arrange
        var positions = new[] { RecordPosition.From(LogPosition.Earliest, Partition.From(0)) };
        using var tracker = new PartitionedPositionTracker(positions);

        // Act
        tracker.Track(RecordPosition.From(LogPosition.From(100), Partition.From(0)));

        // Assert
        await Assert.That(tracker.HasPendingPositions).IsTrue();
    }

    [Test]
    public async ValueTask has_pending_positions_is_false_after_flush() {
        // Arrange
        var positions = new[] { RecordPosition.From(LogPosition.Earliest, Partition.From(0)) };
        using var tracker = new PartitionedPositionTracker(positions);

        var tracked = RecordPosition.From(LogPosition.From(100), Partition.From(0));
        tracker.Track(tracked);

        // Act
        tracker.FlushUpTo([tracked]);

        // Assert
        await Assert.That(tracker.HasPendingPositions).IsFalse();
    }

    [Test]
    public async ValueTask has_pending_positions_reflects_any_partition_with_pending() {
        // Arrange
        var positions = new[] {
            RecordPosition.From(LogPosition.Earliest, Partition.From(0)),
            RecordPosition.From(LogPosition.Earliest, Partition.From(1))
        };
        using var tracker = new PartitionedPositionTracker(positions);

        var p0 = RecordPosition.From(LogPosition.From(100), Partition.From(0));
        var p1 = RecordPosition.From(LogPosition.From(200), Partition.From(1));

        tracker.Track(p0);
        tracker.Track(p1);

        // Act — flush only partition 0
        tracker.FlushUpTo([p0]);

        // Assert — partition 1 still has pending
        await Assert.That(tracker.HasPendingPositions).IsTrue();
    }

    [Test]
    public async ValueTask has_pending_positions_is_false_after_all_partitions_flushed() {
        // Arrange
        var positions = new[] {
            RecordPosition.From(LogPosition.Earliest, Partition.From(0)),
            RecordPosition.From(LogPosition.Earliest, Partition.From(1))
        };
        using var tracker = new PartitionedPositionTracker(positions);

        var p0 = RecordPosition.From(LogPosition.From(100), Partition.From(0));
        var p1 = RecordPosition.From(LogPosition.From(200), Partition.From(1));

        tracker.Track(p0);
        tracker.Track(p1);

        // Act — flush both
        tracker.FlushUpTo([p0, p1]);

        // Assert
        await Assert.That(tracker.HasPendingPositions).IsFalse();
    }

    #endregion // HasPendingPositions
}
