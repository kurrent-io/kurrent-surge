using Kurrent.Validation;

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
}

[PublicAPI]
public record ReadOptions : ReadOptionsBase, IOptions {
    /// <summary>
    /// Starting position for the read operation in either stream or global log context.
    /// </summary>
    public LogPosition Start { get; init; } = LogPosition.Latest;

    /// <inheritdoc />
    public ValidationResult ValidateOptions() =>
        KurrentClientReadOptionsValidator.Instance.ValidateOptions(this);
}

[PublicAPI]
public record ReadStreamOptions : ReadOptionsBase, IOptions {
    /// <summary>
    /// Name of the stream to read from.
    /// </summary>
    public StreamName Stream { get; init; } = StreamName.None;

    /// <summary>
    /// Starting revision within the specified stream to begin reading from.
    /// </summary>
    public StreamRevision Start { get; init; } = StreamRevision.Min;

    /// <inheritdoc />
    public ValidationResult ValidateOptions() =>
        KurrentClientReadStreamOptionsValidator.Instance.ValidateOptions(this);
}
