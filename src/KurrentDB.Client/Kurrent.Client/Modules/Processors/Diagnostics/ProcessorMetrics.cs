// ReSharper disable CheckNamespace

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;

namespace Kurrent.Client.Processors.Diagnostics;

/// <summary>
/// Source-generated metrics for processor workers.
/// </summary>
internal static partial class ProcessorMetrics {
    /// <summary>
    /// Creates a counter for records processed.
    /// </summary>
    [Counter<long>("processor_id", "worker_id", Name = "kurrent_processor_records_processed")]
    public static partial RecordsProcessedCounter CreateRecordsProcessed(Meter meter);

    /// <summary>
    /// Creates a histogram for batch processing duration (milliseconds).
    /// </summary>
    [Histogram<double>("processor_id", Name = "kurrent_processor_batch_duration_ms")]
    public static partial BatchDurationHistogram CreateBatchDuration(Meter meter);

    /// <summary>
    /// Creates a counter for checkpoints completed.
    /// </summary>
    [Counter<long>("processor_id", "worker_id", Name = "kurrent_processor_checkpoints_completed")]
    public static partial CheckpointsCompletedCounter CreateCheckpointsCompleted(Meter meter);

    /// <summary>
    /// Creates a histogram for checkpoint duration (milliseconds).
    /// </summary>
    [Histogram<double>("processor_id", Name = "kurrent_processor_checkpoint_duration_ms")]
    public static partial CheckpointDurationHistogram CreateCheckpointDuration(Meter meter);

    /// <summary>
    /// Creates a counter for workers connected.
    /// </summary>
    [Counter<long>("processor_id", Name = "kurrent_processor_workers_connected")]
    public static partial WorkersConnectedCounter CreateWorkersConnected(Meter meter);

    /// <summary>
    /// Creates a counter for workers disconnected.
    /// </summary>
    [Counter<long>("processor_id", "failed", Name = "kurrent_processor_workers_disconnected")]
    public static partial WorkersDisconnectedCounter CreateWorkersDisconnected(Meter meter);
}
