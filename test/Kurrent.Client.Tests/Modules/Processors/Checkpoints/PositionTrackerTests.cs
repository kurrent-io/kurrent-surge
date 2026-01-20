using Kurrent.Client.Processors.Checkpointing;
using Kurrent.Client.Streams;
using Kurrent.Client.Streams.Processing;

namespace Kurrent.Client.Tests.Modules.Processors.Checkpoints;

[Category("Processors")]
public class PositionTrackerTests : IDisposable {
    PositionTracker _tracker = new();

    public void Dispose() => _tracker.Dispose();

    #region . Track .

    [Test]
    public void track_single_position_returns_count_of_one() {
        // Arrange
        var position = TestPositions.At(100);

        // Act
        var count = _tracker.Track(position);

        // Assert
        count.ShouldBe(1);
    }

    [Test]
    public void track_multiple_positions_returns_incremented_count() {
        // Arrange & Act
        var count1 = _tracker.Track(TestPositions.At(100));
        var count2 = _tracker.Track(TestPositions.At(200));
        var count3 = _tracker.Track(TestPositions.At(300));

        // Assert
        count1.ShouldBe(1);
        count2.ShouldBe(2);
        count3.ShouldBe(3);
    }

    [Test]
    public void track_unset_position_throws_argument_exception() {
        // Arrange
        var unset = RecordPosition.Unset;

        // Act & Assert
        Should.Throw<ArgumentException>(() => _tracker.Track(unset));
    }

