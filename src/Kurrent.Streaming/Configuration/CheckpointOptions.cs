namespace Kurrent.Streaming.Options;

/// <summary>
/// Checkpoint configuration — interval, retention, failure policy.
/// </summary>
[PublicAPI]
public record CheckpointOptions {
    /// <summary>
    /// Master switch for automatic checkpointing. Default: <c>true</c>.
    /// <para>
    /// When <c>true</c>: the periodic interval timer runs and the framework automatically
    /// tracks each record's position when <c>ProcessRecord</c> returns. <c>ctx.Track(...)</c>
    /// throws because the framework is the single writer to the tracker; <c>ctx.Checkpoint(reason)</c>
    /// is allowed (manual barriers compose with the interval timer).
    /// </para>
    /// <para>
    /// When <c>false</c>: the periodic timer is disabled and the framework no longer auto-tracks
    /// on <c>ProcessRecord</c> return. The user is responsible for advancing progress via
    /// <c>ctx.Track(...)</c> and requesting checkpoints via <c>ctx.Checkpoint(reason)</c>.
    /// Use this when you handle your own durability (e.g., flush to a sink and only checkpoint
    /// after a successful flush).
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

    // public int RecordsThreshold { get; init; } = 1000; // this must be per partition or will each drone count all handled records across the partitions its handling?

    /// <summary>
    /// Number of completed checkpoints to retain. Default: 3.
    /// </summary>
    public int RetentionCount { get; set; } = 3;

    /// <summary>
    /// What to do when a checkpoint fails. Default: <see cref="CheckpointFailurePolicy.Continue"/>.
    /// </summary>
    public CheckpointFailurePolicy FailurePolicy { get; set; } = CheckpointFailurePolicy.Continue;

    /// <summary>
    /// Convenience builder with fluent chaining and ms/TimeSpan overloads.
    /// </summary>
    public sealed class Builder(CheckpointOptions options) {
        public Builder AutoCheckpoint(bool enabled)                  { options.AutoCheckpoint = enabled; return this; }
        public Builder Interval(long milliseconds)                   { options.Interval = TimeSpan.FromMilliseconds(milliseconds); return this; }
        public Builder Interval(TimeSpan interval)                   { options.Interval = interval; return this; }
        public Builder RetentionCount(int count)                     { options.RetentionCount = count; return this; }
        public Builder FailurePolicy(CheckpointFailurePolicy policy) { options.FailurePolicy = policy; return this; }
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
