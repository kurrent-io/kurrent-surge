using System.Threading.Channels;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams;
using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Appender;

/// <summary>
/// A single flush lane — bounded channel + background flush loop. Records enqueued
/// into the same lane are flushed sequentially, preserving ordering within the lane.
/// Records arrive pre-serialized; the flush loop only frames and sends.
/// </summary>
sealed class FlushLane : IAsyncDisposable {
    readonly Channel<IPendingRecord> _channel;
    readonly AppenderOptions         _options;
    readonly StreamsClient           _streams;
    readonly Task?                   _flushLoop;

    public FlushLane(AppenderOptions options, StreamsClient streams, CancellationToken ct) {
        _options = options;
        _streams = streams;

        _channel = Channel.CreateBounded<IPendingRecord>(
            new BoundedChannelOptions(options.BufferCapacity) {
                SingleReader                  = true,
                SingleWriter                  = false,
                AllowSynchronousContinuations = false,
                FullMode                      = options.FullMode
            });

        _flushLoop = Task.Run(() => RunFlushLoopAsync(ct), ct);
    }

    public bool TryEnqueue(SerializedRecord record) =>
        _channel.Writer.TryWrite(record);

    public async ValueTask FlushAsync(CancellationToken ct) {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (ct.CanBeCanceled)
            ct.Register(() => tcs.TrySetCanceled(ct));

        await _channel.Writer.WriteAsync(
            new FlushSignal { Completion = tcs }, ct).ConfigureAwait(false);

        await tcs.Task.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() {
        _channel.Writer.TryComplete();

        if (_flushLoop is not null) {
            try {
                await _flushLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // Expected: flush loop exits when channel completes.
            }
        }
    }

    #region ->> Flush Loop <<-

    async Task RunFlushLoopAsync(CancellationToken ct) {
        var batch      = new List<SerializedRecord>(_options.MaxBatchCount);
        var batchBytes = 0;

        await foreach (var message in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
            switch (message) {
                case FlushSignal signal:
                    if (batch.Count > 0)
                        await FlushBatchAsync(batch, ct).ConfigureAwait(false);

                    batchBytes = 0;
                    signal.Completion.TrySetResult();
                    continue;

                case SerializedRecord first:
                    batch.Add(first);
                    batchBytes += first.Serialized.CalculateSize();
                    break;
            }

            // Eagerly drain everything already queued without blocking
            while (!BatchFull(batch, batchBytes) && _channel.Reader.TryRead(out var next))
                switch (next) {
                    case FlushSignal signal:
                        if (batch.Count > 0)
                            await FlushBatchAsync(batch, ct).ConfigureAwait(false);

                        batchBytes = 0;
                        signal.Completion.TrySetResult();
                        continue;

                    case SerializedRecord record:
                        batch.Add(record);
                        batchBytes += record.Serialized.CalculateSize();
                        break;
                }

            // If the batch isn't full yet and the channel is empty, wait up to LingerTimeout
            // for more records to arrive before flushing a partial batch.
            if (!BatchFull(batch, batchBytes)) {
                using var linger = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linger.CancelAfter(_options.LingerTimeout);

                try {
                    while (!BatchFull(batch, batchBytes)) {
                        var next = await _channel.Reader.ReadAsync(linger.Token).ConfigureAwait(false);

                        switch (next) {
                            case FlushSignal signal:
                                if (batch.Count > 0)
                                    await FlushBatchAsync(batch, ct).ConfigureAwait(false);

                                batchBytes = 0;
                                signal.Completion.TrySetResult();
                                continue;

                            case SerializedRecord record:
                                batch.Add(record);
                                batchBytes += record.Serialized.CalculateSize();
                                break;
                        }
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                    // Linger timeout expired — flush the partial batch.
                }
                catch (ChannelClosedException) {
                    // Writer completed (DisposeAsync) while waiting for more records.
                }
            }

            if (batch.Count > 0)
                await FlushBatchAsync(batch, ct).ConfigureAwait(false);

            batchBytes = 0;
        }
    }

    bool BatchFull(List<SerializedRecord> batch, int batchBytes) =>
        batch.Count >= _options.MaxBatchCount || batchBytes >= _options.MaxBatchBytes;

    async ValueTask FlushBatchAsync(List<SerializedRecord> batch, CancellationToken ct) {
        try {
            var request = new Contracts.AppendRecordsRequest {
                Records = { Capacity = batch.Count }
            };

            foreach (var record in batch)
                request.Records.Add(record.Serialized);

            var rsp = await _streams.ServiceClient
                .AppendRecordsAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            var result = new AppendResult(rsp.ToResponse());

            foreach (var record in batch)
                record.Callback(result);
        }
        catch (Exception ex) {
            var mapped = ex.AsWellKnownException(
                AppendConsistencyViolationException.From,
                MaxAppendRecordSizeExceededException.From,
                MaxAppendTransactionSizeExceededException.From);

            _options.OnError?.Invoke(new AppendError(mapped));

            foreach (var record in batch)
                record.Callback(AppendResult.Failed(mapped));
        }
        finally {
            batch.Clear();
        }
    }

    #endregion
}
