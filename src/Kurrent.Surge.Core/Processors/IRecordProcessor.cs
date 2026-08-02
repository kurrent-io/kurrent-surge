using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Models;

namespace Kurrent.Surge.Streaming;

/// <summary>
/// Defines the interface for processing records one at a time.
/// Implement this interface to receive callbacks during the processing lifecycle.
/// <para>
/// Lifecycle methods (<see cref="IProcessor.OnConnected"/>, <see cref="IProcessor.OnCheckpoint"/>)
/// are inherited from <see cref="IProcessor"/> with default no-op implementations. Only
/// <see cref="ProcessRecord"/> must be implemented.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// All lifecycle methods are informational. The SDK handles checkpointing,
/// state management, and coordination automatically.
/// </para>
/// <para>
/// For batched dispatch, see <see cref="IRecordBatchProcessor"/>.
/// </para>
/// </remarks>
[PublicAPI]
public interface IRecordProcessor : IProcessor {
    /// <summary>
    /// Called when a record is available for processing.
    /// <remarks>
    /// This is the only method that must be implemented.
    /// </remarks>
    /// </summary>
    /// <param name="context">
    /// Context containing the record and cancellation token.
    /// </param>
    ValueTask ProcessRecord(ProcessRecordContext context);

    /// <summary>
    /// Called before dispatch to ask whether this processor has anything that will handle the
    /// record. Returning <c>false</c> means the record is never passed to
    /// <see cref="ProcessRecord"/>. Defaults to <c>true</c> — handle everything.
    /// </summary>
    /// <remarks>
    /// Override this when the processor handles only a subset of what it receives, so the records
    /// it ignores are reported rather than silently consumed. A skipped record still advances the
    /// checkpoint frontier: were it otherwise, a stream of unhandled types would never commit and
    /// the processor would replay it forever. Called on every record — keep it cheap and free of
    /// side effects.
    /// </remarks>
    bool CanProcess(Record record) => true;
}

public record DroneContext {
    public static readonly DroneContext None = new DroneContext();

    public ProcessorId ProcessorId { get; init; } = ProcessorId.Unset;

    public string DroneId { get; init; } = "";

    public SessionId SessionId { get; init; } = SessionId.Unset;

    public ILogger Logger { get; init; } = NullLogger.Instance;

    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;

    /// <summary>
    /// Reads the drone's live progress. Nothing is snapshotted until this is called, and the value
    /// reflects the moment of the call rather than the moment of dispatch — so a handler that tracks
    /// positions sees its own effect.
    /// </summary>
    public DroneProgress Progress => ProgressSource();

    // Supplied by the drone; the same live-view shape as RequestTrack. Defaults to Empty so a
    // hand-built context stays usable in tests.
    internal Func<DroneProgress> ProgressSource { get; init; } = static () => DroneProgress.Empty;

    public ProcessRecordContext ForProcessRecord(Record record, Action<RecordPosition> requestTrack) =>
        new(requestTrack) {
            ProcessorId       = ProcessorId,
            DroneId           = DroneId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            ProgressSource    = ProgressSource,
            Record            = record
        };

    public BatchContext ForBatch(RecordBatch records, Action<RecordPosition> requestTrack) =>
        new(requestTrack) {
            ProcessorId       = ProcessorId,
            DroneId           = DroneId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            ProgressSource    = ProgressSource,
            Records           = records
        };

    public InitializeContext ForInitialize(
        Partition[] partitions,
        SubscriptionInitialPosition initialPosition,
        LogPosition startPosition
    ) =>
        new() {
            ProcessorId       = ProcessorId,
            DroneId           = DroneId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            ProgressSource    = ProgressSource,
            Partitions        = partitions,
            InitialPosition   = initialPosition,
            StartPosition     = startPosition
        };

    public ConnectedContext ForConnected() =>
        new() {
            ProcessorId       = ProcessorId,
            DroneId           = DroneId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            ProgressSource    = ProgressSource
        };

