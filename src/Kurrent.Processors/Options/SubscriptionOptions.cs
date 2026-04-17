using Kurrent.Client.Streams;
using Kurrent.Processors.Models;

namespace Kurrent.Processors.Options;

/// <summary>
/// Subscription configuration — what to read and where to start.
/// </summary>
[PublicAPI]
public record SubscriptionOptions {
    /// <summary>
    /// Record filter. Use <see cref="RecordFilter"/> factory methods
    /// (e.g., <c>RecordFilter.ByStream("orders")</c>) or construct an <see cref="ExpressionFilter"/>
    /// directly for SQL-style filtering.
    /// </summary>
    public IRecordFilter Filter { get; set; } = RecordFilter.None;

    /// <summary>
    /// Where to start if no checkpoint exists. Default: <see cref="SubscriptionInitialPosition.Latest"/>.
    /// </summary>
    public SubscriptionInitialPosition InitialPosition { get; set; } = SubscriptionInitialPosition.Latest;

    /// <summary>
    /// Path to extract partition key. Required when parallelism > 1.
    /// Reserved for partitioned processing — not yet exposed as a public knob.
    /// </summary>
    internal string PartitionKeySelector { get; set; } = string.Empty;

    /// <summary>
    /// When set, overrides the stored checkpoint and forces subscription to start from this position.
    /// </summary>
    internal RecordPosition? StartPosition { get; set; }
}
