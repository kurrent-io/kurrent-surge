using Kurrent.Client.Streams;
using Kurrent.Streaming.Checkpointing;

namespace Kurrent.Streaming.Tests.Checkpoints;

[Category("Processors")]
public class PositionTrackerTests {
    #region ->> Track <<-

    [Test]
    public async ValueTask track_single_position_returns_count_of_one() {
        // Arrange
        using var tracker = new PositionTracker();
        var position = TestPositions.At(100);

        // Act
        var count = tracker.Track(position);

        // Assert
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async ValueTask track_multiple_positions_returns_incremented_count() {
        // Arrange
        using var tracker = new PositionTracker();

        // Act
        var count1 = tracker.Track(TestPositions.At(100));
        var count2 = tracker.Track(TestPositions.At(200));
        var count3 = tracker.Track(TestPositions.At(300));

        // Assert
        await Assert.That(count1).IsEqualTo(1);
        await Assert.That(count2).IsEqualTo(2);
        await Assert.That(count3).IsEqualTo(3);
    }

    [Test]
    public async ValueTask track_unset_position_throws_argument_exception() {
        // Arrange
        using var tracker = new PositionTracker();
        var unset = RecordPosition.Unset;

        // Act & Assert
        await Assert.That(() => tracker.Track(unset)).Throws<ArgumentException>();
    }

    [Test]
    public async ValueTask track_position_less_than_or_equal_to_last_flushed_throws() {
        // Arrange
        using var tracker = new PositionTracker();
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(200));
        var lastTracked = tracker.Positions[^1];
        tracker.FlushUpTo(lastTracked);

        // Act & Assert
        await Assert.That(() => tracker.Track(TestPositions.At(150))).Throws<InvalidOperationException>();
        await Assert.That(() => tracker.Track(TestPositions.At(200))).Throws<InvalidOperationException>();
    }

    [Test]
    public async ValueTask positions_returns_all_tracked_positions_in_order() {
        // Arrange
        using var tracker = new PositionTracker();
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(200));
        tracker.Track(TestPositions.At(300));

        // Act
        var positions = tracker.Positions;

