namespace Kurrent.Client.Streams;

/// <summary>
/// Configuration for record batching during processing.
/// </summary>
/// <remarks>
/// <para>
/// Batching controls how records are grouped before delivery to the processor.
/// Smaller batches reduce latency but increase overhead; larger batches improve
/// throughput at the cost of increased memory usage and processing delay.
/// </para>
/// </remarks>
[PublicAPI]
public readonly record struct BatchingOptions {
    /// <summary>
    /// Maximum number of records to include in each batch.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 100 records</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para><b>Valid Range:</b> Minimum 1 record</para>
    /// </remarks>
    public int Size { get; init; } = 100;

    /// <summary>
    /// Maximum time in milliseconds to wait while gathering a batch before delivering what is available.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 500 milliseconds</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para><b>Valid Range:</b> 100 to 5000 milliseconds</para>
    /// <para>
    /// Shorter timeouts reduce latency but may result in smaller batches.
    /// Longer timeouts allow larger batches but increase delivery delay.
    /// </para>
    /// </remarks>
    public long Timeout { get; init; } = 500;

    /// <summary>
    /// Initializes a new instance of <see cref="BatchingOptions"/> with default values.
    /// </summary>
    public BatchingOptions() { }
}

public record ProcessOptions {
    /// <summary>
    /// Initial position from which to start processing records, in the absence of a stored checkpoint or a start position.
    /// <remarks>
    /// <para><b>Default:</b> SubscriptionInitialPosition.Latest</para>
    /// <para><b>Importance:</b> High</para>
    /// </remarks>
    /// </summary>
    public InitialPosition InitialPosition { get; init; } = InitialPosition.Latest;

    /// <summary>
    /// Starting position from which to start processing records, either in the global log or within a specific stream if it is a stream revision.
    /// <remarks>
    /// <para><b>Default:</b> <see cref="StartPosition.Unset"/></para>
    /// <para><b>Importance:</b> High</para>
    /// <para>
    /// Any existing checkpoint will be ignored and processing will begin from the specified position, overriding the checkpoint.
    /// </para>
    /// </remarks>
    /// </summary>
    public StartPosition StartPosition { get; init; } = StartPosition.Unset;

    /// <summary>
    /// Batching configuration for record processing.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> Size = 100 records, Timeout = 500 milliseconds</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para>
    /// Controls how records are grouped before delivery to the processor.
    /// Adjust batch size and timeout based on throughput and latency requirements.
    /// </para>
    /// </remarks>
    public BatchingOptions Batching { get; init; } = new();

    /// <summary>
    /// Record filtering criteria to apply.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> RecordFilter.None</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para>Filters can target specific schemas, metadata properties, or custom predicates to reduce data transfer and processing overhead.</para>
    /// </remarks>
    public IRecordFilter Filter { get; init; } = RecordFilter.None;

    /// <summary>
    /// Configures when and if heartbeats are sent during processing in order to provide progress feedback.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> HeartbeatOptions.Disabled</para>
    /// <para><b>Importance:</b> Low</para>
    /// </remarks>
    public HeartbeatOptions Heartbeat { get; init; } = HeartbeatOptions.Disabled;

    /// <summary>Number of records to buffer in memory.</summary>
    /// <remarks>
    /// <para><b>Default:</b> 1000 records</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para><b>Valid Range:</b> At least as large as <see cref="BatchSize"/> up to 10,000 records</para>
    /// <para>
    /// Larger buffers improve throughput but increase memory usage. Tune based on record size and available memory.
    /// </para>
    /// <para>
    /// The buffer capacity must be at least as large as the BatchSize to ensure efficient processing, and it is advised to set it significantly larger to accommodate incoming records while processing batches.
    /// </para>
    /// <para>Be aware that buffered records can be discarded when canceling a read operation</para>
    /// </remarks>
    public int BufferCapacity { get; init; } = 1000;

    /// <summary>Skip record data deserialization for performance-critical scenarios.</summary>
    /// <remarks>
    /// <para><b>Default:</b> false (decode records)</para>
    /// <para><b>Importance:</b> Low</para>
    /// <para>
    /// When true, records are returned as raw bytes without deserialization, reducing CPU usage for metadata-only operations or custom deserialization.
    /// </para>
    /// <para>
    /// This is particularly useful in high-throughput scenarios where the overhead of decoding can become a bottleneck, or when the application only needs to inspect record metadata without accessing the full data payload.
    /// </para>
    /// </remarks>
    public bool SkipDecoding { get; init; }

    /// <summary>
    /// Validates the current options and throws exceptions for any invalid configurations.
    /// </summary>
    public void EnsureValid() {
        if (Heartbeat.Enable) {
            ArgumentOutOfRangeException.ThrowIfLessThan(Heartbeat.RecordsThreshold, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(Heartbeat.RecordsThreshold, 10000);
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(BufferCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(BufferCapacity, 10000);
    }
}


// public record StreamProcessOptions : ProcessOptions {
//     /// <summary>
//     /// Name of the stream to read from.
//     /// </summary>
//     public StreamName Stream {
//         get; init => Filter = RecordFilter.ByStream(field = value);
//     } = StreamName.None;
//
//     // /// <summary>
//     // /// Starting revision within the specified stream to begin reading from.
//     // /// </summary>
//     // public StreamRevision Start { get; init; } = StreamRevision.Unset;
//
// /// <summary>Record filtering criteria to apply.</summary>
// /// <remarks>
// /// <para><b>Default:</b> RecordFilter.None</para>
// /// <para><b>Importance:</b> Medium</para>
// /// <para>Filters can target specific schemas, metadata properties, or custom predicates to reduce data transfer and processing overhead.</para>
// /// </remarks>
// public IRecordFilter Filter {
//     get;
//     init {
//         if (value.TargetStream != StreamName.None && value.TargetStream != StreamName.All && value.TargetStream != Stream)
//             Stream = value.TargetStream;
//         field = value;
//     }
// } = RecordFilter.None;
//
// /// <summary>
// /// Name of the stream to read from (shortcut for setting filter target stream).
// /// <remarks>
// /// <para><b>Default:</b> StreamName.None</para>
// /// <para><b>Importance:</b> Medium</para>
// /// <para>
// /// Setting this property updates the <see cref="Filter"/> to target the specified stream.
// /// And when the <see cref="Filter"/> is set with a different target stream, this property updates accordingly.
// /// </para>
// /// </remarks>
// /// </summary>
// public StreamName Stream {
//     get; init => Filter = RecordFilter.ByStream(field = value);
// } = StreamName.None;
// }
