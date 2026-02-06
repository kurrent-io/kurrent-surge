using Kurrent.Client.Streams;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options.Validators;

namespace Kurrent.Processors.Options;

/// <summary>
/// Configuration settings for a processor.
/// </summary>
[PublicAPI]
public record ProcessorSettings : OptionsBase<ProcessorSettings, ProcessorSettingsValidator> {
    /// <summary>
    /// Unique identifier for the processor group.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> Empty string</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>
    /// Multiple workers with the same processor ID share checkpoints,
    /// enabling coordinated processing across instances.
    /// </para>
    /// </remarks>
    public string ProcessorId { get; init; } = string.Empty;

    /// <summary>
    /// Unique identifier for this worker instance within the processor group.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> New GUID</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>
    /// Used for checkpoint attribution. For sticky partition assignment,
    /// use a consistent identifier like <c>Environment.MachineName</c>.
    /// </para>
    /// </remarks>
    public string WorkerId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Maximum number of partitions (key groups). Range: 1-32768.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 128</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>
    /// Determines the upper bound for parallel processing.
    /// </para>
    /// </remarks>
    internal int MaxParallelism { get; init; } = 128;

    /// <summary>
    /// Subscription settings (filter, partitioning, initial position).
    /// </summary>
    public SubscriptionSettings Subscription { get; init; } = new();

    /// <summary>
    /// Batching settings (size, timeout).
    /// </summary>
    public BatchingSettings Batching { get; init; } = new();

    /// <summary>
    /// Checkpoint settings (interval, retention, failure policy).
    /// </summary>
    public CheckpointSettings Checkpoint { get; init; } = new();

    /// <summary>
    /// Worker runtime settings (buffering, error handling, decoding).
    /// </summary>
    public WorkerSettings Worker { get; init; } = new();
}

/// <summary>
/// Configuration for subscription behavior.
/// </summary>
[PublicAPI]
public record SubscriptionSettings {
    /// <summary>
    /// Filter expression string for server-side filtering (e.g., <c>"$all"</c>, <c>"stream_id LIKE 'orders-%'"</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> Empty string (no filter)</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>
    /// Used for proto/config binding. When binding from configuration,
    /// a <see cref="RecordFilter"/> can be created automatically from this expression.
    /// </para>
    /// </remarks>
    public string FilterExpression { get; init; } = string.Empty;

    /// <summary>
    /// Record filtering criteria to apply.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> RecordFilter.None</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>
    /// Filters can target specific streams, schemas, metadata properties,
    /// or custom predicates to reduce data transfer and processing overhead.
    /// </para>
    /// </remarks>
    public IRecordFilter Filter { get; init; } = RecordFilter.None;

    /// <summary>
    /// Path to extract partition key. Required when <see cref="ProcessorSettings.MaxParallelism"/> > 1.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> Empty string</para>
    /// <para><b>Importance:</b> High (when using parallelism)</para>
    /// <para>
    /// Supports paths like:
    /// <list type="bullet">
    ///   <item><c>"stream_id"</c> - The stream ID</item>
    ///   <item><c>"data.customer_id"</c> - Field in record data</item>
    ///   <item><c>"metadata.tenant"</c> - Field in record metadata</item>
    /// </list>
    /// </para>
    /// </remarks>
    public string PartitionKeySelector { get; init; } = string.Empty;

    /// <summary>
    /// Where to start if no checkpoint exists.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> <see cref="SubscriptionInitialPosition.Latest"/></para>
    /// <para><b>Importance:</b> High</para>
    /// </remarks>
    public SubscriptionInitialPosition InitialPosition { get; init; } = SubscriptionInitialPosition.Latest;
}

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
public record BatchingSettings {
    /// <summary>
    /// Maximum number of records per batch.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 200 records</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para>
    /// Middle ground between low-latency (100) and high-throughput (500).
    /// </para>
    /// </remarks>
    public int MaxSize { get; init; } = 200;

    /// <summary>
    /// Maximum time to wait for a full batch (milliseconds).
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 250 milliseconds</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para>
    /// Shorter timeouts reduce latency but may result in smaller batches.
    /// Longer timeouts allow larger batches but increase delivery delay.
    /// </para>
    /// </remarks>
    public long Timeout { get; init; } = 250;

    internal TimeSpan TimeoutTimeSpan => TimeSpan.FromMilliseconds(Timeout); // 00:00:00.250
}

