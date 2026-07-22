using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Models;

namespace Kurrent.Surge.Streaming;

/// <summary>
/// Identity of the processor drone emitting an observer event.
/// </summary>
[PublicAPI]
public readonly record struct ProcessorInfo(
    ProcessorId ProcessorId,
    string      DroneId
);

/// <summary>
/// The terminal outcome of processing a record or a batch.
/// </summary>
[PublicAPI]
public enum RecordOutcome {
    /// <summary>
    /// The handler completed successfully.
    /// </summary>
    Processed = 0,

    /// <summary>
    /// The handler threw an exception.
    /// </summary>
    Failed = 1,

    /// <summary>
    /// Processing was cancelled before the handler completed.
    /// </summary>
    Cancelled = 2
}

/// <summary>
/// Event raised when the drone transitions between lifecycle statuses.
/// </summary>
/// <remarks>
/// <see cref="SessionId"/> is populated on transitions into <see cref="DroneStatus.Processing"/>
/// and <see cref="Client.Streams.SessionId.Unset"/> otherwise. A subscription re-session emits a
/// <c>Processing → Processing</c> transition carrying the new session — reconnects are lifecycle
/// facts, not a separate event.
/// <para>
/// <see cref="Progress"/> is the snapshot as the transition happens; on
/// <see cref="DroneStatus.Stopping"/> it carries the run's final throughput. Transitions are rare,
/// so the snapshot costs nothing here — unlike the per-record path.
/// </para>
/// </remarks>
[PublicAPI]
public readonly record struct ProcessorStateChanged(
    ProcessorInfo  Info,
    DroneStatus    FromStatus,
    DroneStatus    ToStatus,
    DroneProgress  Progress,
    DateTimeOffset Timestamp,
    Exception?     Error     = null,
    SessionId      SessionId = default
);

/// <summary>
/// Event raised when a record is bypassed without being dispatched to the handler.
/// </summary>
/// <remarks>
/// Raised when <see cref="IRecordProcessor.CanProcess"/> returns <c>false</c> — the processor has
/// no handler for the record's message type, so it is never dispatched. The record still advances
/// the checkpoint frontier; this event is what keeps that from being silent.
/// </remarks>
[PublicAPI]
public readonly record struct RecordSkipped(
    ProcessorInfo  Info,
    Record         Record,
    DateTimeOffset Timestamp
);

/// <summary>
/// Event raised when a record is decoded and about to be dispatched to the handler.
/// </summary>
/// <remarks>
/// Carries the live record instance the handler will receive. Mutating entries in
/// <see cref="Record.Metadata"/> here is supported — the handler and everything downstream
/// observe the mutation. Identity surfaces of the record are init-only and cannot be
/// changed in place.
/// </remarks>
[PublicAPI]
public readonly record struct RecordReady(
    ProcessorInfo  Info,
    Record         Record,
    DateTimeOffset Timestamp
);

/// <summary>
/// Event raised exactly once after record processing ends, regardless of outcome.
/// </summary>
[PublicAPI]
public readonly record struct RecordCompleted(
    ProcessorInfo  Info,
    Record         Record,
    RecordOutcome  Outcome,
    TimeSpan       Elapsed,
    DroneProgress  Progress,
    DateTimeOffset Timestamp,
    Exception?     Error = null
);

/// <summary>
/// Event raised when a batch is decoded and about to be dispatched to the handler.
/// </summary>
/// <remarks>
/// Carries the live record instances the handler will receive. Mutating entries in
/// <see cref="Record.Metadata"/> here is supported — the handler and everything downstream
/// observe the mutation. Identity surfaces of the records are init-only and cannot be
/// changed in place.
/// </remarks>
[PublicAPI]
public readonly record struct BatchReady(
    ProcessorInfo         Info,
    IReadOnlyList<Record> Records,
    DateTimeOffset        Timestamp
);

/// <summary>
/// Event raised exactly once after batch processing ends, regardless of outcome.
/// </summary>
/// <remarks>
/// <see cref="ProcessedCount"/> is the committed/durable prefix — records at or below the
/// resolved frontier — which is what throughput accounting must count for batches;
/// <c>Records.Count</c> is the dispatched size.
/// </remarks>
[PublicAPI]
public readonly record struct BatchCompleted(
    ProcessorInfo         Info,
    IReadOnlyList<Record> Records,
    RecordOutcome         Outcome,
    int                   ProcessedCount,
    TimeSpan              Elapsed,
    DroneProgress         Progress,
    DateTimeOffset        Timestamp,
    Exception?            Error = null
);

/// <summary>
/// Event raised when the source-lag probe samples how far the drone trails the log head.
/// </summary>
/// <remarks>
/// Opt-in: raised only when <c>ProcessorOptions.TrackSourceLag</c> is set. The probe reads the
/// readable log head on a fixed cadence — the only way a gRPC client learns the head moved ahead —
/// so samples arrive on the probe's timer, not on the record path.
/// </remarks>
[PublicAPI]
public readonly record struct SourceLagSampled(
    ProcessorInfo  Info,
    long           LagBytes,
    double         LagSeconds,
    DateTimeOffset Timestamp
);

/// <summary>
/// Event raised when a checkpoint has been committed.
/// </summary>
[PublicAPI]
public readonly record struct CheckpointCommitted(
    ProcessorInfo                 Info,
    CheckpointTrigger             Trigger,
    string                        CheckpointId,
    IReadOnlyList<RecordPosition> Positions,
    TimeSpan                      Duration,
    DateTimeOffset                Timestamp
);

/// <summary>
/// Event raised when a checkpoint failed to commit.
/// </summary>
[PublicAPI]
public readonly record struct CheckpointFailed(
    ProcessorInfo                 Info,
    CheckpointTrigger             Trigger,
    string                        CheckpointId,
    IReadOnlyList<RecordPosition> Positions,
    Exception                     Error,
    DateTimeOffset                Timestamp
);
