namespace Kurrent.Surge.Processors.Configuration;

/// <summary>
/// Checkpoint configuration. Two independent switches decide a processor's delivery guarantee:
/// <see cref="AutoTrack"/> (when a record's position is marked handled) and
/// <see cref="AutoCheckpoint"/> (when handled positions are committed). Each has a manual escape
/// hatch on the processing context (<c>ctx.Track(...)</c> / <c>ctx.Checkpoint(...)</c>), so any
/// guarantee is reachable by flipping two booleans.
/// </summary>
/// <remarks>
/// <para>
/// <b>Track</b> marks a record's position as handled — held in memory, pending commit.
/// <b>Checkpoint</b> persists all pending tracked positions so a restart resumes past them.
/// </para>
/// <para>The two switches yield four delivery quadrants:</para>
/// <list type="table">
///   <listheader>
///     <term>AutoTrack / AutoCheckpoint</term>
///     <description>Guarantee</description>
///   </listheader>
///   <item>
///     <term><c>true</c> / <c>true</c></term>
///     <description>Hands-off (default) — the framework tracks every record and commits on the interval.</description>
///   </item>
///   <item>
///     <term><c>false</c> / <c>true</c></term>
///     <description>At-least-once — track in the handler after durable work; the timer commits.</description>
///   </item>
///   <item>
///     <term><c>false</c> / <c>false</c></term>
///     <description>Full manual — <c>ctx.Track</c> and <c>ctx.Checkpoint</c> own everything.</description>
///   </item>
///   <item>
///     <term><c>true</c> / <c>false</c></term>
///     <description>Auto-track, manual commit — tracked automatically, committed only by <c>ctx.Checkpoint</c> or shutdown.</description>
///   </item>
/// </list>
/// <para>
/// Shutdown and end-of-stream always commit pending positions, in every mode; manual
/// <c>ctx.Checkpoint</c> always works, in every mode.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class CheckpointOptions {
    /// <summary>
    /// Gates all automatic position tracking. Default: <c>true</c>.
    /// <para>
    /// When <c>true</c>: the framework tracks each record's position after the handler returns
    /// (per-record and whole-batch) and folds server scan watermarks (<c>CaughtUp</c> /
    /// <c>Filter</c>) across owned partitions, so a cold restart skips the already-scanned log.
    /// </para>
    /// <para>
    /// When <c>false</c>: the framework advances <b>nothing</b> on its own — neither records nor
    /// scan watermarks. The user owns progress via <c>ctx.Track(...)</c>. Each scan watermark is
    /// surfaced to <c>OnCheckpoint</c> through <c>CheckpointContext.ScanPosition</c>; if the handler
    /// does not track it, a cold restart rescans the already-scanned no-match / caught-up log.
    /// </para>
    /// </summary>
    public bool AutoTrack { get; set; } = true;

    /// <summary>
    /// Gates the periodic interval commit timer. Default: <c>true</c>.
    /// <para>
    /// When <c>true</c>: a timer commits the pending tracked positions every <see cref="Interval"/>.
    /// </para>
    /// <para>
    /// When <c>false</c>: no interval timer runs — pending positions are committed only by a manual
    /// <c>ctx.Checkpoint(reason)</c> barrier or on shutdown / end-of-stream. Independent of
    /// <see cref="AutoTrack"/>: pairing <c>AutoTrack = false</c> with <c>AutoCheckpoint = true</c>
    /// is the at-least-once recipe — manual track, timed commit.
    /// </para>
    /// </summary>
    public bool AutoCheckpoint { get; set; } = true;

    /// <summary>
    /// Time between automatic checkpoints. Default: 30 seconds.
    /// In JSON config, specify as milliseconds (<c>"Interval": 30000</c>) or TimeSpan format
    /// (<c>"Interval": "00:00:30"</c>).
    /// <para>
    /// Consulted only when <see cref="AutoCheckpoint"/> is <c>true</c>.
    /// </para>
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Number of completed checkpoints to retain. Default: 3.
    /// </summary>
    public int RetentionCount { get; set; } = 3;

    /// <summary>
    /// What to do when a checkpoint fails. Default: <see cref="CheckpointFailurePolicy.Continue"/>.
    /// </summary>
    public CheckpointFailurePolicy FailurePolicy { get; set; } = CheckpointFailurePolicy.Continue;

    /// <summary>
    /// Sets <see cref="Interval"/> and returns this instance for fluent chaining.
    /// </summary>
    /// <param name="interval">The time between automatic checkpoints.</param>
    /// <returns>This <see cref="CheckpointOptions"/> instance.</returns>
    public CheckpointOptions IntervalFrom(TimeSpan interval) {
        Interval = interval;
        return this;
    }

    /// <summary>
    /// Sets <see cref="Interval"/> from a millisecond value and returns this instance for fluent chaining.
    /// </summary>
    /// <param name="milliseconds">The time between automatic checkpoints, in milliseconds.</param>
    /// <returns>This <see cref="CheckpointOptions"/> instance.</returns>
    public CheckpointOptions IntervalFrom(long milliseconds) {
        Interval = TimeSpan.FromMilliseconds(milliseconds);
        return this;
    }
}

/// <summary>
/// Defines what happens when a checkpoint fails.
/// </summary>
[PublicAPI]
public enum CheckpointFailurePolicy {
    /// <summary>
    /// Abort checkpoint, continue processing. Failed drone's partitions are reassigned. Default.
    /// </summary>
    Continue = 0,

    /// <summary>
    /// Stop all processing. Requires manual intervention to resume.
    /// </summary>
    Fail = 1
}
