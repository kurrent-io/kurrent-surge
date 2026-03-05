// using System.Buffers;
// using System.Threading.Channels;
// using Kurrent.Variant;
//
// namespace Kurrent.Client.Streams;
//
// /// <summary>
// /// Provides efficient batched reading of records.
// /// Batches records by size and timeout, flushing immediately on heartbeats.
// /// </summary>
// [PublicAPI]
// public record RecordBatchStream : IAsyncEnumerable<ReadRecordBatchResult>, IAsyncDisposable {
//     readonly Lazy<Channel<ReadRecordsResult>> _lazyChannel;
//
//     readonly int      _batchSize;
//     readonly TimeSpan _timeout;
//
//     long _totalRecordsRead;
//     long _totalBatchesRead;
//     int  _disposed;
//     int  _enumeratorCreated;
//
//     internal RecordBatchStream(Func<Channel<ReadRecordsResult>> channelFactory, int batchSize, TimeSpan timeout) {
//         _lazyChannel = new Lazy<Channel<ReadRecordsResult>>(channelFactory);
//         _batchSize   = batchSize;
//         _timeout     = timeout;
//     }
//
//     internal RecordBatchStream(Func<Channel<ReadRecordsResult>> channelFactory, int batchSize, long timeout)
//         : this(channelFactory, batchSize, TimeSpan.FromMilliseconds(timeout)) { }
//
//     Channel<ReadRecordsResult> Channel => _lazyChannel.Value;
//
//     public long TotalRecordsRead => Interlocked.Read(ref _totalRecordsRead);
//     public long TotalBatchesRead => Interlocked.Read(ref _totalBatchesRead);
//
//     /// <summary>
//     /// The number of items currently queued for processing.
//     /// </summary>
//     public int QueuedMessages => _lazyChannel.IsValueCreated ? Channel.Reader.Count : 0;
//
//     public virtual async ValueTask DisposeAsync() {
//         if (!_lazyChannel.IsValueCreated || Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
//             return;
//
//         Channel.Writer.TryComplete();
//
//         // wait for completion but don't propagate exceptions
//         // errors should surface through enumeration, not dispose
//         try {
//             await Channel.Reader.Completion.ConfigureAwait(false);
//         }
//         catch {
//             // swallow - exception was already propagated via GetAsyncEnumerator
//         }
//     }
//
//     // works but needs to be optimized.
//     public async IAsyncEnumerator<ReadRecordBatchResult> GetAsyncEnumerator(CancellationToken ct = default) {
//         ObjectDisposedException.ThrowIf(Interlocked.CompareExchange(ref _disposed, 0, 0) == 1, this);
//
//         if (Interlocked.CompareExchange(ref _enumeratorCreated, 1, 0) == 1)
//             throw new InvalidOperationException("Records already enumerated.");
//
//         if (ct.IsCancellationRequested)
//             yield break;
//
//         var reader = Channel.Reader;
//         var buffer = ArrayPool<Record>.Shared.Rent(_batchSize);
//         var count  = 0;
//
//         try {
//             while (!ct.IsCancellationRequested) {
//                 var start = TimeProvider.System.GetTimestamp();
//
//                 while (count < _batchSize) {
//                     var remainingTime = _timeout - TimeProvider.System.GetElapsedTime(start);
//                     if (remainingTime <= TimeSpan.Zero) break;
//
//                     var waitTask = reader.WaitToReadAsync(ct);
//
//                     bool hasData;
//                     if (waitTask.IsCompletedSuccessfully)
//                         hasData = waitTask.Result;
//                     else
//                         try {
//                             hasData = await waitTask.AsTask()
//                                 .WaitAsync(remainingTime, ct)
//                                 .ConfigureAwait(false);
//                         }
//                         catch (TimeoutException) {
//                             break;
//                         }
//                         catch (OperationCanceledException) {
//                             yield break;
//                         }
//
//                     if (!hasData) {
//                         // channel completed - yield remaining
//                         if (count > 0) {
//                             yield return buffer.CopyToArray(count);
//                             Interlocked.Increment(ref _totalBatchesRead);
//                             Interlocked.Add(ref _totalRecordsRead, count);
//                             // count =  0;
//                         }
//
//                         // propagate any exception from channel completion
//                         try {
//                             await reader.Completion.ConfigureAwait(false);
//                         }
//                         catch (OperationCanceledException) {
//                             // ignore cancellation
//                         }
//
//                         yield break;
//                     }
//
//                     // drain available items
//                     while (count < _batchSize && reader.TryRead(out var message))
//                         if (message.IsHeartbeat) {
//                             // flush pending records, then yield heartbeat
//                             if (count > 0) {
//                                 yield return buffer.CopyToArray(count);
//                                 Interlocked.Increment(ref _totalBatchesRead);
//                                 Interlocked.Add(ref _totalRecordsRead, count);
//                                 count = 0;
//                             }
//
//                             yield return message.AsHeartbeat;
//                         }
//                         else
//                             buffer[count++] = message.AsRecord;
//                 }
//
//                 // yield batch on size/timeout
//                 if (count > 0) {
//                     yield return buffer.CopyToArray(count);
//                     Interlocked.Increment(ref _totalBatchesRead);
//                     Interlocked.Add(ref _totalRecordsRead, count);
//                     count =  0;
//                 }
//             }
//         }
//         finally {
//             ArrayPool<Record>.Shared.Return(buffer, true);
//         }
//     }
// }
//
// /// <summary>
// /// The result of a read records operation, which can be either a batch of <see cref="Record"/>s or a <see cref="Heartbeat"/>.
// /// </summary>
// public readonly partial record struct ReadRecordBatchResult : IVariant<Record[], Heartbeat>;
//
// // public readonly partial record struct ReadRecordBatchResult : IVariant<RecordBatch, Heartbeat> {
// //     public static implicit operator ReadRecordBatchResult(Record[] batch) => (RecordBatch)batch;
// //
// //     // TODO SS: if we could change the name of the type we could remove the RecordBatch object and this operator, avoiding creating an extra object. Then again, RecordBatch adds IReadOnlyList semantics which is nice.
// // }
// //
// // /// <summary>
// // /// A batch of records read from a stream.
// // /// </summary>
// // public record RecordBatch : IReadOnlyList<Record> {
// //     public static readonly RecordBatch Empty = new() { Records = [] };
// //
// //     RecordBatch() => Records = Empty.Records;
// //
// //     Record[] Records { get; init; }
// //
// //     #region . IReadOnlyList .
// //
// //     public IEnumerator<Record> GetEnumerator() => Records.AsEnumerable().GetEnumerator();
// //
// //     IEnumerator IEnumerable.GetEnumerator() => Records.GetEnumerator();
// //
// //     public int Count => Records.Length;
// //
// //     public Record this[int index] => Records[index];
// //
// //     #endregion
// //
// //     public static implicit operator RecordBatch(Record[] records) => new() { Records = records };
// //     public static implicit operator Record[](RecordBatch batch)   => batch.Records;
// // }