        // Assert
        await positions.ShouldHavePositions(100, 200, 300);
    }

    #endregion // Track

    #region ->> Flush <<-

    [Test]
    public async ValueTask flush_returns_all_tracked_positions() {
        // Arrange
        using var tracker = new PositionTracker();
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(200));

        // Act
        var lastTracked = tracker.Positions[^1];
        var flushed = tracker.FlushUpTo(lastTracked);

        // Assert
        await flushed.ShouldHavePositions(100, 200);
    }

    [Test]
    public async ValueTask flush_clears_tracked_positions() {
        // Arrange
        using var tracker = new PositionTracker();
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(200));

        // Act
        var lastTracked = tracker.Positions[^1];
        tracker.FlushUpTo(lastTracked);

        // Assert
        await Assert.That(tracker.Positions.Length).IsEqualTo(0);
    }

    [Test]
    public async ValueTask flush_updates_last_flushed_position_to_last_tracked() {
        // Arrange
        using var tracker = new PositionTracker();
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(200));

        // Act
        var lastTracked = tracker.Positions[^1];
        tracker.FlushUpTo(lastTracked);

        // Assert
        await Assert.That(tracker.LastFlushedPosition.LogPosition.Value).IsEqualTo(200);
    }

    [Test]
    public async ValueTask flush_on_empty_tracker_returns_empty_span() {
        // Arrange
        using var tracker = new PositionTracker();

        // Act
        var flushed = tracker.FlushUpTo(TestPositions.At(100));

        // Assert
        await Assert.That(flushed.Length).IsEqualTo(0);
    }

    #endregion // Flush

    #region ->> FlushUpTo <<-

    [Test]
    public async ValueTask flush_up_to_retains_positions_after_flush_position() {
        // Arrange
        using var tracker = new PositionTracker();
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(200));
        tracker.Track(TestPositions.At(300));
        tracker.Track(TestPositions.At(400));

        // Act
        tracker.FlushUpTo(TestPositions.At(200));

        // Assert
        await tracker.Positions.ShouldHavePositions(300, 400);
    }

    [Test]
    public async ValueTask flush_up_to_updates_last_flushed_position() {
        // Arrange
        using var tracker = new PositionTracker();
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(200));
        tracker.Track(TestPositions.At(300));

        // Act
        tracker.FlushUpTo(TestPositions.At(200));

        // Assert
        await Assert.That(tracker.LastFlushedPosition.LogPosition.Value).IsEqualTo(200);
    }

    [Test]
    public async ValueTask flush_up_to_on_empty_tracker_returns_empty_span() {
        // Arrange
        using var tracker = new PositionTracker();

        // Act
        var flushed = tracker.FlushUpTo(TestPositions.At(100));

        // Assert
        await Assert.That(flushed.Length).IsEqualTo(0);
    }

    [Test]
    public async ValueTask flush_up_to_beyond_all_tracked_flushes_all() {
        // Arrange
        using var tracker = new PositionTracker();
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(200));
        tracker.Track(TestPositions.At(300));

        // Act - flush position 500 is beyond all tracked positions
        var flushed = tracker.FlushUpTo(TestPositions.At(500));

        // Assert - all positions flushed, LastFlushedPosition is the last actual flushed position
        await flushed.ShouldHavePositions(100, 200, 300);
        await Assert.That(tracker.Positions.Length).IsEqualTo(0);
        await Assert.That(tracker.LastFlushedPosition.LogPosition.Value).IsEqualTo(300);
    }

    [Test]
    public async ValueTask flush_up_to_with_exact_match_includes_that_position() {
        // Arrange
        using var tracker = new PositionTracker();
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(200));
        tracker.Track(TestPositions.At(300));

        // Act
        var flushed = tracker.FlushUpTo(TestPositions.At(200));

        // Assert
        await flushed.ShouldHavePositions(100, 200);
        await tracker.Positions.ShouldHavePositions(300);
    }

    [Test]
    public async ValueTask flush_up_to_untracked_position_flushes_up_to_previous() {
        // Arrange
        using var tracker = new PositionTracker();
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(200));
        tracker.Track(TestPositions.At(300));
        tracker.Track(TestPositions.At(400));

        // Act - position 250 is not tracked, flushes up to 200
        var flushed = tracker.FlushUpTo(TestPositions.At(250));

        // Assert
        await flushed.ShouldHavePositions(100, 200);
        await tracker.Positions.ShouldHavePositions(300, 400);
    }

    [Test]
    public async ValueTask flush_up_to_position_less_than_last_flushed_returns_empty() {
        // Arrange
        using var tracker = new PositionTracker();
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(200));
        tracker.Track(TestPositions.At(300));
        tracker.FlushUpTo(TestPositions.At(200));

        // Act - try to flush to a position before last flushed
        var flushed = tracker.FlushUpTo(TestPositions.At(150));

        // Assert
        await Assert.That(flushed.Length).IsEqualTo(0);
        await Assert.That(tracker.LastFlushedPosition.LogPosition.Value).IsEqualTo(200); // Should remain unchanged
    }

    [Test]
    public async ValueTask consecutive_flushes_maintain_correct_state() {
        // Arrange
        using var tracker = new PositionTracker();
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(200));
        tracker.Track(TestPositions.At(300));
        tracker.Track(TestPositions.At(400));
        tracker.Track(TestPositions.At(500));

        // Act - first flush
        var flushed1 = tracker.FlushUpTo(TestPositions.At(200));

        // Assert first flush
        await flushed1.ShouldHavePositions(100, 200);

        // Act - second flush
        var flushed2 = tracker.FlushUpTo(TestPositions.At(400));

        // Assert second flush
        await flushed2.ShouldHavePositions(300, 400);

        // Assert remaining
        await tracker.Positions.ShouldHavePositions(500);
    }

    [Test]
    public async ValueTask flush_up_to_with_duplicate_positions_handles_correctly() {
        // Arrange - track duplicate positions
        using var tracker = new PositionTracker();
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(200));
        tracker.Track(TestPositions.At(200));

        // Act
        var flushed = tracker.FlushUpTo(TestPositions.At(150));

        // Assert
        await flushed.ShouldHavePositions(100, 100);
        await tracker.Positions.ShouldHavePositions(200, 200);
    }

    #endregion // FlushUpTo

    #region ->> Buffer Management <<-

    [Test]
    public async ValueTask buffer_grows_when_capacity_exceeded() {
        // Arrange - create tracker with small capacity
        using var tracker = new PositionTracker(capacity: 256);

        // Act - track more than capacity
        for (var i = 1; i <= 300; i++)
            tracker.Track(TestPositions.At((ulong)(i * 100)));

        // Assert
        await Assert.That(tracker.Positions.Length).IsEqualTo(300);
    }

    [Test]
    public async ValueTask buffer_at_exact_minimum_capacity_works() {
        // Arrange - create tracker with exact minimum capacity
        using var tracker = new PositionTracker(capacity: 256);

        // Act - track exactly 256 elements (the minimum capacity)
        for (var i = 1; i <= 256; i++)
            tracker.Track(TestPositions.At((ulong)(i * 100)));

        // Assert
        await Assert.That(tracker.Positions.Length).IsEqualTo(256);
    }

    [Test]
    public async ValueTask buffer_compacts_before_growing_when_possible() {
        // Arrange - create tracker with minimum capacity
        using var tracker = new PositionTracker(capacity: 256);

        // Fill buffer halfway
        for (var i = 1; i <= 128; i++)
            tracker.Track(TestPositions.At((ulong)(i * 100)));

        // Flush some to create dead space at the front
        tracker.FlushUpTo(TestPositions.At(64 * 100));

        // Act - add more (should compact first, then possibly grow)
        for (var i = 129; i <= 256; i++)
            tracker.Track(TestPositions.At((ulong)(i * 100)));

        // Assert
        var positions = tracker.Positions;
        await Assert.That(positions.Length).IsEqualTo(192); // 64 remaining (65-128) + 128 new (129-256)
    }

    [Test]
    public void dispose_can_be_called_multiple_times_safely() {
        // Arrange
        var tracker = new PositionTracker();

        // Act & Assert - should not throw
        tracker.Dispose();
        tracker.Dispose();
        tracker.Dispose();
    }

    [Test]
    public async ValueTask operations_after_dispose_throw_object_disposed_exception() {
        // Arrange
        var tracker = new PositionTracker();
        tracker.Dispose();

        // Act & Assert
        await Assert.That(() => tracker.Track(TestPositions.At(100))).Throws<ObjectDisposedException>();
        await Assert.That(() => _ = tracker.Positions).Throws<ObjectDisposedException>();
        await Assert.That(() => tracker.FlushUpTo(TestPositions.At(100))).Throws<ObjectDisposedException>();
    }

    #endregion // Buffer Management

    #region ->> Reset <<-

    [Test]
    public async ValueTask reset_after_partial_flush_clears_pending_positions() {
        // Arrange
        using var tracker = new PositionTracker();
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(200));
        tracker.Track(TestPositions.At(300));
        tracker.Track(TestPositions.At(400));
        tracker.FlushUpTo(TestPositions.At(200));

        // Act
        tracker.Reset(TestPositions.At(500));

        // Assert
        await Assert.That(tracker.Positions.Length).IsEqualTo(0);
        await Assert.That(tracker.LastFlushedPosition.LogPosition.Value).IsEqualTo(500);
    }

    [Test]
    public async ValueTask track_after_reset_enforces_new_baseline() {
        // Arrange
        using var tracker = new PositionTracker();
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(200));
        var lastTracked = tracker.Positions[^1];
        tracker.FlushUpTo(lastTracked);
        tracker.Reset(TestPositions.At(500));

        // Act & Assert - position less than or equal to new baseline should throw
        await Assert.That(() => tracker.Track(TestPositions.At(400))).Throws<InvalidOperationException>();
        await Assert.That(() => tracker.Track(TestPositions.At(500))).Throws<InvalidOperationException>();

        // Position greater than new baseline should succeed
        var count = tracker.Track(TestPositions.At(600));
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async ValueTask reset_with_unset_position_works() {
        // Arrange
        using var tracker = new PositionTracker();
        tracker.Track(TestPositions.At(100));
        tracker.Track(TestPositions.At(200));

        // Act
        tracker.Reset(RecordPosition.Unset);

        // Assert
        await Assert.That(tracker.Positions.Length).IsEqualTo(0);
        await Assert.That(tracker.LastFlushedPosition).IsEqualTo(RecordPosition.Unset);

        // Should be able to track from the beginning
        await Assert.That(tracker.Track(TestPositions.At(50))).IsEqualTo(1);
    }

    #endregion // Reset

    #region ->> Constructor <<-

    [Test]
    public async ValueTask constructor_with_last_flushed_position_sets_initial_state() {
        // Arrange
        var lastFlushed = TestPositions.At(500);

        // Act
        using var tracker = new PositionTracker(lastFlushed);

        // Assert
        await Assert.That(tracker.LastFlushedPosition).IsEqualTo(lastFlushed);
        await Assert.That(tracker.Positions.Length).IsEqualTo(0);
    }

    [Test]
    public async ValueTask constructor_with_last_flushed_position_enforces_baseline() {
        // Arrange
        var lastFlushed = TestPositions.At(500);
        using var tracker = new PositionTracker(lastFlushed);

        // Act & Assert - positions at or below baseline should throw
        await Assert.That(() => tracker.Track(TestPositions.At(400))).Throws<InvalidOperationException>();
        await Assert.That(() => tracker.Track(TestPositions.At(500))).Throws<InvalidOperationException>();

        // Position above baseline should succeed
        await Assert.That(tracker.Track(TestPositions.At(600))).IsEqualTo(1);
    }

    [Test]
    public async ValueTask constructor_with_unset_position_allows_any_tracking() {
        // Arrange
        using var tracker = new PositionTracker(RecordPosition.Unset);

        // Act & Assert - any position should be trackable
        await Assert.That(tracker.Track(TestPositions.At(1))).IsEqualTo(1);
    }

    #endregion // Constructor

    #region ->> Properties <<-

    [Test]
    public async ValueTask last_flushed_position_is_unset_initially() {
        // Arrange
        using var tracker = new PositionTracker();

        // Assert
        await Assert.That(tracker.LastFlushedPosition).IsEqualTo(RecordPosition.Unset);
    }

    [Test]
    public async ValueTask positions_is_empty_initially() {
        // Arrange
        using var tracker = new PositionTracker();

        // Assert
        await Assert.That(tracker.Positions.Length).IsEqualTo(0);
    }

    #endregion // Properties
}

static class TestPositions {
    public static RecordPosition At(ulong logPosition) =>
        RecordPosition.From((long)logPosition);
}

static class PositionTrackerTestExtensions {
    public static async ValueTask ShouldHavePositions(this RecordPosition[] positions, params ulong[] expected) {
        await Assert.That(positions.Length).IsEqualTo(expected.Length);
        for (var i = 0; i < expected.Length; i++)
            await Assert.That(positions[i].LogPosition.Value).IsEqualTo((long)expected[i]);
    }
}