    public CheckpointContext ForCheckpoint(
        Checkpoint checkpoint,
        RecordPosition[] positions,
        RecordPosition? scanPosition,
        Action<RecordPosition> requestTrack
    ) =>
        new(requestTrack) {
            ProcessorId       = ProcessorId,
            DroneId           = DroneId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            ProgressSource    = ProgressSource,
            Positions         = positions,
            CheckpointId      = checkpoint.Id,
            Trigger           = checkpoint.TriggerKind,
            Timestamp         = checkpoint.Timestamp,
            Reason            = (checkpoint as ManualCheckpoint)?.Reason,
            ScanPosition      = scanPosition
        };

}

/// <summary>
/// Context provided when a record is available for processing.
/// </summary>
[PublicAPI]
public record ProcessRecordContext : DroneContext {

    public ProcessRecordContext() { }

    public ProcessRecordContext(Action<RecordPosition> requestTrack) =>
        RequestTrack = requestTrack;

    public required Record Record { get; init; }

    public Action<RecordPosition> RequestTrack { get; init; } = _ => { };

    /// <summary>
    /// Tells the framework that the given record has been durably handled.
    /// Always valid; in <c>AutoTrack = true</c> mode the framework's auto-track
    /// runs on top, but the tracker is idempotent.
    /// </summary>
    public void Track(Record record) {
        _trackedByUser = true;
        RequestTrack(record.Position);
    }

    /// <summary>
    /// Tells the framework that the given position has been durably handled.
    /// Always valid; in <c>AutoTrack = true</c> mode the framework's auto-track
    /// runs on top, but the tracker is idempotent.
    /// </summary>
    public void Track(RecordPosition position) {
        _trackedByUser = true;
        RequestTrack(position);
    }

    /// <summary>
    /// Tells the framework that the given positions have been durably handled.
    /// Always valid; in <c>AutoTrack = true</c> mode the framework's auto-track
    /// runs on top, but the tracker is idempotent.
    /// </summary>
    public void Track(IEnumerable<RecordPosition> positions) {
        foreach (var p in positions) {
            _trackedByUser = true;
            RequestTrack(p);
        }
    }

    /// <summary>
    /// Requests that processing stop after this record. The framework will inject a
    /// shutdown marker once the current dispatch finishes. This record will NOT be
    /// auto-tracked — call <see cref="Track(Record)"/> first if you want it claimed.
    /// </summary>
    public void StopProcessing(string? reason = null) {
        _stopRequested = true;
        _stopReason    = reason;
    }

    /// <summary>
    /// Requests a manual checkpoint barrier. The framework will raise
    /// <c>OnCheckpoint</c> with <see cref="CheckpointTrigger.Manual"/> and the supplied
    /// <paramref name="reason"/>; positions are taken from the tracker's state at the
    /// moment the framework dispatches the marker (after auto-track for this record).
    /// At most one manual checkpoint may be requested per <c>ProcessRecord</c> invocation.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A checkpoint has already been requested for this record, or a stop has already been requested
    /// (after <see cref="StopProcessing"/> the shutdown barrier is the single commit point).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Cancellation has been requested on the context's token.
    /// </exception>
    public void Checkpoint(string? reason = null) {
        if (_checkpointRequested)
            throw new InvalidOperationException("Checkpoint already requested for this record");

        // Once a stop is requested the graceful Shutdown barrier becomes the single OnCheckpoint +
        // commit point, so a manual checkpoint is meaningless — reject it rather than queue a barrier
        // the drone will suppress.
        if (_stopRequested)
            throw new InvalidOperationException("Cannot request a checkpoint after StopProcessing.");

        CancellationToken.ThrowIfCancellationRequested();

        _checkpointRequested = true;
        _checkpointReason    = reason;
    }

    bool    _checkpointRequested;
    string? _checkpointReason;

    bool    _stopRequested;
    string? _stopReason;