/// <summary>
/// Configuration for checkpoint behavior.
/// </summary>
[PublicAPI]
public record CheckpointSettings {
    public static readonly CheckpointSettings Default = new() {
        Interval = 30000,
        Timeout  = 30000
    };

    /// <summary>
    /// Time between automatic checkpoints (milliseconds).
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 30000 milliseconds (30 seconds)</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>
    /// Balances fault tolerance with I/O overhead.
    /// </para>
    /// </remarks>
    public long Interval { get; init; } = 30000;

    internal TimeSpan IntervalTimeSpan => TimeSpan.FromMilliseconds(Interval);

    /// <summary>
    /// Maximum time to wait for workers to acknowledge checkpoint (milliseconds).
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 30000 milliseconds (30 seconds)</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para>
    /// Faster detection of stuck workers. If a worker doesn't acknowledge within this time,
    /// it's considered failed and partitions can be reassigned.
    /// </para>
    /// </remarks>
    public long Timeout { get; init; } = 30000;

    internal TimeSpan TimeoutTimeSpan => TimeSpan.FromMilliseconds(Timeout);

    /// <summary>
    /// Number of completed checkpoints to retain.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 3 checkpoints</para>
    /// <para><b>Importance:</b> Low</para>
    /// </remarks>
    public int RetentionCount { get; init; } = 3;

    /// <summary>
    /// What to do when a checkpoint fails.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> <see cref="CheckpointFailurePolicy.Continue"/></para>
    /// <para><b>Importance:</b> High</para>
    /// </remarks>
    public CheckpointFailurePolicy FailurePolicy { get; init; } = CheckpointFailurePolicy.Continue;
}

/// <summary>
/// Defines what happens when a checkpoint fails.
/// </summary>
/// <remarks>
/// <para>
/// A checkpoint fails if any worker doesn't acknowledge within the timeout.
/// The timed-out worker is always disconnected regardless of this policy.
/// This setting controls what happens to the OTHER workers.
/// </para>
/// </remarks>
[PublicAPI]
public enum CheckpointFailurePolicy {
    /// <summary>
    /// Unspecified failure policy. The server will use the default behavior.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Abort this checkpoint, continue processing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <list type="bullet">
    ///   <item>The checkpoint is marked as failed</item>
    ///   <item>Other workers continue processing from last successful checkpoint</item>
    ///   <item>Failed worker's partitions are reassigned to remaining workers</item>
    ///   <item>Next checkpoint triggers normally on interval/threshold</item>
    /// </list>
    /// </para>
    /// <para>Use when: High availability is priority, some data loss acceptable.</para>
    /// </remarks>
    Continue = 1,

    /// <summary>
    /// Stop all processing on checkpoint failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <list type="bullet">
    ///   <item>All workers are disconnected</item>
    ///   <item>Processor goes to <c>Idle</c> state</item>
    ///   <item>Requires manual intervention (reconnect workers to resume)</item>
    /// </list>
    /// </para>
    /// <para>Use when: Data consistency is critical, prefer downtime over data loss.</para>
    /// </remarks>
    FailProcessor = 2
}

/// <summary>
/// Configuration for worker runtime behavior.
/// </summary>
/// <remarks>
/// <para>
/// These settings control how the worker instance operates locally,
/// independent of the processor definition. They are not sent to the server.
/// </para>
/// </remarks>
[PublicAPI]
public record WorkerSettings {
    /// <summary>
    /// Whether to throw an exception on processing failure or log and stop.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> false (log and stop)</para>
    /// <para><b>Importance:</b> Medium</para>
    /// </remarks>
    public bool ThrowOnFailure { get; init; }

    /// <summary>
    /// Number of records to buffer in memory.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 1000 records</para>
    /// <para><b>Importance:</b> Medium</para>
    /// <para><b>Valid Range:</b> 1 to 10,000 records</para>
    /// <para>
    /// Larger buffers improve throughput but increase memory usage.
    /// The buffer capacity should be at least as large as the batch size.
    /// </para>
    /// </remarks>
    public int BufferCapacity { get; init; } = 1000;

    /// <summary>
    /// Skip record data deserialization for performance-critical scenarios.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> false (decode records)</para>
    /// <para><b>Importance:</b> Low</para>
    /// <para>
    /// When true, records are returned as raw bytes without deserialization,
    /// reducing CPU usage for metadata-only operations.
    /// </para>
    /// </remarks>
    public bool SkipDecoding { get; init; }
}
