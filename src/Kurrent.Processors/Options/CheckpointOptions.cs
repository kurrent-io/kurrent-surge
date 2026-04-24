namespace Kurrent.Processors.Options;

/// <summary>
/// Checkpoint configuration — interval, retention, failure policy.
/// </summary>
[PublicAPI]
public record CheckpointOptions {
    /// <summary>
    /// Time between automatic checkpoints. Default: 30 seconds.
    /// In JSON config, specify as milliseconds (<c>"Interval": 30000</c>) or TimeSpan format
    /// (<c>"Interval": "00:00:30"</c>).
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
        public Builder Interval(long milliseconds) { options.Interval = TimeSpan.FromMilliseconds(milliseconds); return this; }
        public Builder Interval(TimeSpan interval) { options.Interval = interval; return this; }
        public Builder RetentionCount(int count)   { options.RetentionCount = count; return this; }
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
