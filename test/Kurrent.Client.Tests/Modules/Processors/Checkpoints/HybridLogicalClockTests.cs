using System.Collections.Concurrent;
using Kurrent.Processors.Checkpointing;
using Microsoft.Extensions.Time.Testing;

namespace Kurrent.Client.Tests.Modules.Processors.Checkpoints;

[Category("Processors")]
public class HybridLogicalClockTests {
    #region . HybridTimestamp Value Type .

    [Test]
    public void create_encodes_physical_time_in_upper_48_bits() {
        // Arrange
        const long physicalTime = 1000;
        const int counter = 0;

        // Act
        var timestamp = HybridTimestamp.Create(physicalTime, counter);

        // Assert
        (timestamp.Value >> 16).ShouldBe(physicalTime);
    }

    [Test]
    public void create_encodes_counter_in_lower_16_bits() {
        // Arrange
        const long physicalTime = 1000;
        const int counter = 42;

        // Act
        var timestamp = HybridTimestamp.Create(physicalTime, counter);

        // Assert
        (timestamp.Value & 0xFFFF).ShouldBe(counter);
    }

    [Test]
    public void parse_round_trips_with_create() {
        // Arrange
        const long physicalTime = 123456789;
        const int counter = 12345;
        var original = HybridTimestamp.Create(physicalTime, counter);

        // Act
        var parsed = HybridTimestamp.Parse(original.Value);

        // Assert
        parsed.PhysicalTime.ShouldBe(physicalTime);
        parsed.Counter.ShouldBe(counter);
        parsed.Value.ShouldBe(original.Value);
    }

    [Test]
    public void comparison_orders_by_physical_time_first() {
        // Arrange
        var earlier = HybridTimestamp.Create(99, ushort.MaxValue);
        var later = HybridTimestamp.Create(100, 5);

        // Act & Assert
        (later > earlier).ShouldBeTrue();
        (earlier < later).ShouldBeTrue();
        later.CompareTo(earlier).ShouldBeGreaterThan(0);
    }

    [Test]
    public void comparison_orders_by_counter_when_physical_time_equal() {
        // Arrange
        var earlier = HybridTimestamp.Create(100, 4);
        var later = HybridTimestamp.Create(100, 5);

        // Act & Assert
        (later > earlier).ShouldBeTrue();
        (earlier < later).ShouldBeTrue();
        later.CompareTo(earlier).ShouldBeGreaterThan(0);
    }

