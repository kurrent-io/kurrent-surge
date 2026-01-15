// ReSharper disable CheckNamespace

using Kurrent.Client.Streams;

namespace Kurrent.Client.Processors.Diagnostics;

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
/// Event raised when a checkpoint has been completed.
/// </summary>
public readonly record struct CheckpointCompletedEvent(
    string ProcessorId,
    string WorkerId,
    LogPosition BarrierId,
    int PartitionCount,
    double DurationMs
);

/// <summary>
/// Event raised when a worker has connected to the server.
/// </summary>
public readonly record struct WorkerConnectedEvent(
    string ProcessorId,
    string WorkerId,
    Guid SessionId
);

/// <summary>
/// Event raised when a worker has disconnected from the server.
/// </summary>
public readonly record struct WorkerDisconnectedEvent(
    string ProcessorId,
    string WorkerId,
    long TotalRecords,
    TimeSpan Duration,
    bool Failed
);
