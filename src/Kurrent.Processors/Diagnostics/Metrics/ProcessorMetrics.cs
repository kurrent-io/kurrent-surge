using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;

namespace Kurrent.Processors.Diagnostics.Metrics;

/// <summary>
/// Source-generated metrics for drones.
/// </summary>
internal static partial class ProcessorMetrics {
    /// <summary>
    /// Creates a counter for records processed.
    /// </summary>
    [Counter<long>("processor_id", "drone_id", Name = "kurrent_processor_records_processed")]
    public static partial RecordsProcessedCounter CreateRecordsProcessed(Meter meter);

    /// <summary>
    /// Creates a histogram for record processing duration (milliseconds).
    /// </summary>
    [Histogram<double>("processor_id", Name = "kurrent_processor_record_duration_ms")]
    public static partial RecordDurationHistogram CreateRecordDuration(Meter meter);

    /// <summary>
    /// Creates a counter for checkpoints completed.
    /// </summary>
    [Counter<long>("processor_id", "drone_id", Name = "kurrent_processor_checkpoints_completed")]
    public static partial CheckpointsCompletedCounter CreateCheckpointsCompleted(Meter meter);

    /// <summary>
    /// Creates a histogram for checkpoint duration (milliseconds).
    /// </summary>
    [Histogram<double>("processor_id", Name = "kurrent_processor_checkpoint_duration_ms")]
    public static partial CheckpointDurationHistogram CreateCheckpointDuration(Meter meter);

    /// <summary>
    /// Creates a counter for drones connected.
    /// </summary>
    [Counter<long>("processor_id", Name = "kurrent_processor_drones_connected")]
    public static partial DronesConnectedCounter CreateDronesConnected(Meter meter);

    /// <summary>
    /// Creates a counter for drone errors.
    /// </summary>
    [Counter<long>("processor_id", Name = "kurrent_processor_drone_errors")]
    public static partial DroneErrorsCounter CreateDroneErrors(Meter meter);
}
