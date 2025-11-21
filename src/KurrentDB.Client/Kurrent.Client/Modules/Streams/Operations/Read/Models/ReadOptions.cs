namespace Kurrent.Client.Streams;

public abstract record ReadOptionsBase {
    /// <summary>Record filtering criteria to apply during read operations.</summary>
    /// <remarks>
    /// <para><b>Default:</b> RecordFilter.None (no filtering)</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para>Filters can target specific schemas, metadata properties, or custom predicates to reduce data transfer and processing overhead.</para>
    /// </remarks>
    public IRecordFilter Filter { get; init; } = RecordFilter.None;

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

    /// <summary>
    /// Validates the current options and throws exceptions for any invalid configurations.
    /// </summary>
    public virtual void EnsureValid() {
        if (Heartbeat.Enable) {
            ArgumentOutOfRangeException.ThrowIfLessThan(Heartbeat.RecordsThreshold, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(Heartbeat.RecordsThreshold, 10000);
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(Timeout, TimeSpan.FromSeconds(1));

        ArgumentOutOfRangeException.ThrowIfLessThan(BufferSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(BufferSize, 10000);

        ArgumentOutOfRangeException.ThrowIfLessThan(Limit, 1);
    }
}

[PublicAPI]
public record ReadOptions : ReadOptionsBase {
    /// <summary>
    /// Starting position for the read operation in either stream or global log context.
    /// </summary>
    public LogPosition Start { get; init; } = LogPosition.Latest;

    public override void EnsureValid() {
        base.EnsureValid();

        if (Start < LogPosition.Earliest)
            throw new ArgumentOutOfRangeException(nameof(Start), "Start position cannot be less than Earliest.");
    }
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
    public StreamRevision Start { get; init; } = StreamRevision.Min;

    public override void EnsureValid() {
        base.EnsureValid();

        if (Stream.IsAllStream)
            throw new ArgumentException("Stream name cannot be '$all' when reading from a specific stream. Use ReadOptions for reading from the global log.", nameof(Stream));

        if ( Stream == StreamName.None)
            throw new ArgumentException("Stream name must be specified when reading from a specific stream.", nameof(Stream));

        if (Start == StreamRevision.Unset)
            throw new ArgumentException("Start revision cannot be Unset when reading from a specific stream. Specify a valid revision.", nameof(Start));

        if (Start < StreamRevision.Min)
            throw new ArgumentOutOfRangeException(nameof(Start), "Start revision cannot be less than Min.");

        if (Start > StreamRevision.Max)
            throw new ArgumentOutOfRangeException(nameof(Start), "Start revision cannot be greater than Max.");

        if (Direction == ReadDirection.Forwards && Start == StreamRevision.Max)
            throw new ArgumentException("Start revision cannot be Max when reading forwards. Use ReadDirection.Backwards or specify a valid revision.", nameof(Start));
    }
}
