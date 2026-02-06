namespace Kurrent.Processors.Diagnostics;

/// <summary>
/// Event raised when a worker is starting up.
/// </summary>
public readonly record struct WorkerStartingEvent(
    string ProcessorId,
    string WorkerId
);

/// <summary>
/// Event raised when a worker is restoring state from checkpoints.
/// </summary>
public readonly record struct WorkerRestoringStateEvent(
    string ProcessorId,
    string WorkerId,
    int PartitionCount
);

/// <summary>
/// Event raised when a worker has finished restoring state.
/// </summary>
public readonly record struct WorkerStateRestoredEvent(
    string ProcessorId,
    string WorkerId,
    ulong StartPosition,
    int PartitionCount
);

/// <summary>
/// Event raised when a worker begins processing records.
/// </summary>
public readonly record struct WorkerProcessingEvent(
    string ProcessorId,
    string WorkerId,
    Guid SessionId
);

/// <summary>
/// Event raised when a batch of records has been processed.
/// </summary>
public readonly record struct BatchProcessedEvent(
    string ProcessorId,
    string WorkerId,
    int BatchSize,
    long TotalRecords,
    double DurationMs
);

/// <summary>
/// Event raised when a checkpoint is starting.
/// </summary>
public readonly record struct WorkerCheckpointingEvent(
    string ProcessorId,
    string WorkerId,
    string Trigger,
    string CheckpointId
);

/// <summary>
/// Event raised when a checkpoint has been completed.
/// </summary>
public readonly record struct CheckpointCompletedEvent(
    string ProcessorId,
    string WorkerId,
    string Trigger,
    string CheckpointId,
    int PartitionCount,
    double DurationMs
);

/// <summary>
/// Event raised when a worker is stopping.
/// </summary>
public readonly record struct WorkerStoppingEvent(
    string ProcessorId,
    string WorkerId,
    long TotalRecords,
    TimeSpan Duration,
    double RecordsPerSecond
);

/// <summary>
/// Event raised when a worker has stopped cleanly.
/// </summary>
public readonly record struct WorkerStoppedEvent(
    string ProcessorId,
    string WorkerId,
    long TotalRecords,
    TimeSpan Duration
);

/// <summary>
/// Event raised when a final checkpoint is being performed.
/// </summary>
public readonly record struct WorkerFinalCheckpointEvent(
    string ProcessorId,
    string WorkerId,
    int PartitionCount
);

/// <summary>
/// Event raised when a final checkpoint has failed.
/// </summary>
public readonly record struct WorkerFinalCheckpointFailedEvent(
    string ProcessorId,
    string WorkerId,
    string ExceptionType,
    string Message
);

/// <summary>
/// Event raised when a worker has stopped due to an error.
/// </summary>
public readonly record struct WorkerErrorEvent(
    string ProcessorId,
    string WorkerId,
    string ExceptionType,
    string Message
);
