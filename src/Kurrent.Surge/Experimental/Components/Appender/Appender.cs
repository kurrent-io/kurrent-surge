using Kurrent.Surge.Client;
using Kurrent.Surge.Processors.Partitioning;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// High-throughput buffered producer that serializes records on the caller's thread
/// and flushes them in batches via the <c>AppendRecords</c> unary RPC.
///
/// Batches are flushed when any threshold is reached: <see cref="AppenderOptions.MaxBatchCount"/>,
/// <see cref="AppenderOptions.MaxBatchBytes"/>, or <see cref="AppenderOptions.LingerTimeout"/>.
///
/// <para>Two append styles:</para>
/// <list type="bullet">
///   <item><see cref="Append"/> — callback-based, the callback is invoked when the batch
///     containing the record is acknowledged (or fails).</item>
///   <item><see cref="AppendAsync"/> — awaitable, completes when the server
///     acknowledges the batch containing the record.</item>
/// </list>
///
/// <para>Ordering:</para>
/// Records without an ordering key are appended in the order received (single default lane).
/// Records with an ordering key are routed to a lane determined by the key hash — records
/// with the same key are ordered, different keys may flush in parallel.
/// Set the ordering key via <c>AppendRecord.Create(value, r =&gt; r.OrderingKey("key"))</c>.
/// </summary>
sealed class Appender : IAppender {
    readonly KurrentClient           _client;
    readonly StreamsClient           _streams;
    readonly FlushLane               _defaultLane;
    readonly FlushLane[]             _keyedLanes;
    readonly CancellationTokenSource _stopping = new();

    public Appender(AppenderOptions options) {
        options.EnsureValid();
        
        _client      = KurrentClient.Create(options.ClientOptions!);
        _streams     = new StreamsClient(_client);
        _defaultLane = new FlushLane(options, _streams, _stopping.Token);
        _keyedLanes  = new FlushLane[options.Concurrency];
        
        for (var i = 0; i < options.Concurrency; i++)
            _keyedLanes[i] = new FlushLane(options, _streams, _stopping.Token);
    }

    /// <summary>
    /// Serializes the record on the caller's thread and enqueues it for batched append.
    /// The callback is invoked when the batch containing this record is acknowledged (or fails).
    /// </summary>
    public async ValueTask Append(AppendRecord record, Action<AppendResult> callback, CancellationToken ct = default) {
        record.EnsureValid();

        var serialized = await _streams.WritePipeline
            .Execute(record, ct)
            .ConfigureAwait(false);

        var lane    = SelectLane(record);
        var pending = PendingRecord.ForRecord(serialized, serialized.CalculateSize(), callback, asyncCallback: null);

        if (!lane.TryEnqueue(in pending))
            await lane.EnqueueAsync(pending, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes the record on the caller's thread and enqueues it for batched append.
    /// The asynchronous callback is awaited by the flush loop when the batch containing
    /// this record is acknowledged (or fails).
    /// </summary>
    public async ValueTask Append(AppendRecord record, Func<AppendResult, ValueTask> callback, CancellationToken ct = default) {
        record.EnsureValid();

        var serialized = await _streams.WritePipeline
            .Execute(record, ct)
            .ConfigureAwait(false);

        var lane    = SelectLane(record);
        var pending = PendingRecord.ForRecord(serialized, serialized.CalculateSize(), callback: null, callback);

        if (!lane.TryEnqueue(in pending))
            await lane.EnqueueAsync(pending, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes the record on the caller's thread and enqueues it. Returns a
    /// <see cref="ValueTask{T}"/> that completes when the batch containing this record
    /// is acknowledged by the server.
    /// </summary>
    public async ValueTask<AppendResult> AppendAsync(AppendRecord record, CancellationToken ct = default) {
        record.EnsureValid();

        var serialized = await _streams.WritePipeline
            .Execute(record, ct)
            .ConfigureAwait(false);

        var lane    = SelectLane(record);
        var source  = AppendCompletionSource.Rent(ct);
        var pending = PendingRecord.ForRecord(serialized, serialized.CalculateSize(), source.OnResult, asyncCallback: null);

        if (!lane.TryEnqueue(in pending))
            await lane.EnqueueAsync(pending, ct).ConfigureAwait(false);

        return await new ValueTask<AppendResult>(source, source.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Flushes all buffered records across all lanes, blocking until all pending batches are sent.
    /// Returns the number of records flushed by this call and the number still buffered or in flight afterwards.
    /// </summary>
    public async ValueTask<(int Flushed, int Inflight)> FlushAsync(CancellationToken ct = default) {
        var flushes = new ValueTask<(int Flushed, int Inflight)>[_keyedLanes.Length + 1];

        flushes[0] = _defaultLane.FlushAsync(ct);

        for (var i = 0; i < _keyedLanes.Length; i++)
            flushes[i + 1] = _keyedLanes[i].FlushAsync(ct);

        var flushed  = 0;
        var inflight = 0;

        foreach (var flush in flushes) {
            var laneResult = await flush.ConfigureAwait(false);

            flushed  += laneResult.Flushed;
            inflight += laneResult.Inflight;
        }

        return (Flushed: flushed, Inflight: inflight);
    }

    /// <summary>
    /// Drains remaining records across all lanes, flushes final batches, and disposes
    /// the underlying client.
    /// </summary>
    public async ValueTask DisposeAsync() {
        await _defaultLane.DisposeAsync().ConfigureAwait(false);

        foreach (var lane in _keyedLanes)
            await lane.DisposeAsync().ConfigureAwait(false);

        await _stopping.CancelAsync().ConfigureAwait(false);
        _stopping.Dispose();

        await _client.DisposeAsync().ConfigureAwait(false);
    }
    
    FlushLane SelectLane(AppendRecord record) {
        var orderingKey = record.GetOrderingKey();

        if (orderingKey is null)
            return _defaultLane;

        var index = PartitionCalculator.XXHash.Calculate(orderingKey, _keyedLanes.Length);

        return _keyedLanes[index];
    }
}
