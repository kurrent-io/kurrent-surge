using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Kurrent.Surge.Client;

static class ChannelExtensions {
    /// <param name="reader">The reader to read data from the channel.</param>
    /// <typeparam name="T">The type of data in the channel.</typeparam>
    extension<T>(ChannelReader<T> reader) {
        /// <summary>
        /// Reads data from the channel in batches of a specified size up to a given window of time.
        /// </summary>
        /// <param name="maxBatchSize">The maximum number of items in a single batch.</param>
        /// <param name="batchWindow">The maximum duration to wait while gathering a batch.</param>
        /// <param name="cancellationToken">A token to observe for cancellation of the batch processing.</param>
        public async IAsyncEnumerable<List<T>> ReadBatches(
            int maxBatchSize, TimeSpan batchWindow,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        ) {
            var buffer = new List<T>(maxBatchSize);

            while (!cancellationToken.IsCancellationRequested) {
                var timerTask = Task.Delay(batchWindow, cancellationToken);

                while (buffer.Count < maxBatchSize) {
                    var readSignal    = reader.WaitToReadAsync(cancellationToken).AsTask();
                    var completedTask = await Task.WhenAny(readSignal, timerTask).ConfigureAwait(false);

                    if (completedTask == readSignal && !readSignal.IsCanceled && await readSignal) {
                        while (buffer.Count < maxBatchSize && reader.TryRead(out var item))
                            buffer.Add(item);
                    }
                    else break;
                }

                if (buffer.Count > 0) {
                    yield return [..buffer];
                    buffer.Clear();
                }

                if (reader.Completion.IsCompleted) yield break;
            }
        }

        async IAsyncEnumerable<T[]> ReadBatchesOptimized(int maxBatchSize, TimeSpan batchWindow, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            var pool   = ArrayPool<T>.Shared;
            var buffer = pool.Rent(maxBatchSize);

            try {
                while (!cancellationToken.IsCancellationRequested) {
                    var currentIndex = 0;

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                    cts.CancelAfter(batchWindow);

                    while (currentIndex < maxBatchSize && !cts.Token.IsCancellationRequested) {
                        try {
                            if (await reader.WaitToReadAsync(cts.Token))
                                while (currentIndex < maxBatchSize && reader.TryRead(out var item))
                                    buffer[currentIndex++] = item;
                        }
                        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested) {
                            break; // Timeout occurred
                        }
                    }

                    if (currentIndex > 0) {
                        var result = new T[currentIndex];
                        Array.Copy(buffer, 0, result, 0, currentIndex);
                        yield return result;
                    }

                    if (reader.Completion.IsCompleted)
                        yield break;
                }
            }
            finally {
                pool.Return(buffer, clearArray: true);
            }
        }
    }
}