    bool    _trackedByUser;

    internal bool    CheckpointRequested => _checkpointRequested;
    internal string? CheckpointReason    => _checkpointReason;

    internal bool    StopRequested  => _stopRequested;
    internal string? StopReason     => _stopReason;
    internal bool    TrackedByUser  => _trackedByUser;
}

/// <summary>
/// Context provided at startup, before the checkpoint is resolved and before the subscription opens.
/// Carries the partitions this drone owns and the configured start semantics, so a processor that
/// keeps its own checkpoints can resolve its resume positions and claim them via
/// <see cref="OwnCheckpoints"/>.
/// </summary>
[PublicAPI]
public record InitializeContext : DroneContext {
    /// <summary>The partitions this drone owns.</summary>
    public required Partition[] Partitions { get; init; }

    /// <summary>Fallback for partitions with no committed position.</summary>
    public required SubscriptionInitialPosition InitialPosition { get; init; }

    /// <summary>
    /// An explicit start override. When set, it supersedes every stored position — a checkpoint
    /// owner must reset its own state to this position rather than load what it stored.
    /// <see cref="LogPosition.Unset"/> means normal resume.
    /// </summary>
    public required LogPosition StartPosition { get; init; }

    /// <summary>
    /// Declares this processor the checkpoint owner: it loads, commits and resets its own positions,
    /// and the drone stops writing its checkpoint store for this processor.
    /// </summary>
    /// <remarks>
    /// All three operations move together — a processor that supplies its resume positions must also
    /// persist them, or the two records of progress diverge and the processor replays the same span
    /// forever. A checkpoint owner commits its positions from <c>OnCheckpoint</c>, and must not call
    /// <c>ctx.Track</c> there: it would commit one set and flush a larger one.
    /// </remarks>
    /// <param name="resumeFrom">The positions this processor resumes from, one per owned partition.</param>
    public void OwnCheckpoints(RecordPosition[] resumeFrom) => OwnedCheckpoints = resumeFrom;

    internal RecordPosition[]? OwnedCheckpoints { get; private set; }
}

/// <summary>
/// Context provided when the drone has connected and is ready to process records.
/// </summary>
[PublicAPI]
public record ConnectedContext : DroneContext;

/// <summary>
/// Context provided when a checkpoint is signaled. Fires on every barrier (interval, manual, server
/// scan watermark, shutdown) and runs <b>before</b> the commit snapshot, so positions tracked here
/// via <see cref="Track(RecordPosition)"/> land in this barrier's commit.
/// </summary>
[PublicAPI]
public record CheckpointContext : DroneContext {
    public CheckpointContext() { }

    public CheckpointContext(Action<RecordPosition> requestTrack) =>
        RequestTrack = requestTrack;

    public required RecordPosition[]   Positions    { get; init; }
    public required string             CheckpointId { get; init; }
    public required CheckpointTrigger  Trigger      { get; init; }
    public required DateTimeOffset     Timestamp    { get; init; }

    /// <summary>
    /// Optional reason associated with this checkpoint. Populated when
    /// <see cref="Trigger"/> is <see cref="CheckpointTrigger.Manual"/> and the user
    /// passed a reason to <c>ctx.Checkpoint(reason)</c>. Null otherwise.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// The server scan watermark for a <see cref="CheckpointTrigger.Filter"/> or
    /// <see cref="CheckpointTrigger.CaughtUp"/> barrier — the log position the shared scan has
    /// reached over regions with no matching records. The barrier carries a log position and has no
    /// knowledge of partitions; <see cref="Track(RecordPosition)"/> folds it across this drone's
    /// owned partitions. <c>null</c> for every other trigger.
    /// </summary>
    /// <remarks>
    /// With <c>AutoTrack = true</c> the framework already folds this watermark across owned
    /// partitions, so the value is informational. With <c>AutoTrack = false</c> the framework
    /// advances nothing — call <c>ctx.Track(ctx.ScanPosition!)</c> to claim the scanned span, or a
    /// cold restart rescans the already-scanned no-match / caught-up region.
    /// </remarks>
    public RecordPosition? ScanPosition { get; init; }

