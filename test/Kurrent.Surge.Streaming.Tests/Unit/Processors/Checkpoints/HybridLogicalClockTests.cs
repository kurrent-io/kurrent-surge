using System.Collections.Concurrent;
using Kurrent.Surge.Checkpointing;
using Microsoft.Extensions.Time.Testing;

namespace Kurrent.Surge.Streaming.Tests.Checkpoints;

[Category("Processors")]
public class HybridLogicalClockTests {
    #region ->> HybridTimestamp Value Type <<-

    [Test]
    public async ValueTask create_encodes_physical_time_above_counter_bits() {
        // Arrange
        const long physicalTime = 1000;
        const int counter = 0;

        // Act
        var timestamp = HybridTimestamp.Create(physicalTime, counter);

        // Assert
        await Assert.That(timestamp.Value >> 16).IsEqualTo(physicalTime);
    }

    [Test]
    public async ValueTask create_encodes_counter_in_lower_16_bits() {
        // Arrange
        const long physicalTime = 1000;
        const int counter = 42;

        // Act
        var timestamp = HybridTimestamp.Create(physicalTime, counter);

        // Assert
        await Assert.That(timestamp.Value & 0xFFFF).IsEqualTo(counter);
    }

    [Test]
    public async ValueTask parse_round_trips_with_create() {
        // Arrange
        const long physicalTime = 123456789;
        const int counter = 12345;
        var original = HybridTimestamp.Create(physicalTime, counter);

        // Act
        var parsed = HybridTimestamp.Parse(original.Value);

        // Assert
        await Assert.That(parsed.PhysicalTime).IsEqualTo(physicalTime);
        await Assert.That(parsed.Counter).IsEqualTo(counter);
        await Assert.That(parsed.Value).IsEqualTo(original.Value);
    }

    [Test]
    public async ValueTask comparison_orders_by_physical_time_first() {
        // Arrange
        var earlier = HybridTimestamp.Create(99, ushort.MaxValue);
        var later = HybridTimestamp.Create(100, 5);

        // Act & Assert
        await Assert.That(later > earlier).IsTrue();
        await Assert.That(earlier < later).IsTrue();
        await Assert.That(later.CompareTo(earlier)).IsGreaterThan(0);
    }

    [Test]
    public async ValueTask comparison_orders_by_counter_when_physical_time_equal() {
        // Arrange
        var earlier = HybridTimestamp.Create(100, 4);
        var later = HybridTimestamp.Create(100, 5);

        // Act & Assert
        await Assert.That(later > earlier).IsTrue();
        await Assert.That(earlier < later).IsTrue();
        await Assert.That(later.CompareTo(earlier)).IsGreaterThan(0);
    }

