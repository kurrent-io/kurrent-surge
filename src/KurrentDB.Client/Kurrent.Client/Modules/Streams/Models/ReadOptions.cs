namespace Kurrent.Client.Streams;

public abstract record ReadOptionsBase {
    /// <summary>Maximum number of records to read in this operation.</summary>
    /// <remarks>
    /// <para><b>Default:</b> long.MaxValue (unlimited)</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para><b>Valid Range:</b> Minimum 1 record</para>
    /// <para>Use to implement "read first N records" scenarios and control resource usage.</para>
    /// </remarks>
    public long MaxRecords { get; init; } = long.MaxValue;

    /// <summary>Number of records to buffer in memory during streaming read operations.</summary>
    /// <remarks>
    /// <para><b>Default:</b> 1000 records</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para><b>Valid Range:</b> 1 to 10,000 records</para>
    /// <para>Controls backpressure. Larger buffers improve throughput but increase memory usage.</para>
    /// </remarks>
    public int BufferCapacity { get; init; } = 1000;

    /// <summary>Record filtering criteria to apply during read operations.</summary>
    /// <remarks>
    /// <para><b>Default:</b> RecordFilter.None (no filtering)</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para>Filters can target specific schemas, metadata properties, or custom predicates to reduce data transfer and processing overhead.</para>
    /// </remarks>
    public IRecordFilter Filter { get; init; } = RecordFilter.None;

    /// <summary>Heartbeat configuration for long-running read operations.</summary>
    /// <remarks>
    /// <para><b>Default:</b> HeartbeatOptions.Disabled</para>
    /// <para><b>Importance:</b> Medium</para>
    /// </remarks>
    public HeartbeatOptions Heartbeat { get; init; } = HeartbeatOptions.Disabled;

    /// <summary>Direction for reading records through the stream or log.</summary>
    /// <remarks>
    /// <para><b>Default:</b> ReadDirection.Forwards</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>Forwards reads from the specified start position toward newer records. Backwards reads toward older records.</para>
    /// </remarks>
    public ReadDirection Direction { get; init; } = ReadDirection.Forwards;

    /// <summary>Skip record data deserialization for performance-critical scenarios.</summary>
    /// <remarks>
    /// <para><b>Default:</b> false (decode records)</para>
    /// <para><b>Importance:</b> Low</para>
    /// <para>When true, records are returned as raw bytes without deserialization, reducing CPU usage for metadata-only operations or custom deserialization.</para>
    /// </remarks>
    public bool SkipDecoding { get; init; }

    /// <summary>Cancellation token for cooperative cancellation of read operations.</summary>
    /// <remarks>
    /// <para><b>Default:</b> CancellationToken.None</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para>Enables graceful shutdown and timeout handling in distributed systems and user-interactive scenarios.</para>
    /// </remarks>
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;

    /// <summary>
    /// Validates the current options and throws exceptions for any invalid configurations.
    /// </summary>
    public virtual void EnsureValid() {
        if (Heartbeat.Enable) {
            ArgumentOutOfRangeException.ThrowIfLessThan(Heartbeat.RecordsThreshold, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(Heartbeat.RecordsThreshold, 10000);
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(BufferCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(BufferCapacity, 10000);

        ArgumentOutOfRangeException.ThrowIfLessThan(MaxRecords, 1);
    }
}

[PublicAPI]
public record ReadOptions : ReadOptionsBase {
    /// <summary>
    /// Starting position for the read operation in either stream or global log context.
    /// </summary>
    public LogPosition Start { get; init; } = LogPosition.Latest;
}

[PublicAPI]
public record ReadStreamOptions : ReadOptionsBase {
    /// <summary>
    /// Name of the stream to read from.
    /// </summary>
    public StreamName Stream { get; init; } = StreamName.None;

    /// <summary>
    /// Starting revision within the specified stream to begin reading from.
    /// </summary>
    public StreamRevision Start { get; init; } = StreamRevision.Min; // StreamRevision.Unset;

    public override void EnsureValid() {
        base.EnsureValid();

        // if (Stream.IsAllStream)
        //     throw new ArgumentException("Stream name cannot be '$all' when reading from a specific stream.", nameof(Stream));
        //
        // if (Stream == StreamName.None)
        //     throw new ArgumentException("Stream name must be specified when reading from a specific stream.", nameof(Stream));

        // if (Start == StreamRevision.Unset)
        //     throw new ArgumentException("Start revision cannot be Unset when reading from a specific stream. Specify a valid revision.", nameof(Start));

        // if (Start < StreamRevision.Min)
        //     throw new ArgumentOutOfRangeException(nameof(Start), "Start revision cannot be less than Min.");

        // if (Start > StreamRevision.Max)
        //     throw new ArgumentOutOfRangeException(nameof(Start), "Start revision cannot be greater than Max.");

        // if (Direction == ReadDirection.Forwards && Start == StreamRevision.Max)
        //     throw new ArgumentException("Start revision cannot be Max when reading forwards.", nameof(Start));

        if (Filter.TargetStream != StreamName.None && Filter.TargetStream != Stream)
            throw new ArgumentException("Filter target stream does not match the specified stream to read from.", nameof(Filter));
    }
}

// /// <summary>
// /// Represents a position within a specific stream, defined by the stream name and revision.
// /// </summary>
// /// <param name="Stream">
// /// Name of the stream.
// /// </param>
// /// <param name="Revision">
// /// Revision within the stream.
// /// </param>
// public record StreamPosition(StreamName Stream, StreamRevision Revision) {
//     public static readonly StreamPosition Unset = new(StreamName.None, StreamRevision.Unset);
//
//     public override string ToString() => $"{Stream} v{Revision}";
//
//     public static StreamPosition From(StreamName stream, StreamRevision revision) {
//         if (stream == StreamName.None)
//             throw new ArgumentException("Stream name cannot be None.", nameof(stream));
//
//         if (revision == StreamRevision.Unset)
//             throw new ArgumentException("Start revision cannot be Unset.", nameof(revision));
//
//         return new StreamPosition(stream, revision);
//     }
//
//     public static StreamPosition Earliest(StreamName stream) =>
//         From(stream, StreamRevision.Min);
//
//     public static StreamPosition Latest(StreamName stream) =>
//         From(stream, StreamRevision.Max);
// }