    internal Action<RecordPosition> RequestTrack { get; init; } = static _ => { };

    /// <summary>
    /// Marks the given position's log position as durably handled, from inside <c>OnCheckpoint</c>,
    /// folding it across this drone's owned partitions. Because the callback runs before the commit
    /// snapshot, the position is committed by this same barrier. Positions at or below the current
    /// per-partition progress are skipped, so re-tracking a stale watermark is a no-op.
    /// </summary>
    /// <param name="position">The position to track — typically <see cref="ScanPosition"/>.</param>
    public void Track(RecordPosition position) =>
        RequestTrack(position);

    /// <summary>
    /// Marks each of the given positions as durably handled, from inside <c>OnCheckpoint</c>,
    /// folding each across this drone's owned partitions.
    /// </summary>
    /// <param name="positions">The positions to track.</param>
    public void Track(IEnumerable<RecordPosition> positions) {
        foreach (var position in positions)
            RequestTrack(position);
    }
}

/// <summary>
/// Progress for a processor session: how much has been handled, how fast, and how much would be
/// replayed if the process died right now.
/// </summary>
/// <remarks>
/// Every member is a value type, so a snapshot costs a copy and never allocates — it is taken on the
/// per-record path. Per-partition state lives inside the tracker; what surfaces here are the two
/// aggregates that mean something: the lowest committed position (where a restart resumes from) and
/// the highest (whose distance from the lowest is commit skew across partitions).
/// </remarks>
[PublicAPI]
public readonly record struct DroneProgress {
    // Required by the position initializers below: Unset is not the default RecordPosition, and a
    // zeroed struct would read as "committed at position 0" rather than "nothing committed yet".
    public DroneProgress() { }

    /// <summary>
    /// Total records dispatched this session, whether or not they were tracked.
    /// </summary>
    public long TotalRecords { get; init; }

    /// <summary>
    /// Average records dispatched per second since the session started.
    /// </summary>
    public double RecordsPerSecond { get; init; }

    /// <summary>
    /// When the session started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Time elapsed since the session started.
    /// </summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>
    /// Event-time of the last dispatched record, or <c>null</c> if none yet. Compared against the
    /// readable log head to derive how far behind in event-time the processor is running.
    /// </summary>
    public DateTime? LastProcessedAt { get; init; }

    /// <summary>
    /// Lowest committed position across owned partitions — the point a restart resumes from, since a
    /// resuming run is only as durable as its least-advanced partition. Seeded at startup with the
    /// position the checkpointer resolved, so it is meaningful before the first checkpoint;
    /// <see cref="RecordPosition.Unset"/> only when the run started at the live edge.
    /// </summary>
    public RecordPosition LowestCommittedPosition { get; init; } = RecordPosition.Unset;

    /// <summary>
    /// Highest committed position across owned partitions. Its distance from
    /// <see cref="LowestCommittedPosition"/> is the commit skew — a partition falling behind shows up
    /// as that gap widening. Equal to the lowest while a single partition is owned.
    /// </summary>
    public RecordPosition HighestCommittedPosition { get; init; } = RecordPosition.Unset;

    /// <summary>
    /// Records dispatched since the last commit — what would be reprocessed if the process died now.
    /// </summary>
    public long UncommittedRecords { get; init; }

    /// <summary>
    /// Bytes tracked but not yet committed — the same replay window in bytes, summed across
    /// partitions. Log positions are byte offsets, so this is the distance from the resume point to
    /// the tracked frontier: what a crash would replay, measured in bytes.
    /// </summary>
    public long UncommittedBytes { get; init; }

    /// <summary>
    /// Empty progress: nothing dispatched, nothing committed.
    /// </summary>
    public static readonly DroneProgress Empty = new();
}