    [Test]
    public async ValueTask create_rejects_negative_physical_time() {
        // Act & Assert
        await Assert.That(() => HybridTimestamp.Create(-1, 0)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async ValueTask create_rejects_counter_exceeding_max() {
        // Act & Assert
        await Assert.That(() => HybridTimestamp.Create(1000, ushort.MaxValue + 1)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async ValueTask create_rejects_negative_counter() {
        // Act & Assert
        await Assert.That(() => HybridTimestamp.Create(1000, -1)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async ValueTask parse_round_trips_canonical_string() {
        // Arrange
        var original  = HybridTimestamp.Create(123456789, 12345);
        var canonical = original.ToString();

        // Act
        var parsed = HybridTimestamp.Parse(canonical);

        // Assert
        await Assert.That(parsed).IsEqualTo(original);
    }

    [Test]
    public async ValueTask equal_packed_values_compare_equal_regardless_of_creation_path() {
        // Arrange - sub-millisecond ticks must not leak into equality
        var instant      = DateTimeOffset.UnixEpoch.AddMilliseconds(1000).AddTicks(1234);
        var fromInstant  = HybridTimestamp.Create(instant, 42);
        var fromPacked   = HybridTimestamp.Parse(fromInstant.Value);

        // Act & Assert
        await Assert.That(fromInstant).IsEqualTo(fromPacked);
    }

    [Test]
    public async ValueTask default_timestamp_equals_zero_timestamp() {
        // Arrange
        var zero = HybridTimestamp.Create(0, 0);

        // Act & Assert
        await Assert.That(default(HybridTimestamp)).IsEqualTo(zero);
    }

    [Test]
    public async ValueTask create_rejects_physical_time_exceeding_packed_range() {
        // Arrange - 2^47 ms cannot fit in the 47 usable bits of the packed value
        const long overflowingPhysicalTime = 1L << 47;

        // Act & Assert
        await Assert.That(() => HybridTimestamp.Create(overflowingPhysicalTime, 0)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async ValueTask parse_rejects_negative_value() {
        // Act & Assert
        await Assert.That(() => HybridTimestamp.Parse(-1)).Throws<FormatException>();
    }

    [Test]
    public async ValueTask try_parse_returns_false_for_negative_value() {
        // Act
        var result = HybridTimestamp.TryParse(-1, out var timestamp);

        // Assert
        await Assert.That(result).IsFalse();
        await Assert.That(timestamp).IsEqualTo(default(HybridTimestamp));
    }

    [Test]
    public async ValueTask try_parse_returns_true_for_valid_value() {
        // Arrange
        var original = HybridTimestamp.Create(1000, 42);

        // Act
        var result = HybridTimestamp.TryParse(original.Value, out var parsed);

        // Assert
        await Assert.That(result).IsTrue();
        await Assert.That(parsed.PhysicalTime).IsEqualTo(1000);
        await Assert.That(parsed.Counter).IsEqualTo(42);
    }

    [Test]
    public async ValueTask implicit_conversion_to_long_returns_value() {
        // Arrange
        var timestamp = HybridTimestamp.Create(1000, 42);

        // Act
        long value = timestamp;

        // Assert
        await Assert.That(value).IsEqualTo(timestamp.Value);
    }

    [Test]
    public async ValueTask explicit_conversion_from_long_parses_value() {
        // Arrange
        var original = HybridTimestamp.Create(1000, 42);

        // Act
        var parsed = (HybridTimestamp)original.Value;

        // Assert
        await Assert.That(parsed.PhysicalTime).IsEqualTo(original.PhysicalTime);
        await Assert.That(parsed.Counter).IsEqualTo(original.Counter);
    }

    #endregion // HybridTimestamp Value Type

    #region ->> HybridTimestamp Formatting <<-

    [Test]
    public async ValueTask to_string_emits_fixed_width_canonical_form() {
        // Arrange
        var timestamp = HybridTimestamp.Create(1000, 42);
        const string expected = "0000000000000001000.00042";

        // Act
        var result = timestamp.ToString();

        // Assert
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask try_format_utf16_matches_to_string() {
        // Arrange
        var timestamp   = HybridTimestamp.Create(123456789, 12345);
        var expected    = timestamp.ToString();
        var destination = new char[expected.Length];

        // Act
        var success = timestamp.TryFormat(destination, out var charsWritten, default, null);
        var result  = new string(destination, 0, charsWritten);

        // Assert
        await Assert.That(success).IsTrue();
        await Assert.That(charsWritten).IsEqualTo(expected.Length);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask try_format_utf8_matches_to_string() {
        // Arrange
        var timestamp   = HybridTimestamp.Create(123456789, 12345);
        var expected    = timestamp.ToString();
        var destination = new byte[expected.Length];

        // Act
        var success = timestamp.TryFormat(destination, out var bytesWritten, default, null);
        var result  = System.Text.Encoding.UTF8.GetString(destination, 0, bytesWritten);

        // Assert
        await Assert.That(success).IsTrue();
        await Assert.That(bytesWritten).IsEqualTo(expected.Length);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask try_format_utf16_returns_false_when_destination_too_small() {
        // Arrange
        var timestamp   = HybridTimestamp.Create(1000, 42);
        var destination = new char[timestamp.ToString().Length - 1];

        // Act
        var success = timestamp.TryFormat(destination, out var charsWritten, default, null);

        // Assert
        await Assert.That(success).IsFalse();
        await Assert.That(charsWritten).IsEqualTo(0);
    }

    [Test]
    public async ValueTask try_format_utf8_returns_false_when_destination_too_small() {
        // Arrange
        var timestamp   = HybridTimestamp.Create(1000, 42);
        var destination = new byte[timestamp.ToString().Length - 1];

        // Act
        var success = timestamp.TryFormat(destination, out var bytesWritten, default, null);

        // Assert
        await Assert.That(success).IsFalse();
        await Assert.That(bytesWritten).IsEqualTo(0);
    }

    [Test]
    public async ValueTask try_parse_chars_rejects_malformed_input() {
        // Arrange - wrong width, wrong separator, non-digits, counter past 16-bit range
        string[] malformed = ["", "123.45", "0000000000000001000-00042", "000000000000000100A.00042", "0000000000000001000.99999"];

        // Act & Assert
        foreach (var value in malformed) {
            var parsed = HybridTimestamp.TryParse(value.AsSpan(), out _);
            await Assert.That(parsed).IsFalse();
        }
    }

    [Test]
    public async ValueTask parse_utf8_round_trips_canonical_form() {
        // Arrange
        var original = HybridTimestamp.Create(123456789, 12345);
        var utf8     = new byte[25];
        original.TryFormat(utf8, out _, default, null);

        // Act
        var parsed = HybridTimestamp.Parse((ReadOnlySpan<byte>)utf8);

        // Assert
        await Assert.That(parsed).IsEqualTo(original);
    }

    [Test]
    public async ValueTask string_conversion_round_trips() {
        // Arrange
        var original = HybridTimestamp.Create(1000, 42);

        // Act
        string text   = original;
        var    parsed = (HybridTimestamp)text;

        // Assert
        await Assert.That(parsed).IsEqualTo(original);
    }

    [Test]
    public async ValueTask to_string_sorts_lexicographically_in_value_order() {
        // Arrange — counter rollover boundary: lower counter at later physical time
        // must still sort after higher counter at earlier physical time.
        var earlier = HybridTimestamp.Create(99, ushort.MaxValue);
        var later   = HybridTimestamp.Create(100, 5);

        // Act
        var ordinal = string.CompareOrdinal(earlier.ToString(), later.ToString());

        // Assert
        await Assert.That(ordinal).IsLessThan(0);
        await Assert.That(earlier < later).IsTrue();
    }

    #endregion // HybridTimestamp Formatting

    #region ->> Clock.Now() - Monotonicity Guarantee <<-

    [Test]
    public async ValueTask now_advances_counter_when_wall_clock_unchanged() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);

        // Act
        var first = clock.Now();
        var second = clock.Now();
        var third = clock.Now();

        // Assert
        await Assert.That(first.PhysicalTime).IsEqualTo(1000);
        await Assert.That(second.PhysicalTime).IsEqualTo(1000);
        await Assert.That(third.PhysicalTime).IsEqualTo(1000);

        await Assert.That(first.Counter).IsEqualTo(0);
        await Assert.That(second.Counter).IsEqualTo(1);
        await Assert.That(third.Counter).IsEqualTo(2);
    }

    [Test]
    public async ValueTask now_resets_counter_when_wall_clock_advances() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);

        // Act
        var first = clock.Now();
        _ = clock.Now(); // counter = 1

        time.Advance(TimeSpan.FromMilliseconds(1));
        var afterAdvance = clock.Now();

        // Assert
        await Assert.That(first.PhysicalTime).IsEqualTo(1000);
        await Assert.That(first.Counter).IsEqualTo(0);

        await Assert.That(afterAdvance.PhysicalTime).IsEqualTo(1001);
        await Assert.That(afterAdvance.Counter).IsEqualTo(0);
    }

    [Test]
    public async ValueTask now_returns_monotonically_increasing_timestamps() {
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
            await Assert.That(timestamps[i] > timestamps[i - 1]).IsTrue();
    }

    [Test]
    public async ValueTask now_detects_counter_overflow_when_stuck() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);

        // Don't advance time - issue the full 16-bit counter range
        for (var i = 0; i <= ushort.MaxValue; i++)
            clock.Now();

        // Act & Assert - next call should overflow
        await Assert.That(() => clock.Now()).Throws<CounterOverflowException>();
    }

    [Test]
    public async ValueTask now_issues_full_counter_range_before_overflow() {
        // Arrange
        var time  = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);
        const int expectedLastCounter = ushort.MaxValue;

        // Act - issue every counter value in the 16-bit range at a stuck wall clock
        var last = default(HybridTimestamp);
        for (var i = 0; i <= ushort.MaxValue; i++)
            last = clock.Now();

        // Assert
        await Assert.That(last.Counter).IsEqualTo(expectedLastCounter);
    }

    [Test]
    public async ValueTask now_keeps_throwing_counter_overflow_after_exhaustion() {
        // Arrange - exhaust the counter range at a stuck wall clock
        var time  = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);

        for (var i = 0; i <= ushort.MaxValue; i++)
            try { clock.Now(); } catch (CounterOverflowException) { break; }

        // Act & Assert - every subsequent call reports overflow, not an argument error
        await Assert.That(() => clock.Now()).Throws<CounterOverflowException>();
    }

    #endregion // Clock.Now() - Monotonicity Guarantee

    #region ->> Clock.Receive() - Causal Ordering <<-

    [Test]
    public async ValueTask receive_advances_past_received_when_local_behind() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);
        var received = HybridTimestamp.Create(2000, 5);

        // Act
        var result = clock.Receive(received);

        // Assert - should be past received
        await Assert.That(result > received).IsTrue();
        await Assert.That(result.PhysicalTime).IsEqualTo(2000);
        await Assert.That(result.Counter).IsEqualTo(6);
    }

    [Test]
    public async ValueTask receive_advances_past_local_when_received_behind() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(2000));
        var clock = new HybridLogicalClock(time);
        _ = clock.Now(); // local at (2000, 0), internal counter = 1
        _ = clock.Now(); // local at (2000, 1), internal counter = 2