    [Test]
    public void track_position_less_than_or_equal_to_last_flushed_throws() {
        // Arrange
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));
        var lastTracked = _tracker.Positions[^1];
        _tracker.FlushUpTo(lastTracked);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _tracker.Track(TestPositions.At(150)));
        Should.Throw<InvalidOperationException>(() => _tracker.Track(TestPositions.At(200)));
    }

    [Test]
    public void positions_returns_all_tracked_positions_in_order() {
        // Arrange
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));
        _tracker.Track(TestPositions.At(300));

        // Act
        var positions = _tracker.Positions;

        // Assert
        positions.Length.ShouldBe(3);
        positions[0].LogPosition.Value.ShouldBe(100);
        positions[1].LogPosition.Value.ShouldBe(200);
        positions[2].LogPosition.Value.ShouldBe(300);
    }

    #endregion

    #region . Flush .

    [Test]
    public void flush_returns_all_tracked_positions() {
        // Arrange
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));

        // Act
        var lastTracked = _tracker.Positions[^1];
        var flushed = _tracker.FlushUpTo(lastTracked);

        // Assert
        flushed.Length.ShouldBe(2);
        flushed[0].LogPosition.Value.ShouldBe(100);
        flushed[1].LogPosition.Value.ShouldBe(200);
    }

    [Test]
    public void flush_clears_tracked_positions() {
        // Arrange
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));

        // Act
        var lastTracked = _tracker.Positions[^1];
        _tracker.FlushUpTo(lastTracked);

        // Assert
        _tracker.Positions.Length.ShouldBe(0);
    }

    [Test]
    public void flush_updates_last_flushed_position_to_last_tracked() {
        // Arrange
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));

        // Act
        var lastTracked = _tracker.Positions[^1];
        _tracker.FlushUpTo(lastTracked);

        // Assert
        _tracker.LastFlushedPosition.LogPosition.Value.ShouldBe(200);
    }

    [Test]
    public void flush_on_empty_tracker_returns_empty_span() {
        // Act
        var flushed = _tracker.FlushUpTo(TestPositions.At(100));

        // Assert
        flushed.Length.ShouldBe(0);
    }

    #endregion

    #region . FlushUpTo .

    [Test]
    public void flush_up_to_returns_positions_up_to_and_including_barrier() {
        // Arrange
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));
        _tracker.Track(TestPositions.At(300));
        _tracker.Track(TestPositions.At(400));

        // Act
        var flushed = _tracker.FlushUpTo(TestPositions.At(200));

        // Assert
        flushed.Length.ShouldBe(2);
        flushed[0].LogPosition.Value.ShouldBe(100);
        flushed[1].LogPosition.Value.ShouldBe(200);
    }

    [Test]
    public void flush_up_to_retains_positions_after_barrier() {
        // Arrange
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));
        _tracker.Track(TestPositions.At(300));
        _tracker.Track(TestPositions.At(400));

        // Act
        _tracker.FlushUpTo(TestPositions.At(200));

        // Assert
        var remaining = _tracker.Positions;
        remaining.Length.ShouldBe(2);
        remaining[0].LogPosition.Value.ShouldBe(300);
        remaining[1].LogPosition.Value.ShouldBe(400);
    }

    [Test]
    public void flush_up_to_updates_last_flushed_position() {
        // Arrange
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));
        _tracker.Track(TestPositions.At(300));

        // Act
        _tracker.FlushUpTo(TestPositions.At(200));

        // Assert
        _tracker.LastFlushedPosition.LogPosition.Value.ShouldBe(200);
    }

    [Test]
    public void flush_up_to_with_untracked_barrier_still_flushes_up_to_it() {
        // Arrange - barrier at 250 is not tracked, but should flush 100 and 200
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));
        _tracker.Track(TestPositions.At(300));
        _tracker.Track(TestPositions.At(400));

        // Act
        var flushed = _tracker.FlushUpTo(TestPositions.At(250));

        // Assert
        flushed.Length.ShouldBe(2);
        flushed[0].LogPosition.Value.ShouldBe(100);
        flushed[1].LogPosition.Value.ShouldBe(200);
    }

    [Test]
    public void flush_up_to_on_empty_tracker_returns_empty_span() {
        // Act
        var flushed = _tracker.FlushUpTo(TestPositions.At(100));

        // Assert
        flushed.Length.ShouldBe(0);
    }

    [Test]
    public void flush_up_to_with_exact_match_includes_that_position() {
        // Arrange
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));
        _tracker.Track(TestPositions.At(300));

        // Act
        var flushed = _tracker.FlushUpTo(TestPositions.At(200));

        // Assert
        flushed.Length.ShouldBe(2);
        flushed[0].LogPosition.Value.ShouldBe(100);
        flushed[1].LogPosition.Value.ShouldBe(200);

        var remaining = _tracker.Positions;
        remaining.Length.ShouldBe(1);
        remaining[0].LogPosition.Value.ShouldBe(300);
    }

    [Test]
    public void flush_up_to_between_positions_stops_before_next() {
        // Arrange
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));
        _tracker.Track(TestPositions.At(300));
        _tracker.Track(TestPositions.At(400));

        // Act - barrier at 250 is not tracked
        var flushed = _tracker.FlushUpTo(TestPositions.At(250));

        // Assert
        flushed.Length.ShouldBe(2);
        flushed[0].LogPosition.Value.ShouldBe(100);
        flushed[1].LogPosition.Value.ShouldBe(200);

        var remaining = _tracker.Positions;
        remaining.Length.ShouldBe(2);
        remaining[0].LogPosition.Value.ShouldBe(300);
        remaining[1].LogPosition.Value.ShouldBe(400);
    }

    [Test]
    public void flush_up_to_position_less_than_last_flushed_returns_empty() {
        // Arrange
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));
        _tracker.Track(TestPositions.At(300));
        _tracker.FlushUpTo(TestPositions.At(200));

        // Act - try to flush to a position before last flushed
        var flushed = _tracker.FlushUpTo(TestPositions.At(150));

        // Assert
        flushed.Length.ShouldBe(0);
        _tracker.LastFlushedPosition.LogPosition.Value.ShouldBe(200); // Should remain unchanged
    }

    [Test]
    public void consecutive_flushes_maintain_correct_state() {
        // Arrange
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));
        _tracker.Track(TestPositions.At(300));
        _tracker.Track(TestPositions.At(400));
        _tracker.Track(TestPositions.At(500));

        // Act - first flush
        var flushed1 = _tracker.FlushUpTo(TestPositions.At(200));

        // Assert first flush
        flushed1.Length.ShouldBe(2);
        flushed1[0].LogPosition.Value.ShouldBe(100);
        flushed1[1].LogPosition.Value.ShouldBe(200);

        // Act - second flush
        var flushed2 = _tracker.FlushUpTo(TestPositions.At(400));

        // Assert second flush
        flushed2.Length.ShouldBe(2);
        flushed2[0].LogPosition.Value.ShouldBe(300);
        flushed2[1].LogPosition.Value.ShouldBe(400);

        // Assert remaining
        var remaining = _tracker.Positions;
        remaining.Length.ShouldBe(1);
        remaining[0].LogPosition.Value.ShouldBe(500);
    }

    [Test]
    public void flush_up_to_with_duplicate_positions_handles_correctly() {
        // Arrange - track duplicate positions
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));
        _tracker.Track(TestPositions.At(200));

        // Act
        var flushed = _tracker.FlushUpTo(TestPositions.At(150));

        // Assert
        flushed.Length.ShouldBe(2);
        flushed[0].LogPosition.Value.ShouldBe(100);
        flushed[1].LogPosition.Value.ShouldBe(100);

        var remaining = _tracker.Positions;
        remaining.Length.ShouldBe(2);
        remaining[0].LogPosition.Value.ShouldBe(200);
        remaining[1].LogPosition.Value.ShouldBe(200);
    }

    #endregion

    #region . Buffer Management .

    [Test]
    public void buffer_grows_when_capacity_exceeded() {
        // Arrange - create tracker with small capacity
        _tracker.Dispose();
        _tracker = new PositionTracker(capacity: 256);

        // Act - track more than capacity
        for (var i = 1; i <= 300; i++)
            _tracker.Track(TestPositions.At((ulong)(i * 100)));

        // Assert
        _tracker.Positions.Length.ShouldBe(300);
    }

    [Test]
    public void buffer_compacts_before_growing_when_possible() {
        // Arrange - create tracker with minimum capacity
        _tracker.Dispose();
        _tracker = new PositionTracker(capacity: 256);

        // Fill buffer halfway
        for (var i = 1; i <= 128; i++)
            _tracker.Track(TestPositions.At((ulong)(i * 100)));

        // Flush some to create dead space at the front
        _tracker.FlushUpTo(TestPositions.At(64 * 100));

        // Act - add more (should compact first, then possibly grow)
        for (var i = 129; i <= 256; i++)
            _tracker.Track(TestPositions.At((ulong)(i * 100)));

        // Assert
        var positions = _tracker.Positions;
        positions.Length.ShouldBe(192); // 64 remaining (65-128) + 128 new (129-256)
    }

    [Test]
    public void dispose_can_be_called_multiple_times_safely() {
        // Act & Assert - should not throw
        _tracker.Dispose();
        _tracker.Dispose();
        _tracker.Dispose();
    }

    [Test]
    public void operations_after_dispose_throw_object_disposed_exception() {
        // Arrange
        _tracker.Dispose();

        // Act & Assert
        Should.Throw<ObjectDisposedException>(() => _tracker.Track(TestPositions.At(100)));
        Should.Throw<ObjectDisposedException>(() => _ = _tracker.Positions);
        Should.Throw<ObjectDisposedException>(() => _tracker.FlushUpTo(TestPositions.At(100)));
    }

    #endregion

    #region . Reset .

    [Test]
    public void reset_after_partial_flush_clears_pending_positions() {
        // Arrange
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));
        _tracker.Track(TestPositions.At(300));
        _tracker.Track(TestPositions.At(400));
        _tracker.FlushUpTo(TestPositions.At(200));

        // Act
        _tracker.Reset(TestPositions.At(500));

        // Assert
        _tracker.Positions.Length.ShouldBe(0);
        _tracker.LastFlushedPosition.LogPosition.Value.ShouldBe(500);
    }

    [Test]
    public void track_after_reset_enforces_new_baseline() {
        // Arrange
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));
        var lastTracked = _tracker.Positions[^1];
        _tracker.FlushUpTo(lastTracked);
        _tracker.Reset(TestPositions.At(500));

        // Act & Assert - position less than or equal to new baseline should throw
        Should.Throw<InvalidOperationException>(() => _tracker.Track(TestPositions.At(400)));
        Should.Throw<InvalidOperationException>(() => _tracker.Track(TestPositions.At(500)));

        // Position greater than new baseline should succeed
        var count = _tracker.Track(TestPositions.At(600));
        count.ShouldBe(1);
    }

    [Test]
    public void reset_with_unset_position_works() {
        // Arrange
        _tracker.Track(TestPositions.At(100));
        _tracker.Track(TestPositions.At(200));

        // Act
        _tracker.Reset(RecordPosition.Unset);

        // Assert
        _tracker.Positions.Length.ShouldBe(0);
        _tracker.LastFlushedPosition.ShouldBe(RecordPosition.Unset);

        // Should be able to track from the beginning
        _tracker.Track(TestPositions.At(50)).ShouldBe(1);
    }

    #endregion

    #region . Properties .

    [Test]
    public void last_flushed_position_is_unset_initially() {
        // Assert
        _tracker.LastFlushedPosition.ShouldBe(RecordPosition.Unset);
    }

    [Test]
    public void positions_is_empty_initially() {
        // Assert
        _tracker.Positions.Length.ShouldBe(0);
    }

    #endregion
}