    [Test]
    public void create_rejects_negative_physical_time() {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => HybridTimestamp.Create(-1, 0));
    }

    [Test]
    public void create_rejects_counter_exceeding_max() {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => HybridTimestamp.Create(1000, ushort.MaxValue + 1));
    }

    [Test]
    public void create_rejects_negative_counter() {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => HybridTimestamp.Create(1000, -1));
    }

    [Test]
    public void parse_rejects_negative_value() {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => HybridTimestamp.Parse(-1));
    }

    [Test]
    public void try_parse_returns_false_for_negative_value() {
        // Act
        var result = HybridTimestamp.TryParse(-1, out var timestamp);

        // Assert
        result.ShouldBeFalse();
        timestamp.ShouldBe(default);
    }

    [Test]
    public void try_parse_returns_true_for_valid_value() {
        // Arrange
        var original = HybridTimestamp.Create(1000, 42);

        // Act
        var result = HybridTimestamp.TryParse(original.Value, out var parsed);

        // Assert
        result.ShouldBeTrue();
        parsed.PhysicalTime.ShouldBe(1000);
        parsed.Counter.ShouldBe(42);
    }

    [Test]
    public void implicit_conversion_to_long_returns_value() {
        // Arrange
        var timestamp = HybridTimestamp.Create(1000, 42);

        // Act
        long value = timestamp;

        // Assert
        value.ShouldBe(timestamp.Value);
    }

    [Test]
    public void explicit_conversion_from_long_parses_value() {
        // Arrange
        var original = HybridTimestamp.Create(1000, 42);

        // Act
        var parsed = (HybridTimestamp)original.Value;

        // Assert
        parsed.PhysicalTime.ShouldBe(original.PhysicalTime);
        parsed.Counter.ShouldBe(original.Counter);
    }

    #endregion

    #region . Clock.Now() - Monotonicity Guarantee .

    [Test]
    public void now_advances_counter_when_wall_clock_unchanged() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);

        // Act
        var first = clock.Now();
        var second = clock.Now();
        var third = clock.Now();

        // Assert
        first.PhysicalTime.ShouldBe(1000);
        second.PhysicalTime.ShouldBe(1000);
        third.PhysicalTime.ShouldBe(1000);

        first.Counter.ShouldBe(0);
        second.Counter.ShouldBe(1);
        third.Counter.ShouldBe(2);
    }

    [Test]
    public void now_resets_counter_when_wall_clock_advances() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);

        // Act
        var first = clock.Now();
        _ = clock.Now(); // counter = 1

        time.Advance(TimeSpan.FromMilliseconds(1));
        var afterAdvance = clock.Now();

        // Assert
        first.PhysicalTime.ShouldBe(1000);
        first.Counter.ShouldBe(0);

        afterAdvance.PhysicalTime.ShouldBe(1001);
        afterAdvance.Counter.ShouldBe(0);
    }

    [Test]
    public void now_returns_monotonically_increasing_timestamps() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);
        var timestamps = new List<HybridTimestamp>();

        // Act - mix of time advances and same-time calls
        for (var i = 0; i < 100; i++) {
            timestamps.Add(clock.Now());
            if (i % 10 == 0)
                time.Advance(TimeSpan.FromMilliseconds(1));
        }

        // Assert - all strictly increasing
        for (var i = 1; i < timestamps.Count; i++)
            (timestamps[i] > timestamps[i - 1]).ShouldBeTrue($"Timestamp at {i} should be greater than {i - 1}");
    }

    [Test]
    public void now_detects_counter_overflow_when_stuck() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);

        // Don't advance time - force counter to climb
        for (var i = 0; i < ushort.MaxValue; i++)
            clock.Now();

        // Act & Assert - next call should overflow
        Should.Throw<InvalidOperationException>(() => clock.Now());
    }

    #endregion

    #region . Clock.Receive() - Causal Ordering .

    [Test]
    public void receive_advances_past_received_when_local_behind() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);
        var received = HybridTimestamp.Create(2000, 5);

        // Act
        var result = clock.Receive(received);

        // Assert - should be past received
        (result > received).ShouldBeTrue();
        result.PhysicalTime.ShouldBe(2000);
        result.Counter.ShouldBe(6);
    }

    [Test]
    public void receive_advances_past_local_when_received_behind() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(2000));
        var clock = new HybridLogicalClock(time);
        _ = clock.Now(); // local at (2000, 0), internal counter = 1
        _ = clock.Now(); // local at (2000, 1), internal counter = 2

        var received = HybridTimestamp.Create(1000, 100);

        // Act
        var result = clock.Receive(received);

        // Assert - local stays ahead, counter incremented to 3
        result.PhysicalTime.ShouldBe(2000);
        result.Counter.ShouldBe(3);
    }

    [Test]
    public void receive_merges_counters_when_physical_times_equal() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);
        _ = clock.Now(); // local at (1000, 0)
        _ = clock.Now(); // local at (1000, 1)
        _ = clock.Now(); // local at (1000, 2)

        var received = HybridTimestamp.Create(1000, 10);

        // Act
        var result = clock.Receive(received);

        // Assert - should be max(2, 10) + 1 = 11
        result.PhysicalTime.ShouldBe(1000);
        result.Counter.ShouldBe(11);
    }

    [Test]
    public void receive_uses_wall_clock_when_it_exceeds_both() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(3000));
        var clock = new HybridLogicalClock(time);
        _ = clock.Now(); // local at (3000, 0)

        // now wall clock advanced past both
        time.Advance(TimeSpan.FromMilliseconds(1000)); // wall clock at 4000

        var received = HybridTimestamp.Create(2000, 100);

        // Act
        var result = clock.Receive(received);

        // Assert - wall clock wins
        result.PhysicalTime.ShouldBe(4000);
        result.Counter.ShouldBe(0);
    }

    [Test]
    public void receive_throws_on_excessive_clock_drift() {
        // Arrange
        var maxOffset = TimeSpan.FromSeconds(10);
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time, maxOffset);

        // received timestamp is 11 seconds ahead (exceeds 10s max offset)
        var received = HybridTimestamp.Create(12000, 0);

        // Act & Assert
        Should.Throw<ClockDriftException>(() => clock.Receive(received));
    }

    [Test]
    public void receive_allows_drift_at_threshold() {
        // Arrange
        var maxOffset = TimeSpan.FromSeconds(10);
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time, maxOffset);

        // received timestamp is exactly 10 seconds ahead (at threshold)
        var received = HybridTimestamp.Create(11000, 0);

        // Act & Assert - should not throw
        var result = clock.Receive(received);
        result.PhysicalTime.ShouldBe(11000);
        result.Counter.ShouldBe(1);
    }

    #endregion

    #region . Clock.Sync() - Catch-Up Without Advance .

    [Test]
    public void sync_catches_up_when_behind() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);
        var targetTimestamp = HybridTimestamp.Create(2000, 50);

        // Act
        clock.Sync(targetTimestamp);

        // Assert
        clock.Current.PhysicalTime.ShouldBe(2000);
        clock.Current.Counter.ShouldBe(50);
    }

    [Test]
    public void sync_preserves_state_when_already_ahead() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(3000));
        var clock = new HybridLogicalClock(time);
        _ = clock.Now(); // advance to (3000, 0)
        _ = clock.Now(); // advance to (3000, 1)

        var behindTimestamp = HybridTimestamp.Create(2000, 100);

        // Act
        clock.Sync(behindTimestamp);

        // Assert - no change since local is ahead
        clock.Current.PhysicalTime.ShouldBe(3000);
        clock.Current.Counter.ShouldBe(2);
    }

    [Test]
    public void sync_catches_up_counter_when_physical_time_equal() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);
        _ = clock.Now(); // (1000, 0)
        _ = clock.Now(); // (1000, 1)

        var samePhysicalHigherCounter = HybridTimestamp.Create(1000, 50);

        // Act
        clock.Sync(samePhysicalHigherCounter);

        // Assert
        clock.Current.PhysicalTime.ShouldBe(1000);
        clock.Current.Counter.ShouldBe(50);
    }

    [Test]
    public void sync_throws_on_excessive_drift() {
        // Arrange
        var maxOffset = TimeSpan.FromSeconds(10);
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time, maxOffset);

        // timestamp is 11 seconds ahead
        var futureTimestamp = HybridTimestamp.Create(12000, 0);

        // Act & Assert
        Should.Throw<ClockDriftException>(() => clock.Sync(futureTimestamp));
    }

    #endregion

    #region . Thread Safety .

    [Test]
    public async Task concurrent_now_calls_produce_unique_timestamps() {
        // Arrange
        var clock = new HybridLogicalClock();
        var timestamps = new ConcurrentBag<HybridTimestamp>();
        const int callsPerThread = 100;
        const int threadCount = 10;

        // Act
        await Parallel.ForEachAsync(
            Enumerable.Range(0, callsPerThread * threadCount),
            new ParallelOptions { MaxDegreeOfParallelism = threadCount },
            async (_, _) => {
                timestamps.Add(clock.Now());
                await Task.Yield();
            }
        );

        // Assert
        timestamps.Distinct().Count().ShouldBe(callsPerThread * threadCount);
    }

    [Test]
    public async Task concurrent_receive_maintains_monotonicity() {
        // Arrange
        var clock = new HybridLogicalClock();
        var timestamps = new ConcurrentBag<HybridTimestamp>();
        const int threadCount = 10;
        const int operationsPerThread = 50;

        // Act - mix of Now() and Receive() calls
        await Parallel.ForEachAsync(
            Enumerable.Range(0, threadCount * operationsPerThread),
            new ParallelOptions { MaxDegreeOfParallelism = threadCount },
            async (i, _) => {
                if (i % 3 == 0) {
                    // Create a timestamp close to current time
                    var received = HybridTimestamp.Create(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0);
                    timestamps.Add(clock.Receive(received));
                }
                else {
                    timestamps.Add(clock.Now());
                }
                await Task.Yield();
            }
        );

        // Assert - all unique
        var list = timestamps.ToList();
        list.Distinct().Count().ShouldBe(list.Count);
    }

    #endregion

    #region . Constructor Initialization .

    [Test]
    public void constructor_with_initial_timestamp_syncs_when_ahead_of_wall_clock() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var initialTimestamp = HybridTimestamp.Create(2000, 50);

        // Act
        var clock = new HybridLogicalClock(initialTimestamp, time);

        // Assert - should restore from persisted state since initial > wall clock
        clock.Current.PhysicalTime.ShouldBe(2000);
        clock.Current.Counter.ShouldBe(50);
    }

    [Test]
    public void constructor_with_initial_timestamp_uses_wall_clock_when_ahead() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(3000));
        var initialTimestamp = HybridTimestamp.Create(1000, 50);

        // Act
        var clock = new HybridLogicalClock(initialTimestamp, time);

        // Assert - wall clock wins, counter reset
        clock.Current.PhysicalTime.ShouldBe(3000);
        clock.Current.Counter.ShouldBe(0);
    }

    [Test]
    public void constructor_uses_system_time_provider_by_default() {
        // Act
        var clock = new HybridLogicalClock();
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var timestamp = clock.Now();
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Assert - physical time should be close to actual time
        timestamp.PhysicalTime.ShouldBeGreaterThanOrEqualTo(before);
        timestamp.PhysicalTime.ShouldBeLessThanOrEqualTo(after);
    }

    [Test]
    public void constructor_uses_default_max_offset() {
        // Act
        var clock = new HybridLogicalClock();

        // Assert
        clock.MaxOffset.ShouldBe(HybridLogicalClock.DefaultMaxOffset);
    }

    #endregion
}
