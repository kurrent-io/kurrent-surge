using Kurrent.Variant;

namespace Kurrent.Client.Streams;

// public readonly record struct MyRecordFilter {
//     public static readonly MyRecordFilter None = new() {
//         Expression = "",
//         Stream     = StreamName.None
//     };
//
//     public string     Expression { get; init; }
//     public StreamName Stream     { get; init; }
//
//     public bool IsStreamSpecific =>
//         Stream != StreamName.None && Stream != StreamName.AllStream;
//
//
//     public bool IsMatch(Record record) {
//
//         return false;
//     }
// }
//
//
// public enum CatchUpFilterScope {
//     Unspecified = 0,
//
//     /// <summary>
//     /// The filter will be applied to all the record properties
//     /// and to its data payload (if json and the filter type is compatible)
//     /// This is the most expensive filter type
//     /// </summary>
//     Record = 1,
//
//     /// <summary>
//     /// The filter will be applied to the properties of the record
//     /// These might also include the stream and schema name
//     /// </summary>
//     Properties = 2,
//
//     /// <summary>
//     /// The filter will be applied to the data payload of the record.
//     /// At the moment this is limited to JSON formatted data.
//     /// Compatible filter types include: Literal, Regex, JsonPath, and Sql.
//     /// </summary>
//     Data = 4,
//
//
// }
//
// public enum CatchUpFilterType {
//     Unspecified = 0,
//     Literal     = 1,
//     Regex       = 2,
//     Sql         = 3, // valid for full record and data filters if data is JSON
//     JsonPath    = 4, // valid for data filters if data is JSON
//     StreamId    = 5  // valid for stream filters only
// }
//
//
//
// public enum ConsumeFilterScope {
//     Unspecified = 0,
//     Stream      = 1,
//     Record      = 2
// }
//
// public enum ConsumeFilterType {
//     Unspecified = 0,
//     Regex       = 1,
//     Prefix      = 2,
//     JsonPath    = 3,
//     StreamId    = 4
// }

[PublicAPI]
public record CatchUpOptions {
    /// <summary>Target stream name for stream-specific read operations.</summary>
    /// <remarks>
    /// <para><b>Default:</b> StreamName.None (no specific stream)</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>When set to StreamName.None, the read operation targets the global event log across all streams.</para>
    /// </remarks>
    public StreamName Stream { get; init; } = StreamName.AllStream; // could we do this using the sql filter and something like `from '$all' where ...` or `from 'order-003' where ...?

    /// <summary>Filtering criteria to apply.</summary>
    /// <remarks>
    /// <para><b>Default:</b> RecordFilter.None (no filtering)</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>Filters can target specific schema types, metadata properties, or custom predicates to reduce data transfer and processing overhead.</para>
    /// </remarks>
    public RecordFilter Filter { get; init; } = RecordFilter.None;

    /// <summary>Starting position for the read operation in either stream or global log context.</summary>
    /// <remarks>
    /// <para><b>Default:</b> LogPosition.Latest</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>Can be either a LogPosition for global log reads or a StreamRevision for stream-specific reads. The type ensures position context matches the operation.</para>
    /// </remarks>
    public RecordPosition Start { get; init; } = LogPosition.Latest;

    /// <summary>Heartbeat configuration for long-running read operations.</summary>
    /// <remarks>
    /// <para><b>Default:</b> HeartbeatOptions.Default</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para>Heartbeats maintain connection liveness during extended reads and provide progress feedback for monitoring systems.</para>
    /// </remarks>
    public HeartbeatOptions Heartbeat { get; init; } = HeartbeatOptions.Default;

    /// <summary>Maximum time to wait for read operations to complete.</summary>
    /// <remarks>
    /// <para><b>Default:</b> 30 seconds</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para><b>Valid Range:</b> Minimum 1 second</para>
    /// <para>Prevents indefinite blocking on slow or unresponsive server operations.</para>
    /// </remarks>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Number of events to buffer in memory during streaming read operations.</summary>
    /// <remarks>
    /// <para><b>Default:</b> 1000 events</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para><b>Valid Range:</b> 1 to 10,000 events</para>
    /// <para>Larger buffers improve throughput but increase memory usage. Tune based on event size and available memory.</para>
    /// </remarks>
    public int BufferSize { get; init; } = 1000;

    /// <summary>Direction for reading events through the stream or log.</summary>
    /// <remarks>
    /// <para><b>Default:</b> ReadDirection.Forwards</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>Forwards reads from the specified start position toward newer events. Backwards reads toward older events.</para>
    /// </remarks>
    public ReadDirection Direction { get; init; } = ReadDirection.Forwards;

    /// <summary>Maximum number of events to read in this operation.</summary>
    /// <remarks>
    /// <para><b>Default:</b> long.MaxValue (unlimited)</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para><b>Valid Range:</b> Minimum 1 event</para>
    /// <para>Use to prevent unbounded reads and control resource usage. Essential for batch processing scenarios.</para>
    /// </remarks>
    public long Limit { get; init; } = long.MaxValue;

    /// <summary>Skip event data deserialization for performance-critical scenarios.</summary>
    /// <remarks>
    /// <para><b>Default:</b> false (decode events)</para>
    /// <para><b>Importance:</b> Low</para>
    /// <para>When true, events are returned as raw bytes without deserialization, reducing CPU usage for metadata-only operations or custom deserialization.</para>
    /// </remarks>
    public bool SkipDecoding { get; init; }

    /// <summary>Cancellation token for cooperative cancellation of read operations.</summary>
    /// <remarks>
    /// <para><b>Default:</b> CancellationToken.None</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para>Enables graceful shutdown and timeout handling in distributed systems and user-interactive scenarios.</para>
    /// </remarks>
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
}

// [PublicAPI]
// public record ReadAllOptions : ReadOptionsBase {
//     public LogPosition Start { get; init; } = LogPosition.Latest;
//
//     public override void EnsureValid() {
//         base.EnsureValid();
//         ArgumentOutOfRangeException.ThrowIfLessThan(Start, LogPosition.Earliest);
//     }
//
//     internal static ReadAllOptions FirstRecord(CancellationToken cancellationToken = default) =>
//         new() {
//             Start             = LogPosition.Earliest,
//             Direction         = ReadDirection.Forwards,
//             Limit             = 1,
//             Heartbeat         = HeartbeatOptions.Disabled,
//             CancellationToken = cancellationToken
//         };
//
//     internal static ReadAllOptions LastRecord(CancellationToken cancellationToken = default) =>
//         new() {
//             Start             = LogPosition.Latest,
//             Direction         = ReadDirection.Backwards,
//             Limit             = 1,
//             Heartbeat         = HeartbeatOptions.Disabled,
//             CancellationToken = cancellationToken
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
//         ArgumentException.ThrowIfNullOrEmpty(Stream);
//         ArgumentOutOfRangeException.ThrowIfLessThan(Start, StreamRevision.Min);
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
//     public ReadFilter Filter { get; init; } = ReadFilter.None;
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
