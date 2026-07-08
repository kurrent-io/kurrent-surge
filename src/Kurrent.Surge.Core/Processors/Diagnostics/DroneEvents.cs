namespace Kurrent.Streaming.Diagnostics;

/// <summary>
/// Event raised when a drone is starting up.
/// </summary>
public readonly record struct DroneStartingEvent(
    string ProcessorId,
    string DroneId
);

/// <summary>
/// Event raised when a drone is restoring state from checkpoints.
/// </summary>
public readonly record struct DroneRestoringStateEvent(
    string ProcessorId,
    string DroneId,
    int PartitionCount
);

/// <summary>
/// Event raised when a drone has finished restoring state.
/// </summary>
public readonly record struct DroneStateRestoredEvent(
    string ProcessorId,
    string DroneId,
    ulong StartPosition,
    int PartitionCount
);

/// <summary>
/// Event raised when a drone begins processing records.
/// </summary>
public readonly record struct DroneProcessingEvent(
    string ProcessorId,
    string DroneId,
    Guid SessionId
);

/// <summary>
/// Event raised when a record has been processed.
/// </summary>
public readonly record struct RecordProcessedEvent(
    string ProcessorId,
    string DroneId,
    long TotalRecords,
    double? DurationSeconds = null,
    long UncommittedRecords = 0,
    long UncommittedBytes = 0
);

/// <summary>
/// Event raised once per batch after <c>ProcessBatch</c> returns. <see cref="ProcessedCount"/> is the
/// committed/durable prefix (records at or below the resolved frontier), which is what
/// <c>records_processed</c> counts; <see cref="DurationSeconds"/> times the real <c>ProcessBatch</c> call.
/// </summary>
public readonly record struct BatchProcessedEvent(
    string ProcessorId,
    string DroneId,
    long TotalRecords,
    int ProcessedCount,
    double? DurationSeconds = null,
    long UncommittedRecords = 0,
    long UncommittedBytes = 0
);

/// <summary>
/// Event raised when the source-lag probe samples the distance to the readable log head.
/// </summary>
public readonly record struct SourceLagSampleEvent(
    string ProcessorId,
    string DroneId,
    long LagBytes,
    double LagSeconds
);

/// <summary>
/// Event raised when a checkpoint is starting.
/// </summary>
public readonly record struct CheckpointInitiatedEvent(
    string ProcessorId,
    string DroneId,
    string Trigger,
    string CheckpointId
);

/// <summary>
/// Event raised when a checkpoint has been completed.
/// </summary>
public readonly record struct CheckpointCompletedEvent(
    string ProcessorId,
    string DroneId,
    string Trigger,
    string CheckpointId,
    int PartitionCount,
    double DurationSeconds
);

/// <summary>
/// Event raised when a checkpoint failed to complete.
/// </summary>
public readonly record struct CheckpointFailedEvent(
    string ProcessorId,
    string DroneId,
    string Trigger,
    string CheckpointId,
    string ExceptionType,
    string Message
);

/// <summary>
/// Event raised when a drone is stopping.
/// </summary>
public readonly record struct DroneStoppingEvent(
    string ProcessorId,
    string DroneId,
    long TotalRecords,
    TimeSpan Duration,
    double RecordsPerSecond
);

/// <summary>
/// Event raised when a drone has stopped cleanly.
/// </summary>
public readonly record struct DroneStoppedEvent(
    string ProcessorId,
    string DroneId,
    long TotalRecords,
    TimeSpan Duration
);

/// <summary>
/// Event raised when a drone has stopped due to an error.
/// </summary>
public readonly record struct DroneErrorEvent(
    string ProcessorId,
    string DroneId,
    string ExceptionType,
    string Message
);
