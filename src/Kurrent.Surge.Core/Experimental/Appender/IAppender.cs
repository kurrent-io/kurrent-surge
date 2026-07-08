using Kurrent.Client.Streams;

namespace Kurrent.Appender;

interface IAppender : IAsyncDisposable {
    /// <summary>
    /// Serializes the record on the caller's thread and enqueues it for batched append.
    /// The callback is invoked when the batch containing this record is acknowledged (or fails).
    /// </summary>
    ValueTask Append(AppendRecord record, Action<AppendResult> callback, CancellationToken ct = default);

    /// <summary>
    /// Serializes the record on the caller's thread and enqueues it. Returns a
    /// <see cref="ValueTask{T}"/> that completes when the batch containing this record
    /// is acknowledged by the server.
    /// </summary>
    ValueTask<AppendResult> AppendAsync(AppendRecord record, CancellationToken ct = default);

    /// <summary>
    /// Flushes all buffered records across all lanes, blocking until all pending batches are sent.
    /// </summary>
    ValueTask FlushAsync(CancellationToken ct = default);
}
