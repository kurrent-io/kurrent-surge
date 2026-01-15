// using Kurrent.Client.Processors.Checkpointing;
// using Kurrent.Client.Streams;
// using Kurrent.Client.Streams.Processing;
//
// namespace Kurrent.Client.Tests.Modules.Processors.Checkpoints;
//
// [Category("Processors")]
// public class PositionTrackerTests : IDisposable {
//     PositionTracker _tracker = new();
//
//     public void Dispose() => _tracker.Dispose();
//
//     #region . Track .
//
//     [Test]
//     public void track_single_position_returns_count_of_one() {
//         // Arrange
//         var position = TestPositions.At(100);
//
//         // Act
//         var count = _tracker.Track(position);
//
//         // Assert
//         count.ShouldBe(1);
//     }
//
//     [Test]
//     public void track_multiple_positions_returns_incremented_count() {
//         // Arrange & Act
//         var count1 = _tracker.Track(TestPositions.At(100));
//         var count2 = _tracker.Track(TestPositions.At(200));
//         var count3 = _tracker.Track(TestPositions.At(300));
//
//         // Assert
//         count1.ShouldBe(1);
//         count2.ShouldBe(2);
//         count3.ShouldBe(3);
//     }
//
//     [Test]
//     public void track_unset_position_throws_argument_exception() {
//         // Arrange
//         var unset = RecordPosition.Unset;
//
//         // Act & Assert
//         Should.Throw<ArgumentException>(() => _tracker.Track(unset));
//     }
//
//     [Test]
//     public void track_position_less_than_or_equal_to_last_flushed_throws() {
//         // Arrange
//         _tracker.Track(TestPositions.At(100));
//         _tracker.Track(TestPositions.At(200));
//         _tracker.Flush();
//
//         // Act & Assert
//         Should.Throw<InvalidOperationException>(() => _tracker.Track(TestPositions.At(150)));
//         Should.Throw<InvalidOperationException>(() => _tracker.Track(TestPositions.At(200)));
//     }
//
//     [Test]
//     public void positions_returns_all_tracked_positions_in_order() {
//         // Arrange
//         _tracker.Track(TestPositions.At(100));
//         _tracker.Track(TestPositions.At(200));
//         _tracker.Track(TestPositions.At(300));
//
//         // Act
//         var positions = _tracker.Positions;
//
//         // Assert
//         positions.Length.ShouldBe(3);
//         positions[0].LogPosition.Value.ShouldBe(100);
//         positions[1].LogPosition.Value.ShouldBe(200);
//         positions[2].LogPosition.Value.ShouldBe(300);
//     }
//
//     #endregion
//
//     #region . Flush .
//
//     [Test]
//     public void flush_returns_all_tracked_positions() {
//         // Arrange
//         _tracker.Track(TestPositions.At(100));
//         _tracker.Track(TestPositions.At(200));
//
//         // Act
//         var flushed = _tracker.Flush();
//
//         // Assert
//         flushed.Length.ShouldBe(2);
//         flushed[0].LogPosition.Value.ShouldBe(100);
//         flushed[1].LogPosition.Value.ShouldBe(200);
//     }
//
//     [Test]
//     public void flush_clears_tracked_positions() {
//         // Arrange
//         _tracker.Track(TestPositions.At(100));
//         _tracker.Track(TestPositions.At(200));
//
//         // Act
//         _tracker.Flush();
//
//         // Assert
//         _tracker.Positions.Length == 0.ShouldBeTrue();
//     }
//
//     [Test]
//     public void flush_updates_last_flushed_position_to_last_tracked() {
//         // Arrange
//         _tracker.Track(TestPositions.At(100));
//         _tracker.Track(TestPositions.At(200));
//
//         // Act
//         _tracker.Flush();
//
//         // Assert
//         _tracker.LastFlushedPosition.LogPosition.Value.ShouldBe(200);
//     }
//
//     [Test]
//     public void flush_on_empty_tracker_returns_empty_span() {
//         // Act
//         var flushed = _tracker.Flush();
//
//         // Assert
//         flushed.Length == 0.ShouldBeTrue();
//     }
//
//     #endregion
//
//     #region . FlushUpTo .
//
//     [Test]
//     public void flush_up_to_returns_positions_up_to_and_including_barrier() {
//         // Arrange
//         _tracker.Track(TestPositions.At(100));
//         _tracker.Track(TestPositions.At(200));
//         _tracker.Track(TestPositions.At(300));
//         _tracker.Track(TestPositions.At(400));
//
//         // Act
//         var flushed = _tracker.FlushUpTo(TestPositions.At(200));
//
//         // Assert
//         flushed.Length.ShouldBe(2);
//         flushed[0].LogPosition.Value.ShouldBe(100);
//         flushed[1].LogPosition.Value.ShouldBe(200);
//     }
//
//     [Test]
//     public void flush_up_to_retains_positions_after_barrier() {
//         // Arrange
//         _tracker.Track(TestPositions.At(100));
//         _tracker.Track(TestPositions.At(200));
//         _tracker.Track(TestPositions.At(300));
//         _tracker.Track(TestPositions.At(400));
//
//         // Act
//         _tracker.FlushUpTo(TestPositions.At(200));
//
//         // Assert
//         var remaining = _tracker.Positions;
//         remaining.Length.ShouldBe(2);
//         remaining[0].LogPosition.Value.ShouldBe(300);
//         remaining[1].LogPosition.Value.ShouldBe(400);
//     }
//
//     [Test]
//     public void flush_up_to_updates_last_flushed_position() {
//         // Arrange
//         _tracker.Track(TestPositions.At(100));
//         _tracker.Track(TestPositions.At(200));
//         _tracker.Track(TestPositions.At(300));
//
//         // Act
//         _tracker.FlushUpTo(TestPositions.At(200));
//
//         // Assert
//         _tracker.LastFlushedPosition.LogPosition.Value.ShouldBe(200);
//     }
//
//     [Test]
//     public void flush_up_to_updates_last_barrier_position() {
//         // Arrange
//         _tracker.Track(TestPositions.At(100));
//         _tracker.Track(TestPositions.At(200));
//         _tracker.Track(TestPositions.At(300));
//
//         // Act
//         _tracker.FlushUpTo(TestPositions.At(250));
//
//         // Assert
//         _tracker.LastBarrierPosition.LogPosition.Value.ShouldBe(250);
//     }
//
//     [Test]
//     public void flush_up_to_with_untracked_barrier_still_flushes_up_to_it() {
//         // Arrange - barrier at 250 is not tracked, but should flush 100 and 200
//         _tracker.Track(TestPositions.At(100));
//         _tracker.Track(TestPositions.At(200));
//         _tracker.Track(TestPositions.At(300));
//         _tracker.Track(TestPositions.At(400));
//
//         // Act
//         var flushed = _tracker.FlushUpTo(TestPositions.At(250));
//
//         // Assert
//         flushed.Length.ShouldBe(2);
//         flushed[0].LogPosition.Value.ShouldBe(100);
//         flushed[1].LogPosition.Value.ShouldBe(200);
//     }
//
//     [Test]
//     public void flush_up_to_on_empty_tracker_returns_empty_span() {
//         // Act
//         var flushed = _tracker.FlushUpTo(TestPositions.At(100));
//
//         // Assert
//         flushed.Length == 0.ShouldBeTrue();
//     }
//
//     #endregion
//
//     #region . Buffer Management .
//
//     [Test]
//     public void buffer_grows_when_capacity_exceeded() {
//         // Arrange - create tracker with small capacity
//         _tracker.Dispose();
//         _tracker = new PositionTracker(capacity: 4);
//
//         // Act - track more than capacity
//         for (var i = 1; i <= 10; i++)
//             _tracker.Track(TestPositions.At((ulong)(i * 100)));
//
//         // Assert
//         _tracker.Positions.Length.ShouldBe(10);
//     }
//
//     [Test]
//     public void buffer_compacts_before_growing_when_possible() {
//         // Arrange - create tracker with small capacity
//         _tracker.Dispose();
//         _tracker = new PositionTracker(capacity: 8);
//
//         // Fill buffer halfway
//         for (var i = 1; i <= 4; i++)
//             _tracker.Track(TestPositions.At((ulong)(i * 100)));
//
//         // Flush some to create dead space at the front
//         _tracker.FlushUpTo(TestPositions.At(200));
//
//         // Act - add more (should compact first, then possibly grow)
//         for (var i = 5; i <= 10; i++)
//             _tracker.Track(TestPositions.At((ulong)(i * 100)));
//
//         // Assert
//         var positions = _tracker.Positions;
//         positions.Length.ShouldBe(8); // 2 remaining + 6 new
//     }
//
//     [Test]
//     public void dispose_can_be_called_multiple_times_safely() {
//         // Act & Assert - should not throw
//         _tracker.Dispose();
//         _tracker.Dispose();
//         _tracker.Dispose();
//     }
//
//     [Test]
//     public void operations_after_dispose_throw_object_disposed_exception() {
//         // Arrange
//         _tracker.Dispose();
//
//         // Act & Assert
//         Should.Throw<ObjectDisposedException>(() => _tracker.Track(TestPositions.At(100)));
//         Should.Throw<ObjectDisposedException>(() => _ = _tracker.Positions);
//         Should.Throw<ObjectDisposedException>(() => _tracker.Flush());
//         Should.Throw<ObjectDisposedException>(() => _tracker.FlushUpTo(TestPositions.At(100)));
//     }
//
//     #endregion
//
//     #region . Properties .
//
//     [Test]
//     public void last_flushed_position_is_unset_initially() {
//         // Assert
//         _tracker.LastFlushedPosition.ShouldBe(RecordPosition.Unset);
//     }
//
//     [Test]
//     public void last_barrier_position_is_unset_initially() {
//         // Assert
//         _tracker.LastBarrierPosition.ShouldBe(RecordPosition.Unset);
//     }
//
//     [Test]
//     public void positions_is_empty_initially() {
//         // Assert
//         _tracker.Positions.Length == 0.ShouldBeTrue();
//     }
//
//     #endregion
// }
//
// static class TestPositions {
//     public static RecordPosition At(ulong logPosition) =>
//         RecordPosition.FromLogPosition(new LogPosition((long)logPosition));
//
//     public static RecordPosition[] Range(ulong start, int count) =>
//         Enumerable.Range(0, count)
//             .Select(i => At(start + (ulong)i * 100))
//             .ToArray();
// }