static class TestPositions {
    public static RecordPosition At(ulong logPosition) =>
        RecordPosition.FromLogPosition(new LogPosition((long)logPosition));

    public static RecordPosition[] Range(ulong start, int count) =>
        Enumerable.Range(0, count)
            .Select(i => At(start + (ulong)i * 100))
            .ToArray();
}

/// <summary>
/// Helper extension methods for PositionTracker tests.
/// </summary>
static class PositionTrackerTestExtensions {
    /// <summary>
    /// Tracks multiple positions and returns the tracker for chaining.
    /// </summary>
    public static PositionTracker TrackPositions(this PositionTracker tracker, params ulong[] positions) {
        foreach (var pos in positions)
            tracker.Track(TestPositions.At(pos));
        return tracker;
    }

    /// <summary>
    /// Flushes all currently tracked positions.
    /// </summary>
    public static RecordPosition[] FlushAll(this PositionTracker tracker) {
        if (tracker.Positions.Length == 0)
            return [];
        return tracker.FlushUpTo(tracker.Positions[^1]);
    }

    /// <summary>
    /// Asserts that the positions match the expected values.
    /// </summary>
    public static void ShouldHavePositions(this RecordPosition[] positions, params ulong[] expected) {
        positions.Length.ShouldBe(expected.Length);
        for (var i = 0; i < expected.Length; i++)
            positions[i].LogPosition.Value.ShouldBe((long)expected[i]);
    }
}
