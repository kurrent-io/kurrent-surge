using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kurrent.Client;

static class AsyncEnumerableExtensions {
    /// <param name="source">The async enumerable to read data from.</param>
    /// <typeparam name="T">The type of data in the enumerable.</typeparam>
    extension<T>(IAsyncEnumerable<T> source) {
        /// <summary>
        /// Reads data from the async enumerable in batches of a specified size up to a given timeout period.
        /// Uses ArrayPool internally for efficient batch building, then creates a List via CollectionsMarshal.
        /// </summary>
        /// <param name="batchSize">The maximum number of items in a single batch.</param>
        /// <param name="timeout">The maximum duration to wait while gathering a batch.</param>
        /// <param name="cancellationToken">A token to observe for cancellation of the batch processing.</param>
        public async IAsyncEnumerable<List<T>> ReadBatches(
            int batchSize, TimeSpan timeout,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        ) {
            var pool   = ArrayPool<T>.Shared;
            var buffer = pool.Rent(batchSize);

            try {
                await using var enumerator = source.GetAsyncEnumerator(cancellationToken);

                while (!cancellationToken.IsCancellationRequested) {
                    var count = 0;
                    var start = TimeProvider.System.GetTimestamp();

                    while (count < batchSize) {
                        bool hasNext;

                        try {
                            var moveNext = enumerator.MoveNextAsync();

                            // fast path: synchronous completion avoids task allocation
                            if (moveNext.IsCompletedSuccessfully)
                                hasNext = moveNext.Result;
                            else {
                                // slow path: async with remaining timeout
                                var remaining = timeout - TimeProvider.System.GetElapsedTime(start);
                                if (remaining <= TimeSpan.Zero)
                                    break; // timeout expired

                                hasNext = await moveNext.AsTask()
                                    .WaitAsync(remaining, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                        }
                        catch (TimeoutException) {
                            break; // batch timeout, yield what we have
                        }

                        if (!hasNext) {
                            // source exhausted
                            if (count > 0)
                                yield return ToList(buffer, count);

                            yield break;
                        }

                        buffer[count++] = enumerator.Current;
                    }

                    if (count > 0)
                        yield return ToList(buffer, count);
                }
            }
            finally {
                // only clear if T can hold references (avoids clearing value types)
                pool.Return(buffer, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            }

            yield break;

            //// Creates a List from a buffer segment using CollectionsMarshal for efficient bulk copy.
            //// Avoids List.Add() overhead by directly setting count and copying via Span.
            static List<T> ToList(T[] buffer, int count) {
                var list = new List<T>(count);
                CollectionsMarshal.SetCount(list, count);
                buffer.AsSpan(0, count).CopyTo(CollectionsMarshal.AsSpan(list));
                return list;
            }
        }
    }
}
