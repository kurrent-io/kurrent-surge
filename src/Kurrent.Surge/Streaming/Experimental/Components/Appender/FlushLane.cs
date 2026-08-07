using System.Runtime.InteropServices;
using System.Threading.Channels;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams;
using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// A single flush lane — bounded channel + background flush loop. Records enqueued
/// into the same lane are flushed sequentially, preserving ordering within the lane.
/// Records arrive pre-serialized; the flush loop only frames and sends.
/// </summary>
sealed class FlushLane : IAsyncDisposable {
    // Producers hit _pending with an Interlocked per record while the flush loop writes
    // _flushed per batch — separate cache lines so the counters never ping-pong across cores.
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    struct PaddedCounter {
        [FieldOffset(64)] public long Value;
    }

    readonly Channel<PendingRecord> _channel;
    readonly AppenderOptions        _options;
    readonly StreamsClient          _streams;
    readonly Task?                  _flushLoop;

    PaddedCounter _flushed;
    PaddedCounter _pending;

    public FlushLane(AppenderOptions options, StreamsClient streams, CancellationToken ct) {
        _options = options;
        _streams = streams;

        _channel = Channel.CreateBounded<PendingRecord>(
            new BoundedChannelOptions(options.BufferCapacity) {
                SingleReader                  = true,
                SingleWriter                  = false,
                AllowSynchronousContinuations = false,
                FullMode                      = options.FullMode
            });

        _flushLoop = RunFlushLoopAsync(ct);
    }

    public bool TryEnqueue(in PendingRecord record) {
        if (!_channel.Writer.TryWrite(record))
            return false;

        Interlocked.Increment(ref _pending.Value);
        return true;
    }

    /// <summary>
    /// Backpressure path for <see cref="BoundedChannelFullMode.Wait"/> — waits for channel
    /// capacity instead of dropping when <see cref="TryEnqueue"/> finds the buffer full.
    /// </summary>
    public async ValueTask EnqueueAsync(PendingRecord record, CancellationToken ct) {
        await _channel.Writer.WriteAsync(record, ct).ConfigureAwait(false);
        Interlocked.Increment(ref _pending.Value);
    }

    public async ValueTask<(int Flushed, int Inflight)> FlushAsync(CancellationToken ct) {
        var flushedBefore = Interlocked.Read(ref _flushed.Value);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var registration = ct.CanBeCanceled
            ? ct.Register(() => tcs.TrySetCanceled(ct))
            : default;

        try {
            await _channel.Writer.WriteAsync(PendingRecord.ForFlush(tcs), ct).ConfigureAwait(false);
            await tcs.Task.ConfigureAwait(false);
        }
        finally {
            registration.Dispose();
        }

        var flushed = (int)(Interlocked.Read(ref _flushed.Value) - flushedBefore);

        return (Flushed: flushed, Inflight: (int)Volatile.Read(ref _pending.Value));
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
        var batch      = new List<PendingRecord>(_options.MaxBatchCount);
        var batchBytes = 0;

        // Reused while the linger timer never fires; a fired timer poisons the source and
        // TryReset fails, so only the expired-linger cycle pays for a fresh one.
        CancellationTokenSource? linger = null;

        try {
            await foreach (var message in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
                if (message.FlushCompletion is { } signal) {
                    if (batch.Count > 0)
                        await FlushBatchAsync(batch, ct).ConfigureAwait(false);

                    batchBytes = 0;
                    signal.TrySetResult();
                    continue;
                }

                batch.Add(message);
                batchBytes += message.SizeBytes;

                // Eagerly drain everything already queued without blocking
                while (!BatchFull(batch, batchBytes) && _channel.Reader.TryRead(out var next)) {
                    if (next.FlushCompletion is { } drainedSignal) {
                        if (batch.Count > 0)
                            await FlushBatchAsync(batch, ct).ConfigureAwait(false);

                        batchBytes = 0;
                        drainedSignal.TrySetResult();
                        continue;
                    }

                    batch.Add(next);
                    batchBytes += next.SizeBytes;
                }

                // If the batch isn't full yet and the channel is empty, wait up to LingerTimeout
                // for more records to arrive before flushing a partial batch.
                if (!BatchFull(batch, batchBytes)) {
                    linger ??= CancellationTokenSource.CreateLinkedTokenSource(ct);
                    linger.CancelAfter(_options.LingerTimeout);

                    try {
                        while (!BatchFull(batch, batchBytes)) {
                            var next = await _channel.Reader.ReadAsync(linger.Token).ConfigureAwait(false);

                            if (next.FlushCompletion is { } lingerSignal) {
                                if (batch.Count > 0)
                                    await FlushBatchAsync(batch, ct).ConfigureAwait(false);

                                batchBytes = 0;
                                lingerSignal.TrySetResult();
                                continue;
                            }

                            batch.Add(next);
                            batchBytes += next.SizeBytes;
                        }
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                        // Linger timeout expired — flush the partial batch.
                    }
                    catch (ChannelClosedException) {
                        // Writer completed (DisposeAsync) while waiting for more records.
                    }
                    finally {
                        if (!linger.TryReset()) {
                            linger.Dispose();
                            linger = null;
                        }
                    }
                }

                if (batch.Count > 0)
                    await FlushBatchAsync(batch, ct).ConfigureAwait(false);

                batchBytes = 0;
            }
        }
        finally {
            linger?.Dispose();
        }
    }

    bool BatchFull(List<PendingRecord> batch, int batchBytes) =>
        batch.Count >= _options.MaxBatchCount || batchBytes >= _options.MaxBatchBytes;

    async ValueTask FlushBatchAsync(List<PendingRecord> batch, CancellationToken ct) {
        try {
            var request = new Contracts.AppendRecordsRequest {
                Records = { Capacity = batch.Count }
            };

            foreach (var record in batch)
                request.Records.Add(record.Serialized!);

            var rsp = await _streams.ServiceClient
                .AppendRecordsAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            var result = new AppendResult(rsp.ToResponse());

            foreach (var record in batch)
                await InvokeCallback(record, result).ConfigureAwait(false);
        }
        catch (Exception ex) {
            var mapped = ex.MapToWellKnownException(
                AppendConsistencyViolationException.Match,
                MaxAppendRecordSizeExceededException.Match,
                MaxAppendTransactionSizeExceededException.Match);

            _options.OnError?.Invoke(new AppendError(mapped));

            foreach (var record in batch)
                await InvokeCallback(record, AppendResult.Failed(mapped)).ConfigureAwait(false);
        }
        finally {
            Interlocked.Add(ref _flushed.Value, batch.Count);
            Interlocked.Add(ref _pending.Value, -batch.Count);
            batch.Clear();
        }
    }

    static ValueTask InvokeCallback(in PendingRecord record, AppendResult result) {
        if (record.Callback is not null) {
            record.Callback(result);
            return ValueTask.CompletedTask;
        }

        return record.AsyncCallback!(result);
    }

    #endregion // Flush Loop
}
