using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Experimental;

interface IAppender : IAsyncDisposable {
    ValueTask Append(AppendRecord record, Action<AppendResult> callback, CancellationToken ct = default);

    ValueTask Append(AppendRecord record, Func<AppendResult, ValueTask> callback, CancellationToken ct = default);

    ValueTask<AppendResult> AppendAsync(AppendRecord record, CancellationToken ct = default);

    /// <summary>
    /// Flushes all buffered records across all lanes, blocking until all pending batches are sent.
    /// Returns the number of records flushed by this call and the number still buffered or in flight afterwards.
    /// </summary>
    ValueTask<(int Flushed, int Inflight)> FlushAsync(CancellationToken ct = default);
}
