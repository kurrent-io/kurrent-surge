// namespace Kurrent.Client.Streams;
//
// // /// <summary>
// // /// Represents the record position either in a specific stream or in the global transaction log.
// // /// </summary>
// // /// <remarks>
// // /// <para>
// // /// This discriminated union allows specifying a position as either a LogPosition (global transaction log)
// // /// or a StreamRevision (stream-specific position). The type ensures type-safe handling of different
// // /// position contexts without runtime type checking.
// // /// </para>
// // /// <para>
// // /// <b>Usage:</b> Use LogPosition for reading from the global event log across all streams,
// // /// or StreamRevision for reading from a specific stream starting at a particular revision.
// // /// </para>
// // /// </remarks>
// // public readonly partial record struct RecordPosition : IVariant<LogPosition, StreamRevision>;
//
// /// <summary>
// /// Configuration options for reading events from KurrentDB streams or the global event log.
// /// </summary>
// /// <remarks>
// /// <para>
// /// Provides comprehensive configuration for event stream reading operations, supporting both
// /// stream-specific reads and global log reads with flexible positioning, filtering, and
// /// performance tuning options.
// /// </para>
// /// <para>
// /// <b>Usage:</b> Create instances using object initialization syntax to specify only the
// /// options that differ from defaults. All properties use sensible defaults for common scenarios.
// /// </para>
// /// </remarks>
// [PublicAPI]
// public record ReadOptions {
//     /// <summary>Target stream name for stream-specific read operations.</summary>
//     /// <remarks>
//     /// <para><b>Default:</b> StreamName.None (no specific stream)</para>
//     /// <para><b>Importance:</b> High</para>
//     /// <para>When set to StreamName.None, the read operation targets the global event log across all streams.</para>
//     /// </remarks>
//     public StreamName Stream { get; init; } = StreamName.AllStream;
//
//     /// <summary>Event filtering criteria to apply during read operations.</summary>
//     /// <remarks>
//     /// <para><b>Default:</b> RecordFilter.None (no filtering)</para>
//     /// <para><b>Importance:</b> Medium</para>
//     /// <para>Filters can target specific event types, metadata properties, or custom predicates to reduce data transfer and processing overhead.</para>
//     /// </remarks>
//     public IRecordFilter Filter { get; init; } = RecordFilter.None;
//
//     /// <summary>Starting position for the read operation in either stream or global log context.</summary>
//     /// <remarks>
//     /// <para><b>Default:</b> LogPosition.Latest</para>
//     /// <para><b>Importance:</b> High</para>
//     /// <para>Can be either a LogPosition for global log reads or a StreamRevision for stream-specific reads. The type ensures position context matches the operation.</para>
//     /// </remarks>
//     public RecordPosition Start { get; init; } = LogPosition.Latest;
//
//     /// <summary>Heartbeat configuration for long-running read operations.</summary>
//     /// <remarks>
//     /// <para><b>Default:</b> HeartbeatOptions.Default</para>
//     /// <para><b>Importance:</b> Medium</para>
//     /// <para>Heartbeats maintain connection liveness during extended reads and provide progress feedback for monitoring systems.</para>
//     /// </remarks>
//     public HeartbeatOptions Heartbeat { get; init; } = HeartbeatOptions.Default;
//
//     /// <summary>Maximum time to wait for read operations to complete.</summary>
//     /// <remarks>
//     /// <para><b>Default:</b> 30 seconds</para>
//     /// <para><b>Importance:</b> Medium</para>
//     /// <para><b>Valid Range:</b> Minimum 1 second</para>
//     /// <para>Prevents indefinite blocking on slow or unresponsive server operations.</para>
//     /// </remarks>
//     public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
//
//     /// <summary>Number of events to buffer in memory during streaming read operations.</summary>
//     /// <remarks>
//     /// <para><b>Default:</b> 1000 events</para>
//     /// <para><b>Importance:</b> Medium</para>
//     /// <para><b>Valid Range:</b> 1 to 10,000 events</para>
//     /// <para>Larger buffers improve throughput but increase memory usage. Tune based on event size and available memory.</para>
//     /// </remarks>
//     public int BufferSize { get; init; } = 1000;
//
//     /// <summary>Direction for reading events through the stream or log.</summary>
//     /// <remarks>
//     /// <para><b>Default:</b> ReadDirection.Forwards</para>
//     /// <para><b>Importance:</b> High</para>
//     /// <para>Forwards reads from the specified start position toward newer events. Backwards reads toward older events.</para>
//     /// </remarks>
//     public ReadDirection Direction { get; init; } = ReadDirection.Forwards;
//
//     /// <summary>Maximum number of events to read in this operation.</summary>
//     /// <remarks>
//     /// <para><b>Default:</b> long.MaxValue (unlimited)</para>
//     /// <para><b>Importance:</b> Medium</para>
//     /// <para><b>Valid Range:</b> Minimum 1 event</para>
//     /// <para>Use to prevent unbounded reads and control resource usage. Essential for batch processing scenarios.</para>
//     /// </remarks>
//     public long Limit { get; init; } = long.MaxValue;
//
//     /// <summary>Skip event data deserialization for performance-critical scenarios.</summary>
//     /// <remarks>
//     /// <para><b>Default:</b> false (decode events)</para>
//     /// <para><b>Importance:</b> Low</para>
//     /// <para>When true, events are returned as raw bytes without deserialization, reducing CPU usage for metadata-only operations or custom deserialization.</para>
//     /// </remarks>
//     public bool SkipDecoding { get; init; }
//
//     /// <summary>Cancellation token for cooperative cancellation of read operations.</summary>
//     /// <remarks>
//     /// <para><b>Default:</b> CancellationToken.None</para>
//     /// <para><b>Importance:</b> Medium</para>
//     /// <para>Enables graceful shutdown and timeout handling in distributed systems and user-interactive scenarios.</para>
//     /// </remarks>
//     public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
// }
//
// [PublicAPI]
// public record ReadAllOptions : ReadOptionsBase {
//     public LogPosition Start { get; init; } = LogPosition.Latest;
//
//     public override void EnsureValid() {
//         base.EnsureValid();
//         ArgumentOutOfRangeException.ThrowIfLessThan(Start, LogPosition.Earliest, nameof(Start));
//     }
//
//     internal static ReadAllOptions FirstRecord(CancellationToken ct = default) =>
//         new() {
//             Start             = LogPosition.Earliest,
//             Direction         = ReadDirection.Forwards,
//             Limit             = 1,
//             Heartbeat         = HeartbeatOptions.Disabled,
//             CancellationToken = ct
//         };
//
//     internal static ReadAllOptions LastRecord(CancellationToken ct = default) =>
//         new() {
//             Start             = LogPosition.Latest,
//             Direction         = ReadDirection.Backwards,
//             Limit             = 1,
//             Heartbeat         = HeartbeatOptions.Disabled,
//             CancellationToken = ct
//         };
// }
//
// [PublicAPI]
// public record ReadStreamOptions : ReadOptionsBase {
//     public StreamName Stream { get; init; } = StreamName.None;
//
//     public StreamRevision Start { get; init; } = StreamRevision.Min;
//
//     public override void EnsureValid() {
//         base.EnsureValid();
//
//         ArgumentException.ThrowIfNullOrEmpty(Stream, nameof(Stream));
//         ArgumentOutOfRangeException.ThrowIfLessThan(Start, StreamRevision.Min, nameof(Start));
//
//         if (Direction == ReadDirection.Forwards && Start == StreamRevision.Max)
//             throw new ArgumentException(
//                 "Start revision cannot be Max when reading forwards. Use ReadDirection.Backwards or specify a valid revision.",
//                 nameof(Start)
//             );
//     }
// }
//
// [PublicAPI]
// public abstract record ReadOptionsBase {
//     public bool SkipDecoding { get; init; }
//
//     public IRecordFilter Filter { get; init; } = RecordFilter.None;
//
//     public HeartbeatOptions Heartbeat { get; init; } = HeartbeatOptions.Default;
//
//     public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
//
//     public int BufferSize { get; init; } = 1000;
//
//     public ReadDirection Direction { get; init; } = ReadDirection.Forwards;
//
//     public long Limit { get; init; } = long.MaxValue;
//
//     public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
//
//     public virtual void EnsureValid() {
//         if (Heartbeat.Enable) {
//             ArgumentOutOfRangeException.ThrowIfLessThan(Heartbeat.RecordsThreshold, 1);
//             ArgumentOutOfRangeException.ThrowIfGreaterThan(Heartbeat.RecordsThreshold, 10000);
//         }
//
//         ArgumentOutOfRangeException.ThrowIfLessThan(Timeout, TimeSpan.FromSeconds(1));
//
//         ArgumentOutOfRangeException.ThrowIfLessThan(BufferSize, 1);
//         ArgumentOutOfRangeException.ThrowIfGreaterThan(BufferSize, 10000);
//
//         ArgumentOutOfRangeException.ThrowIfLessThan(Limit, 1);
//     }
// }
