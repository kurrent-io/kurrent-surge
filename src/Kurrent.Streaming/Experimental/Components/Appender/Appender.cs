using Kurrent.Client;
using Kurrent.Client.Processors.Partitioning;
using Kurrent.Client.Streams;

namespace Kurrent.Streaming.Experimental.Components.Appender;

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
///   <item><see cref="AppendAsync"/> — awaitable, completes when the batch containing the record
///     is acknowledged by the server.</item>
/// </list>
///
/// <para>Ordering:</para>
/// Records without an ordering key are appended in the order received (single default lane).
/// Records with an ordering key are routed to a lane determined by the key hash — records
/// with the same key are ordered, different keys may flush in parallel.
/// Set the ordering key via <c>AppendRecord.New().OrderingKey("key")</c>.
/// </summary>
internal sealed class Appender : IAppender {
    readonly AppenderOptions         _options;
    readonly KurrentClient           _client;
    readonly StreamsClient           _streams;
    readonly FlushLane               _defaultLane;
    readonly FlushLane[]             _keyedLanes;
    readonly CancellationTokenSource _stopping = new();

    public Appender(AppenderOptions options) {
        options.EnsureValid();
        _options = options;

        _client  = KurrentClient.Create(options.ClientOptions!);
        _streams = _client.Streams;

        //TODO SS: could the default lane be 0 if we simply start the keyed lanes on 1? :)
        
        _defaultLane = new FlushLane(options, _streams, _stopping.Token);

        _keyedLanes = new FlushLane[options.Concurrency];
        for (var i = 0; i < options.Concurrency; i++)
            _keyedLanes[i] = new FlushLane(options, _streams, _stopping.Token);
    }

    /// <summary>
    /// Serializes the record on the caller's thread and enqueues it for batched append.
    /// The callback is invoked when the batch containing this record is acknowledged (or fails).
    /// </summary>
    public async ValueTask Append(AppendRecord record, Action<AppendResult> callback, CancellationToken ct = default) {
        record.EnsureValid();

        var ctx = _streams.CreateMappingContext(ct);

        var serialized = await record
            .ToContract(ctx)
            .ConfigureAwait(false);

        var lane = SelectLane(record);

        if (!lane.TryEnqueue(new SerializedRecord { Serialized = serialized, Callback = callback }))
            callback(AppendResult.Dropped());
    }

    /// <summary>
    /// Serializes the record on the caller's thread and enqueues it. Returns a
    /// <see cref="ValueTask{T}"/> that completes when the batch containing this record
    /// is acknowledged by the server.
    /// </summary>
    public async ValueTask<AppendResult> AppendAsync(AppendRecord record, CancellationToken ct = default) {
        record.EnsureValid();

        var ctx = _streams.CreateMappingContext(ct);

        var serialized = await record
            .ToContract(ctx)
            .ConfigureAwait(false);

        var tcs  = new TaskCompletionSource<AppendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lane = SelectLane(record);

        Action<AppendResult> callback;

        if (ct.CanBeCanceled) {
            var registration = ct.Register(() => tcs.TrySetCanceled(ct));

            callback = result => {
                registration.Dispose();

                if (result.IsSuccess)
                    tcs.TrySetResult(result);
                else
                    tcs.TrySetException(result.Error!);
            };
        }
        else {
            callback = result => {
                if (result.IsSuccess)
                    tcs.TrySetResult(result);
                else
                    tcs.TrySetException(result.Error!);
            };
        }

        if (!lane.TryEnqueue(new SerializedRecord { Serialized = serialized, Callback = callback }))
            return AppendResult.Dropped();

        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Flushes all buffered records across all lanes, blocking until all pending batches are sent.
    /// </summary>
    public async ValueTask FlushAsync(CancellationToken ct = default) {
        var tasks = new ValueTask[_keyedLanes.Length + 1];
        tasks[0] = _defaultLane.FlushAsync(ct);

        for (var i = 0; i < _keyedLanes.Length; i++)
            tasks[i + 1] = _keyedLanes[i].FlushAsync(ct);

        foreach (var task in tasks)
            await task.ConfigureAwait(false);
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

    #region ->> Lane Selection <<-

    FlushLane SelectLane(AppendRecord record) {
        var orderingKey = record.GetOrderingKey();

        if (orderingKey is null)
            return _defaultLane;

        var index = PartitionCalculator.XXHash.Calculate(orderingKey, _keyedLanes.Length);

        return _keyedLanes[index];
    }

    #endregion
}
