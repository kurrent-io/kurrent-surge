// ReSharper disable CheckNamespace

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Kurrent.Client.Processors.Diagnostics;

/// <summary>
/// Diagnostics facade for processor workers.
/// Provides zero-overhead instrumentation when no listener is registered.
/// </summary>
/// <remarks>
/// Uses <see cref="DiagnosticSource"/> internally to raise events.
/// Register a <see cref="ProcessorDiagnosticsObserver"/> to enable metrics and tracing.
/// </remarks>
static class ProcessorDiagnostics {
    /// <summary>
    /// The diagnostic source name used for processor events.
    /// </summary>
    public const string SourceName = "Kurrent.Processors";

    static readonly DiagnosticSource Source = new DiagnosticListener(SourceName);

    /// <summary>
    /// Activity source for distributed tracing.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");

    /// <summary>
    /// Starts tracking a batch processing operation.
    /// Duration is recorded when <see cref="BatchScope.Complete"/> is called.
    /// </summary>
    /// <param name="processorId">The processor identifier.</param>
    /// <param name="workerId">The worker identifier.</param>
    /// <returns>A scope that records the batch processing event when completed.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BatchScope TrackBatch(string processorId, string workerId) {
        var activity = ActivitySource.StartActivity(
            "processor.process_batch",
            ActivityKind.Consumer,
            parentContext: default,
            tags: [
                new("processor.id", processorId),
                new("processor.worker_id", workerId)
            ]
        );

        var timestamp = Source.IsEnabled("BatchProcessed") ? Stopwatch.GetTimestamp() : 0;
        return new BatchScope(processorId, workerId, timestamp, activity);
    }

    /// <summary>
    /// Starts tracking a checkpoint operation.
    /// Duration is recorded when <see cref="CheckpointScope.Complete"/> is called.
    /// </summary>
    /// <param name="processorId">The processor identifier.</param>
    /// <param name="workerId">The worker identifier.</param>
    /// <returns>A scope that records the checkpoint event when completed.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CheckpointScope TrackCheckpoint(string processorId, string workerId) {
        var activity = ActivitySource.StartActivity(
            "processor.checkpoint",
            ActivityKind.Internal,
            parentContext: default,
            tags: [
                new("processor.id", processorId),
                new("processor.worker_id", workerId)
            ]
        );

        var timestamp = Source.IsEnabled("CheckpointCompleted") ? Stopwatch.GetTimestamp() : 0;
        return new CheckpointScope(processorId, workerId, timestamp, activity);
    }

    /// <summary>
    /// Records a worker connected event.
    /// </summary>
    /// <param name="processorId">The processor identifier.</param>
    /// <param name="workerId">The worker identifier.</param>
    /// <param name="sessionId">The session identifier assigned by the server.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WorkerConnected(string processorId, string workerId, Guid sessionId) {
        if (Source.IsEnabled("WorkerConnected"))
            Source.Write("WorkerConnected", new WorkerConnectedEvent(processorId, workerId, sessionId));
    }

    /// <summary>
    /// Records a worker disconnected event.
    /// </summary>
    /// <param name="processorId">The processor identifier.</param>
    /// <param name="workerId">The worker identifier.</param>
    /// <param name="totalRecords">Total records processed during the session.</param>
    /// <param name="duration">Total duration of the session.</param>
    /// <param name="failed">Whether the worker disconnected due to a failure.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WorkerDisconnected(string processorId, string workerId, long totalRecords, TimeSpan duration, bool failed) {
        if (Source.IsEnabled("WorkerDisconnected"))
            Source.Write("WorkerDisconnected", new WorkerDisconnectedEvent(processorId, workerId, totalRecords, duration, failed));
    }

    /// <summary>
    /// Writes a batch processed event. Called internally by <see cref="BatchScope"/>.
    /// </summary>
    internal static void WriteBatchProcessed(BatchProcessedEvent evt) =>
        Source.Write("BatchProcessed", evt);

    /// <summary>
    /// Writes a checkpoint completed event. Called internally by <see cref="CheckpointScope"/>.
    /// </summary>
    internal static void WriteCheckpointCompleted(CheckpointCompletedEvent evt) =>
        Source.Write("CheckpointCompleted", evt);
}
