using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors.Models;

namespace Kurrent.Surge.Processors.Configuration;

/// <summary>
/// Subscription configuration — what to read and where to start.
/// </summary>
[PublicAPI]
public sealed class SubscriptionOptions {
    /// <summary>
    /// Record filter. Use <see cref="RecordFilter"/> factory methods
    /// (e.g., <c>RecordFilter.ByStream("orders")</c>) or construct an <see cref="ExpressionFilter"/>
    /// directly for SQL-style filtering. Defaults to <see cref="RecordFilter.ExcludeSystemRecords"/>;
    /// <see cref="RecordFilter.None"/> reads the raw log.
    /// </summary>
    public RecordFilter Filter { get; set; } = RecordFilter.ExcludeSystemRecords;

    /// <summary>
    /// Where to start if no checkpoint exists. Default: <see cref="SubscriptionInitialPosition.Latest"/>.
    /// </summary>
    public SubscriptionInitialPosition InitialPosition { get; set; } = SubscriptionInitialPosition.Latest;

    /// <summary>
    /// Path to extract partition key. Required when parallelism > 1.
    /// Reserved for partitioned processing — not yet exposed as a public knob.
    /// </summary>
    /// <remarks>
    ///
    /// Three syntaxes supported, all DuckDB compatible:<br />
    /// 
    /// **Bracket Notation** (preferred for keys with special characters):<br />
    /// ```sql
    ///     metadata['userId'] = 'user-123'
    /// metadata['order.total'] > 100
    /// ```
    /// 
    /// **Dot Notation** (convenient for simple keys):<br />
    /// ```sql
    ///     metadata.userId = 'user-123'
    /// metadata.amount > 100
    /// ```
    /// 
    /// **Arrow Text Notation** (`->>`, extracts as text):<br />
    /// ```sql
    ///     metadata->>'userId' = 'user-123'
    /// metadata->>'amount' > 100
    /// ```
    /// </remarks>
    internal string PartitionKeySelector { get; set; } = "";

    /// <summary>
    /// Overrides the stored checkpoint and forces the subscription to start from this log position.
    /// <see cref="LogPosition.Unset"/> (the default) means no override — resume from the checkpoint.
    /// </summary>
    internal LogPosition StartPosition { get; set; } = LogPosition.Unset;
}