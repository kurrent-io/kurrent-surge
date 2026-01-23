// using JetBrains.Annotations;
//
// namespace Kurrent.Processors.Checkpointing;
//
// /// <summary>
// /// Represents a hybrid logical timestamp combining physical time with a logical counter.
// /// Used for establishing causal ordering of events in distributed systems.
// /// </summary>
// /// <remarks>
// /// The timestamp is encoded as a 64-bit value where the upper 48 bits represent
// /// Unix time in milliseconds and the lower 16 bits represent the counter.
// /// </remarks>
// [PublicAPI]
// public readonly record struct HybridTimestamp : IComparable<HybridTimestamp>, IComparable {
//     /// <summary>
//     /// Gets the physical time component (Unix time in milliseconds).
//     /// </summary>
//     public long PhysicalTime { get; private init; }
//
//     /// <summary>
//     /// Gets the counter component for ordering events within the same millisecond.
//     /// </summary>
//     public int Counter { get; private init; }
//
//     /// <summary>
//     /// Gets the packed 64-bit representation of this timestamp.
//     /// </summary>
//     public long Value { get; private init; }
//
//     /// <inheritdoc />
//     public int CompareTo(HybridTimestamp other) => Value.CompareTo(other.Value);
//
//     /// <inheritdoc />
//     public int CompareTo(object? obj) =>
//         obj is HybridTimestamp other
//             ? CompareTo(other)
//             : throw new ArgumentException($"Object must be of type {nameof(HybridTimestamp)}");
//
//     /// <summary>Determines whether one timestamp is less than another.</summary>
//     public static bool operator <(HybridTimestamp left, HybridTimestamp right) => left.Value < right.Value;
//
//     /// <summary>Determines whether one timestamp is greater than another.</summary>
//     public static bool operator >(HybridTimestamp left, HybridTimestamp right) => left.Value > right.Value;
//
//     /// <summary>Determines whether one timestamp is less than or equal to another.</summary>
//     public static bool operator <=(HybridTimestamp left, HybridTimestamp right) => left.Value <= right.Value;
//
//     /// <summary>Determines whether one timestamp is greater than or equal to another.</summary>
//     public static bool operator >=(HybridTimestamp left, HybridTimestamp right) => left.Value >= right.Value;
//
//     /// <summary>
//     /// Converts a <see cref="HybridTimestamp"/> to its packed 64-bit representation.
//     /// </summary>
//     /// <param name="timestamp">The timestamp to convert.</param>
//     public static implicit operator long(HybridTimestamp timestamp) => timestamp.Value;
//
//     /// <inheritdoc />
//     public override string ToString() => $"{PhysicalTime:D19}.{Counter:D5}";
//
//     /// <summary>
//     /// Creates a new <see cref="HybridTimestamp"/> with the specified physical time and counter.
//     /// </summary>
//     /// <param name="physicalTime">The physical time in Unix milliseconds. Must be non-negative.</param>
//     /// <param name="counter">The counter value. Must be between 0 and 65535 (ushort.MaxValue).</param>
//     /// <returns>A new <see cref="HybridTimestamp"/> instance.</returns>
//     /// <exception cref="ArgumentOutOfRangeException">
//     /// Thrown when <paramref name="physicalTime"/> is negative or <paramref name="counter"/> exceeds ushort.MaxValue.
//     /// </exception>
//     public static HybridTimestamp Create(long physicalTime, int counter) {
//         ArgumentOutOfRangeException.ThrowIfNegative(physicalTime);
//         ArgumentOutOfRangeException.ThrowIfNegative(counter);
//         ArgumentOutOfRangeException.ThrowIfGreaterThan(counter, ushort.MaxValue);
//
//         return new HybridTimestamp {
//             PhysicalTime = physicalTime,
//             Counter      = counter,
//             Value        = Pack(physicalTime, counter)
//         };
//     }
//
//     /// <summary>
//     /// Parses a packed 64-bit value into a <see cref="HybridTimestamp"/>.
//     /// </summary>
//     /// <param name="value">The packed timestamp value. Must be non-negative.</param>
//     /// <returns>A <see cref="HybridTimestamp"/> parsed from the value.</returns>
//     /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is negative.</exception>
//     public static HybridTimestamp Parse(long value) {
//         ArgumentOutOfRangeException.ThrowIfNegative(value);
//
//         return new HybridTimestamp {
//             PhysicalTime = GetPhysicalTime(value),
//             Counter      = GetCounter(value),
//             Value        = value
//         };
//     }
//
//     /// <summary>
//     /// Attempts to parse a packed 64-bit value into a <see cref="HybridTimestamp"/>.
//     /// </summary>
//     /// <param name="value">The packed timestamp value to parse.</param>
//     /// <param name="timestamp">When this method returns, contains the parsed timestamp if successful; otherwise, the default value.</param>
//     /// <returns><c>true</c> if parsing succeeded; otherwise, <c>false</c>.</returns>
//     public static bool TryParse(long value, out HybridTimestamp timestamp) {
//         if (value < 0) {
//             timestamp = default;
//             return false;
//         }
//
//         timestamp = new HybridTimestamp {
//             PhysicalTime = GetPhysicalTime(value),
//             Counter      = GetCounter(value),
//             Value        = value
//         };
//
//         return true;
//     }
//
//     /// <summary>
//     /// Packs a physical time and counter into a single 64-bit value.
//     /// </summary>
//     internal static long Pack(long physicalTime, int counter) =>
//         (physicalTime << 16) | (uint)counter;
//
//     /// <summary>
//     /// Extracts the physical time from a packed 64-bit value.
//     /// </summary>
//     internal static long GetPhysicalTime(long packed) => packed >> 16;
//
//     /// <summary>
//     /// Extracts the counter from a packed 64-bit value.
//     /// </summary>
//     internal static int GetCounter(long packed) => (int)(packed & 0xFFFF);
// }
//
// /// <summary>
// /// A thread-safe, lock-free Hybrid Logical Clock (HLC) implementation for generating causally ordered timestamps.
// /// </summary>
// /// <remarks>
// /// <para>
// /// HLC combines physical time with a logical counter to provide timestamps that:
// /// <list type="bullet">
// ///   <item>Are monotonically increasing within a single node</item>
// ///   <item>Preserve causal ordering across distributed nodes when using <see cref="Receive"/></item>
// ///   <item>Stay close to physical wall-clock time</item>
// /// </list>
// /// </para>
// /// <para>
// /// This implementation uses lock-free CAS (Compare-And-Swap) operations for high-performance
// /// concurrent access, similar to <see cref="LamportClock"/>.
// /// </para>
// /// <para>
// /// Reference: "Logical Physical Clocks and Consistent Snapshots in Globally Distributed Databases"
// /// by Kulkarni et al.
// /// </para>
// /// </remarks>
// [PublicAPI]
// public sealed class HybridLogicalClock {
//     /// <summary>
//     /// The default maximum tolerated clock offset (1 minute).
//     /// </summary>
//     public static readonly TimeSpan DefaultMaxOffset = TimeSpan.FromMinutes(1);
//
//     readonly TimeProvider _timeProvider;
//     readonly long         _maxOffsetMs;
//
//     /// <summary>
//     /// Packed state: upper 48 bits = physical time, lower 16 bits = counter.
//     /// </summary>
//     long _state;
//
//     /// <summary>
//     /// Initializes a new instance of the <see cref="HybridLogicalClock"/> class.
//     /// </summary>
//     /// <param name="timeProvider">
//     /// The time provider for obtaining physical time. If <c>null</c>, uses <see cref="TimeProvider.System"/>.
//     /// </param>
//     /// <param name="maxOffset">
//     /// The maximum tolerated clock offset. Timestamps received with drift exceeding this value will be rejected.
//     /// If <c>null</c>, uses <see cref="DefaultMaxOffset"/>.
//     /// </param>
//     public HybridLogicalClock(TimeProvider? timeProvider = null, TimeSpan? maxOffset = null) {
//         _timeProvider = timeProvider ?? TimeProvider.System;
//         _maxOffsetMs  = (long)(maxOffset ?? DefaultMaxOffset).TotalMilliseconds;
//         _state        = HybridTimestamp.Pack(GetWallClockTime(), 0);
//     }
//
//     /// <summary>
//     /// Initializes a new instance of the <see cref="HybridLogicalClock"/> class with an initial timestamp.
//     /// </summary>
//     /// <param name="initialTimestamp">
//     /// The initial timestamp to synchronize with, typically restored from persisted state.
//     /// The clock will be at least this value.
//     /// </param>
//     /// <param name="timeProvider">
//     /// The time provider for obtaining physical time. If <c>null</c>, uses <see cref="TimeProvider.System"/>.
//     /// </param>
//     /// <param name="maxOffset">
//     /// The maximum tolerated clock offset. Timestamps received with drift exceeding this value will be rejected.
//     /// If <c>null</c>, uses <see cref="DefaultMaxOffset"/>.
//     /// </param>
//     public HybridLogicalClock(HybridTimestamp initialTimestamp, TimeProvider? timeProvider = null, TimeSpan? maxOffset = null) {
//         _timeProvider = timeProvider ?? TimeProvider.System;
//         _maxOffsetMs  = (long)(maxOffset ?? DefaultMaxOffset).TotalMilliseconds;
//
//         var wallClock = GetWallClockTime();
//
//         _state = wallClock >= initialTimestamp.PhysicalTime
//             ? HybridTimestamp.Pack(wallClock, 0)
//             : initialTimestamp.Value;
//     }
//
//     /// <summary>
//     /// Gets the maximum tolerated clock offset.
//     /// </summary>
//     public TimeSpan MaxOffset => TimeSpan.FromMilliseconds(_maxOffsetMs);
//
//     /// <summary>
//     /// Gets the current timestamp without advancing the clock.
//     /// </summary>
//     public HybridTimestamp Current => HybridTimestamp.Parse(Volatile.Read(ref _state));
//
//     /// <summary>
//     /// Generates a new timestamp for a local event, advancing the clock.
//     /// </summary>
//     /// <returns>A new <see cref="HybridTimestamp"/> guaranteed to be greater than any previously returned timestamp.</returns>
//     /// <exception cref="InvalidOperationException">Thrown when the counter overflows due to excessive clock drift.</exception>
//     /// <remarks>
//     /// Call this method when a local event occurs or before sending a message to another node.
//     /// </remarks>
//     public HybridTimestamp Now() {
//         long current, updated;
//
//         do {
//             current = Volatile.Read(ref _state);
//
//             var currentTime    = HybridTimestamp.GetPhysicalTime(current);
//             var currentCounter = HybridTimestamp.GetCounter(current);
//             var wallClock      = GetWallClockTime();
//
//             if (wallClock > currentTime) {
//                 updated = HybridTimestamp.Pack(wallClock, 0);
//             }
//             else {
//                 var newCounter = currentCounter + 1;
//                 ValidateCounterOverflow(newCounter, currentTime);
//                 updated = HybridTimestamp.Pack(currentTime, newCounter);
//             }
//         } while (Interlocked.CompareExchange(ref _state, updated, current) != current);
//
//         return HybridTimestamp.Parse(updated);
//     }
//
//     /// <summary>
//     /// Updates the clock based on a received timestamp and generates a new local timestamp.
//     /// </summary>
//     /// <param name="received">The timestamp received from another node.</param>
//     /// <returns>A new <see cref="HybridTimestamp"/> that is greater than both the local clock and the received timestamp.</returns>
//     /// <exception cref="InvalidOperationException">Thrown when the counter overflows due to excessive clock drift.</exception>
//     /// <exception cref="ClockDriftException">
//     /// Thrown when the received timestamp exceeds the local physical time by more than <see cref="MaxOffset"/>.
//     /// </exception>
//     /// <remarks>
//     /// Call this method when receiving a message from another node to maintain causal ordering.
//     /// This advances the clock (creates a new event).
//     /// </remarks>
//     public HybridTimestamp Receive(HybridTimestamp received) {
//         // Validate drift before entering CAS loop (fail fast)
//         ValidateClockDrift(received, GetWallClockTime());
//
//         long current, updated;
//
//         do {
//             current = Volatile.Read(ref _state);
//
//             var currentTime    = HybridTimestamp.GetPhysicalTime(current);
//             var currentCounter = HybridTimestamp.GetCounter(current);
//             var wallClock      = GetWallClockTime();
//
//             // Re-validate drift (wall clock may have changed)
//             ValidateClockDrift(received, wallClock);
//
//             long newTime;
//             int  newCounter;
//
//             if (wallClock > currentTime && wallClock > received.PhysicalTime) {
//                 newTime    = wallClock;
//                 newCounter = 0;
//             }
//             else if (currentTime == received.PhysicalTime) {
//                 newTime    = currentTime;
//                 newCounter = Math.Max(currentCounter, received.Counter) + 1;
//             }
//             else if (currentTime > received.PhysicalTime) {
//                 newTime    = currentTime;
//                 newCounter = currentCounter + 1;
//             }
//             else {
//                 newTime    = received.PhysicalTime;
//                 newCounter = received.Counter + 1;
//             }
//
//             ValidateCounterOverflow(newCounter, newTime);
//             updated = HybridTimestamp.Pack(newTime, newCounter);
//         } while (Interlocked.CompareExchange(ref _state, updated, current) != current);
//
//         return HybridTimestamp.Parse(updated);
//     }
//
//     /// <summary>
//     /// Synchronizes the clock to be at least the given timestamp without advancing (no new event).
//     /// </summary>
//     /// <param name="timestamp">The timestamp to synchronize with.</param>
//     /// <exception cref="ClockDriftException">
//     /// Thrown when the timestamp exceeds the local physical time by more than <see cref="MaxOffset"/>.
//     /// </exception>
//     /// <remarks>
//     /// <para>
//     /// Use this method to catch up to a known timestamp without creating a new event.
//     /// This is typically used during startup to restore from persisted state.
//     /// </para>
//     /// <para>
//     /// Unlike <see cref="Receive"/>, this method does not increment the counter.
//     /// </para>
//     /// </remarks>
//     public void Sync(HybridTimestamp timestamp) {
//         // Validate drift before entering CAS loop (fail fast)
//         ValidateClockDrift(timestamp, GetWallClockTime());
//
//         long current, updated;
//
//         do {
//             current = Volatile.Read(ref _state);
//
//             var currentTime    = HybridTimestamp.GetPhysicalTime(current);
//             var currentCounter = HybridTimestamp.GetCounter(current);
//
//             // Re-validate drift (wall clock may have changed)
//             ValidateClockDrift(timestamp, GetWallClockTime());
//
//             if (timestamp.PhysicalTime > currentTime) {
//                 updated = timestamp.Value;
//             }
//             else if (timestamp.PhysicalTime == currentTime && timestamp.Counter > currentCounter) {
//                 updated = timestamp.Value;
//             }
//             else {
//                 // Our clock is already ahead, nothing to do
//                 return;
//             }
//         } while (Interlocked.CompareExchange(ref _state, updated, current) != current);
//     }
//
//     long GetWallClockTime() => _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
//
//     void ValidateCounterOverflow(int counter, long physicalTime) {
//         if (counter > ushort.MaxValue)
//             throw new InvalidOperationException(
//                 $"HLC counter overflow - clock drift too high. PhysicalTime: {physicalTime}, WallClock: {GetWallClockTime()}"
//             );
//     }
//
//     void ValidateClockDrift(HybridTimestamp received, long wallClock) {
//         var drift = received.PhysicalTime - wallClock;
//
//         if (drift > _maxOffsetMs)
//             throw new ClockDriftException(
//                 $"Received timestamp {received} is {drift}ms ahead of wall clock. Maximum allowed offset is {_maxOffsetMs}ms."
//             );
//     }
// }
//
// /// <summary>
// /// Exception thrown when a received timestamp exceeds the maximum tolerated clock drift.
// /// </summary>
// [PublicAPI]
// public sealed class ClockDriftException : Exception {
//     /// <summary>
//     /// Initializes a new instance of the <see cref="ClockDriftException"/> class.
//     /// </summary>
//     public ClockDriftException() { }
//
//     /// <summary>
//     /// Initializes a new instance of the <see cref="ClockDriftException"/> class with a specified error message.
//     /// </summary>
//     /// <param name="message">The message that describes the error.</param>
//     public ClockDriftException(string message) : base(message) { }
//
//     /// <summary>
//     /// Initializes a new instance of the <see cref="ClockDriftException"/> class with a specified error message
//     /// and a reference to the inner exception that is the cause of this exception.
//     /// </summary>
//     /// <param name="message">The message that describes the error.</param>
//     /// <param name="innerException">The exception that is the cause of the current exception.</param>
//     public ClockDriftException(string message, Exception innerException) : base(message, innerException) { }
// }
