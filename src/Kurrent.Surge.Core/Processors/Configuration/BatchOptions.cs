namespace Kurrent.Surge.Processors.Configuration;

/// <summary>
/// Batch-read tuning. The drone reads the channel in batches of up to <see cref="MaxBatchSize"/>,
/// flushing early when <see cref="BatchWindow"/> elapses or a lifecycle marker arrives. Batch
/// processors receive the whole batch; record processors receive each record in turn. Purely a
/// read/throughput concern — it does not change delivery guarantees.
/// </summary>
[PublicAPI]
public sealed class BatchOptions {
    /// <summary>
    /// Maximum records per read batch. Default: 500. Range: 1–10,000. A record processor still sees
    /// one record at a time; this only sets how many are read per channel pull.
    /// </summary>
    public int MaxBatchSize { get; set; } = 500;

    /// <summary>
    /// Maximum time to gather a batch before flushing a partial one. Default: 100 ms. Bounds dispatch
    /// latency on sparse or live streams, where the size threshold is rarely reached.
    /// </summary>
    public TimeSpan BatchWindow { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Channel buffer capacity expressed as a multiple of <see cref="MaxBatchSize"/>. Default: 2, so the
    /// reader can fill one batch while another is in flight. Channel capacity is
    /// <see cref="MaxBatchSize"/> × <see cref="BufferMultiplier"/>. Range: 1–100. Keeping it at least 1
    /// guarantees the size threshold is always reachable.
    /// </summary>
    public int BufferMultiplier { get; set; } = 2;
}