        var received = HybridTimestamp.Create(1000, 100);

        // Act
        var result = clock.Receive(received);

        // Assert - local stays ahead; paper rule c = last issued + 1, no skipped counter values
        await Assert.That(result.PhysicalTime).IsEqualTo(2000);
        await Assert.That(result.Counter).IsEqualTo(2);
    }

    [Test]
    public async ValueTask receive_merges_counters_when_physical_times_equal() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);
        _ = clock.Now(); // local at (1000, 0)
        _ = clock.Now(); // local at (1000, 1)
        _ = clock.Now(); // local at (1000, 2)

        var received = HybridTimestamp.Create(1000, 10);

        // Act
        var result = clock.Receive(received);

        // Assert - paper rule: max(last issued 2, received 10) + 1 = 11
        await Assert.That(result.PhysicalTime).IsEqualTo(1000);
        await Assert.That(result.Counter).IsEqualTo(11);
    }

    [Test]
    public async ValueTask receive_merge_does_not_skip_counter_when_local_dominates() {
        // Arrange - same physical time, local counter dominates the received one
        var time  = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);
        _ = clock.Now(); // issued (1000, 0)
        _ = clock.Now(); // issued (1000, 1)
        _ = clock.Now(); // issued (1000, 2)

        var received = HybridTimestamp.Create(1000, 1);

        // Act
        var result = clock.Receive(received);

        // Assert - paper rule: max(last issued 2, received 1) + 1 = 3, no skipped counter values
        await Assert.That(result.PhysicalTime).IsEqualTo(1000);
        await Assert.That(result.Counter).IsEqualTo(3);
    }

    [Test]
    public async ValueTask receive_uses_wall_clock_when_it_exceeds_both() {
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
        await Assert.That(result.PhysicalTime).IsEqualTo(4000);
        await Assert.That(result.Counter).IsEqualTo(0);
    }

    [Test]
    public async ValueTask receive_throws_on_excessive_clock_drift() {
        // Arrange
        var maxOffset = TimeSpan.FromSeconds(10);
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time, maxOffset);

        // received timestamp is 11 seconds ahead (exceeds 10s max offset)
        var received = HybridTimestamp.Create(12000, 0);

        // Act & Assert
        await Assert.That(() => clock.Receive(received)).Throws<ClockDriftException>();
    }

    [Test]
    public async ValueTask receive_allows_drift_at_threshold() {
        // Arrange
        var maxOffset = TimeSpan.FromSeconds(10);
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time, maxOffset);

        // received timestamp is exactly 10 seconds ahead (at threshold)
        var received = HybridTimestamp.Create(11000, 0);

        // Act & Assert - should not throw
        var result = clock.Receive(received);
        await Assert.That(result.PhysicalTime).IsEqualTo(11000);
        await Assert.That(result.Counter).IsEqualTo(1);
    }

    #endregion // Clock.Receive() - Causal Ordering

    #region ->> Clock.Sync() - Catch-Up Without Advance <<-

    [Test]
    public async ValueTask sync_catches_up_when_behind() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);
        var targetTimestamp = HybridTimestamp.Create(2000, 50);

        // Act
        clock.Sync(targetTimestamp);

        // Assert - next issued counter starts past the synced timestamp, which was already issued
        await Assert.That(clock.Current.PhysicalTime).IsEqualTo(2000);
        await Assert.That(clock.Current.Counter).IsEqualTo(51);
    }

    [Test]
    public async ValueTask sync_preserves_state_when_already_ahead() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(3000));
        var clock = new HybridLogicalClock(time);
        _ = clock.Now(); // advance to (3000, 0)
        _ = clock.Now(); // advance to (3000, 1)

        var behindTimestamp = HybridTimestamp.Create(2000, 100);

        // Act
        clock.Sync(behindTimestamp);

        // Assert - no change since local is ahead
        await Assert.That(clock.Current.PhysicalTime).IsEqualTo(3000);
        await Assert.That(clock.Current.Counter).IsEqualTo(2);
    }

    [Test]
    public async ValueTask sync_catches_up_counter_when_physical_time_equal() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time);
        _ = clock.Now(); // (1000, 0)
        _ = clock.Now(); // (1000, 1)

        var samePhysicalHigherCounter = HybridTimestamp.Create(1000, 50);

        // Act
        clock.Sync(samePhysicalHigherCounter);

        // Assert - next issued counter starts past the synced timestamp, which was already issued
        await Assert.That(clock.Current.PhysicalTime).IsEqualTo(1000);
        await Assert.That(clock.Current.Counter).IsEqualTo(51);
    }

    [Test]
    public async ValueTask sync_throws_on_excessive_drift() {
        // Arrange
        var maxOffset = TimeSpan.FromSeconds(10);
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock = new HybridLogicalClock(time, maxOffset);

        // timestamp is 11 seconds ahead
        var futureTimestamp = HybridTimestamp.Create(12000, 0);

        // Act & Assert
        await Assert.That(() => clock.Sync(futureTimestamp)).Throws<ClockDriftException>();
    }

    [Test]
    public async ValueTask now_after_sync_exceeds_synced_timestamp() {
        // Arrange - synced timestamp was persisted, so it was already issued once
        var time      = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var clock     = new HybridLogicalClock(time);
        var persisted = HybridTimestamp.Create(2000, 50);

        // Act
        clock.Sync(persisted);
        var next = clock.Now();

        // Assert
        await Assert.That(next > persisted).IsTrue();
    }

    #endregion // Clock.Sync() - Catch-Up Without Advance

    #region ->> Thread Safety <<-

    [Test]
    public async ValueTask concurrent_now_calls_produce_unique_timestamps() {
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
        await Assert.That(timestamps.Distinct().Count()).IsEqualTo(callsPerThread * threadCount);
    }

    [Test]
    public async ValueTask concurrent_receive_maintains_monotonicity() {
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
        await Assert.That(list.Distinct().Count()).IsEqualTo(list.Count);
    }

    #endregion // Thread Safety

    #region ->> Constructor Initialization <<-

    [Test]
    public async ValueTask constructor_with_initial_timestamp_syncs_when_ahead_of_wall_clock() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var initialTimestamp = HybridTimestamp.Create(2000, 50);

        // Act
        var clock = new HybridLogicalClock(initialTimestamp, time);

        // Assert - restored from persisted state; next issued counter starts past it
        await Assert.That(clock.Current.PhysicalTime).IsEqualTo(2000);
        await Assert.That(clock.Current.Counter).IsEqualTo(51);
    }

    [Test]
    public async ValueTask constructor_with_initial_timestamp_uses_wall_clock_when_ahead() {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(3000));
        var initialTimestamp = HybridTimestamp.Create(1000, 50);

        // Act
        var clock = new HybridLogicalClock(initialTimestamp, time);

        // Assert - wall clock wins, counter reset
        await Assert.That(clock.Current.PhysicalTime).IsEqualTo(3000);
        await Assert.That(clock.Current.Counter).IsEqualTo(0);
    }

    [Test]
    public async ValueTask constructor_uses_system_time_provider_by_default() {
        // Act
        var clock = new HybridLogicalClock();
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var timestamp = clock.Now();
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Assert - physical time should be close to actual time
        await Assert.That(timestamp.PhysicalTime).IsGreaterThanOrEqualTo(before);
        await Assert.That(timestamp.PhysicalTime).IsLessThanOrEqualTo(after);
    }

    [Test]
    public async ValueTask constructor_uses_default_max_offset() {
        // Act
        var clock = new HybridLogicalClock();

        // Assert
        await Assert.That(clock.MaxOffset).IsEqualTo(HybridLogicalClock.DefaultMaxOffset);
    }

    [Test]
    public async ValueTask restoring_constructor_throws_on_excessive_drift() {
        // Arrange - same guard as Sync: a persisted timestamp 11s ahead of a 10s max offset
        // indicates a corrupted checkpoint or misconfigured clock and must not seed the clock
        var maxOffset = TimeSpan.FromSeconds(10);
        var time      = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var persisted = HybridTimestamp.Create(12000, 0);

        // Act & Assert
        await Assert.That(() => new HybridLogicalClock(persisted, time, maxOffset)).Throws<ClockDriftException>();
    }

    [Test]
    public async ValueTask restoring_constructor_allows_drift_at_threshold() {
        // Arrange - exactly at the max offset boundary, mirroring receive_allows_drift_at_threshold
        var maxOffset = TimeSpan.FromSeconds(10);
        var time      = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var persisted = HybridTimestamp.Create(11000, 0);

        // Act
        var clock = new HybridLogicalClock(persisted, time, maxOffset);

        // Assert
        await Assert.That(clock.Now() > persisted).IsTrue();
    }

    [Test]
    public async ValueTask restored_clock_never_reissues_persisted_timestamp() {
        // Arrange - wall clock behind the persisted timestamp
        var time      = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(1000));
        var persisted = HybridTimestamp.Create(2000, 50);
        var clock     = new HybridLogicalClock(persisted, time);

        // Act
        var next = clock.Now();

        // Assert
        await Assert.That(next > persisted).IsTrue();
    }

    [Test]
    public async ValueTask restored_clock_advances_past_persisted_timestamp_when_wall_clock_equal() {
        // Arrange - wall clock at the same millisecond as the persisted timestamp
        var time      = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddMilliseconds(2000));
        var persisted = HybridTimestamp.Create(2000, 50);
        var clock     = new HybridLogicalClock(persisted, time);

        // Act
        var next = clock.Now();

        // Assert
        await Assert.That(next > persisted).IsTrue();
    }

    #endregion // Constructor Initialization
}
