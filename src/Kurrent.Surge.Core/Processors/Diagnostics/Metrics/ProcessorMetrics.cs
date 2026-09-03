using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;

namespace Kurrent.Surge.Processors.Diagnostics.Metrics;

/// <summary>
/// Source-generated metrics for drones.
/// </summary>
static partial class ProcessorMetrics {
    /// <summary>
    /// Creates a counter for records processed.
    /// </summary>
    [Counter<long>("processor_id", "drone_id", Name = "kurrent_processor_records_processed")]
    public static partial RecordsProcessedCounter CreateRecordsProcessed(Meter meter);

    /// <summary>
    /// Creates a histogram for record processing duration, in seconds. On the batch path this carries the
    /// per-record average (batch duration / committed count); on the per-record path, the true per-record time.
    /// </summary>
    [Histogram<double>("processor_id", Name = "kurrent_processor_record_duration_seconds", Unit = "s")]
    public static partial RecordDurationHistogram CreateRecordDuration(Meter meter);

    /// <summary>
    /// Creates a histogram for batch processing duration, in seconds — the real <c>ProcessBatch</c> time.
    /// </summary>
    [Histogram<double>("processor_id", Name = "kurrent_processor_batch_duration_seconds", Unit = "s")]
    public static partial BatchDurationHistogram CreateBatchDuration(Meter meter);

    /// <summary>
    /// Creates a histogram for effective batch size — the committed-prefix count distribution.
    /// </summary>
    [Histogram<int>("processor_id", Name = "kurrent_processor_batch_size")]
    public static partial BatchSizeHistogram CreateBatchSize(Meter meter);

    /// <summary>
    /// Creates a counter for checkpoints completed, tagged by trigger.
    /// </summary>
    [Counter<long>("processor_id", "drone_id", "trigger", Name = "kurrent_processor_checkpoints_completed")]
    public static partial CheckpointsCompletedCounter CreateCheckpointsCompleted(Meter meter);

    /// <summary>
    /// Creates a counter for checkpoints that failed, tagged by trigger.
    /// </summary>
    [Counter<long>("processor_id", "drone_id", "trigger", Name = "kurrent_processor_checkpoints_failed")]
    public static partial CheckpointsFailedCounter CreateCheckpointsFailed(Meter meter);

    /// <summary>
    /// Creates a histogram for checkpoint duration, in seconds.
    /// </summary>
    [Histogram<double>("processor_id", Name = "kurrent_processor_checkpoint_duration_seconds", Unit = "s")]
    public static partial CheckpointDurationHistogram CreateCheckpointDuration(Meter meter);

    /// <summary>
    /// Creates a counter for drone errors.
    /// </summary>
    [Counter<long>("processor_id", Name = "kurrent_processor_drone_errors")]
    public static partial DroneErrorsCounter CreateDroneErrors(Meter meter);

    /// <summary>
    /// Creates a gauge for records tracked but not yet committed — the local replay window.
    /// </summary>
    [Gauge<long>("processor_id", "drone_id", Name = "kurrent_processor_uncommitted_records")]
    public static partial UncommittedRecordsGauge CreateUncommittedRecords(Meter meter);

    /// <summary>
    /// Creates a gauge for the byte span tracked but not yet committed — the local replay window.
    /// </summary>
    [Gauge<long>("processor_id", "drone_id", Name = "kurrent_processor_uncommitted_bytes", Unit = "By")]
    public static partial UncommittedBytesGauge CreateUncommittedBytes(Meter meter);

    /// <summary>
    /// Creates a gauge for the byte distance from the committed position to the readable log head.
    /// </summary>
    [Gauge<long>("processor_id", "drone_id", Name = "kurrent_processor_source_lag_bytes", Unit = "By")]
    public static partial SourceLagBytesGauge CreateSourceLagBytes(Meter meter);

    /// <summary>
    /// Creates a gauge for the event-time distance from the last processed record to the log head, in seconds.
    /// </summary>
    [Gauge<double>("processor_id", "drone_id", Name = "kurrent_processor_source_lag_seconds", Unit = "s")]
    public static partial SourceLagSecondsGauge CreateSourceLagSeconds(Meter meter);
}
