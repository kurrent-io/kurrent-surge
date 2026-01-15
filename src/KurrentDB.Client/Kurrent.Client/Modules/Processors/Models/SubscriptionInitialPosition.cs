namespace Kurrent.Client.Streams;

/// <summary>
/// Initial position at which the cursor will be set when subscribing.
/// </summary>
/// <remarks>
/// <para>
/// Only used when:
/// <list type="bullet">
///   <item>Processor is created for the first time (no checkpoint exists)</item>
///   <item>All checkpoints have been deleted</item>
/// </list>
/// </para>
/// <para>
/// Once a checkpoint exists, the processor always resumes from the last
/// checkpoint position, ignoring this setting.
/// </para>
/// </remarks>
[PublicAPI]
public enum SubscriptionInitialPosition {
    /// <summary>
    /// Unspecified initial position. The server will use the default behavior.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Start from current end of log (new records only).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The processor will only see records written AFTER it starts.
    /// Historical data is ignored.
    /// </para>
    /// <para>Use when: You only care about new events, not historical data.</para>
    /// </remarks>
    Latest = 1,

    /// <summary>
    /// Start from beginning of log (all records).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The processor will receive all historical records before
    /// transitioning to live processing (triggers <c>CaughtUp</c>).
    /// </para>
    /// <para>Use when: You need to process/replay all historical data.</para>
    /// </remarks>
    Earliest = 2
}
