using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Kurrent.Testkit.Datasets.HomeAutomation;
using Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;

namespace Benchmarks;

/// <summary>
/// Benchmarks comparing different implementations of ReadBatches for async enumerable batching.
/// Tests throughput with sync fast path (items yield immediately).
/// </summary>
[Config(typeof(BenchmarkConfig))]
public class ReadBatchesBenchmarks {
    [Params(100, 250, 500, 1000)]
    public int BatchSize { get; set; }

    [Params(1000, 5000)]
    public int TotalItems { get; set; }

    static TimeSpan DefaultTimeout => TimeSpan.FromMilliseconds(100);

    List<IHomeAutomationEvent> _events = null!;

    [GlobalSetup]
    public void Setup() =>
        _events = HomeAutomationDataSet.Default.Events().Take(50_000).ToList();

    // Fast path - sync completion (throughput benchmarks)
    IAsyncEnumerable<IHomeAutomationEvent> CreateFastSource() =>
        _events.Take(TotalItems).ToAsyncEnumerable();

    [Benchmark(Baseline = true)]
    public async Task<int> Current_ArrayPoolCollectionsMarshal() {
        var count = 0;
        await foreach (var batch in CreateFastSource().ReadBatches_Current(BatchSize, DefaultTimeout))
            count += batch.Count;
        return count;
    }

    [Benchmark]
    public async Task<int> SimpleListAdd() {
        var count = 0;
        await foreach (var batch in CreateFastSource().ReadBatches_SimpleListAdd(BatchSize, DefaultTimeout))
            count += batch.Count;
        return count;
    }

    [Benchmark]
    public async Task<int> CtsTimeout() {
        var count = 0;
        await foreach (var batch in CreateFastSource().ReadBatches_CtsTimeout(BatchSize, DefaultTimeout))
            count += batch.Count;
        return count;
    }

    [Benchmark]
    public async Task<int> SpreadSyntax() {
        var count = 0;
        await foreach (var batch in CreateFastSource().ReadBatches_SpreadSyntax(BatchSize, DefaultTimeout))
            count += batch.Count;
        return count;
    }
}

// /// <summary>
// /// Benchmarks comparing timeout mechanisms when timeouts actually trigger.
// /// Uses a slow source with delays to force timeout behavior.
// /// </summary>
// [Config(typeof(BenchmarkConfig))]
// public class ReadBatchesTimeoutBenchmarks {
//     [Params(100, 250, 500, 1000)]
//     public int BatchSize { get; set; }
//
//     [Params(1000, 5000, 10000, 50000)]
//     public int TotalItems { get; set; }
//
//     [Params(10, 50)]
//     public int TimeoutMs { get; set; }
//
//     [Params(20)] // Delay > timeout triggers early batch yield
//     public int ItemDelayMs { get; set; }
//
//     TimeSpan Timeout => TimeSpan.FromMilliseconds(TimeoutMs);
//
//     List<IHomeAutomationEvent> _events = null!;
//
//     [GlobalSetup]
//     public void Setup() =>
//         _events = HomeAutomationDataSet.Default.Events().Take(50_000).ToList();
//
//     // Slow path - async delays (timeout trigger benchmarks)
//     async IAsyncEnumerable<IHomeAutomationEvent> CreateSlowSource() {
//         foreach (var homeAutomationEvent in _events.Take(TotalItems)) {
//             await Task.Delay(ItemDelayMs).ConfigureAwait(false);
//             yield return homeAutomationEvent;
//         }
//     }
//
//     [Benchmark(Baseline = true)]
//     public async Task<int> WaitAsync_Timeout() {
//         var count = 0;
//         await foreach (var batch in CreateSlowSource().ReadBatches_Current(BatchSize, Timeout))
//             count += batch.Count;
//         return count;
//     }
//
//     [Benchmark]
//     public async Task<int> CtsTimeout_Trigger() {
//         var count = 0;
//         await foreach (var batch in CreateSlowSource().ReadBatches_CtsTimeout(BatchSize, Timeout))
//             count += batch.Count;
//         return count;
//     }
// }

// =============================================================================
// Implementation Variants
// =============================================================================

static class ReadBatchesImplementations {
    // -------------------------------------------------------------------------
    // 1. Current Implementation (Baseline)
    //    ArrayPool + CollectionsMarshal.SetCount + WaitAsync timeout
    // -------------------------------------------------------------------------
    public static async IAsyncEnumerable<List<T>> ReadBatches_Current<T>(
        this IAsyncEnumerable<T> source,
        int batchSize,
        TimeSpan timeout,
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
                            yield return ToListCollectionsMarshal(buffer, count);

                        yield break;
                    }

                    buffer[count++] = enumerator.Current;
                }

                if (count > 0)
                    yield return ToListCollectionsMarshal(buffer, count);
            }
        }
        finally {
            // only clear if T can hold references (avoids clearing value types)
            pool.Return(buffer, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }

        yield break;

        // Creates a List from a buffer segment using CollectionsMarshal for efficient bulk copy.
        static List<T> ToListCollectionsMarshal(T[] buffer, int count) {
            var list = new List<T>(count);
            CollectionsMarshal.SetCount(list, count);
            buffer.AsSpan(0, count).CopyTo(CollectionsMarshal.AsSpan(list));
            return list;
        }
    }

    // -------------------------------------------------------------------------
    // 2. Simple List.Add Implementation
    //    No ArrayPool, direct List.Add() calls
    // -------------------------------------------------------------------------
    public static async IAsyncEnumerable<List<T>> ReadBatches_SimpleListAdd<T>(
        this IAsyncEnumerable<T> source,
        int batchSize,
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    ) {
        await using var enumerator = source.GetAsyncEnumerator(cancellationToken);

        while (!cancellationToken.IsCancellationRequested) {
            var batch = new List<T>(batchSize);
            var start = TimeProvider.System.GetTimestamp();

            while (batch.Count < batchSize) {
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
                    if (batch.Count > 0)
                        yield return batch;

                    yield break;
                }

                batch.Add(enumerator.Current);
            }

            if (batch.Count > 0)
                yield return batch;
        }
    }

    // -------------------------------------------------------------------------
    // 3. CTS-based Timeout Implementation
    //    ArrayPool + CollectionsMarshal, uses CancellationTokenSource.CancelAfter()
    // -------------------------------------------------------------------------
    public static async IAsyncEnumerable<List<T>> ReadBatches_CtsTimeout<T>(
        this IAsyncEnumerable<T> source,
        int batchSize,
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    ) {
        var pool   = ArrayPool<T>.Shared;
        var buffer = pool.Rent(batchSize);

        try {
            await using var enumerator = source.GetAsyncEnumerator(cancellationToken);

            while (!cancellationToken.IsCancellationRequested) {
                var count = 0;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);

                while (count < batchSize) {
                    bool hasNext;

                    try {
                        var moveNext = enumerator.MoveNextAsync();

                        // fast path: synchronous completion avoids task allocation
                        if (moveNext.IsCompletedSuccessfully)
                            hasNext = moveNext.Result;
                        else {
                            // slow path: wait with CTS timeout
                            hasNext = await moveNext.AsTask()
                                .WaitAsync(timeoutCts.Token)
                                .ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
                        break; // batch timeout, yield what we have
                    }

                    if (!hasNext) {
                        // source exhausted
                        if (count > 0)
                            yield return ToListCollectionsMarshal(buffer, count);

                        yield break;
                    }

                    buffer[count++] = enumerator.Current;
                }

                if (count > 0)
                    yield return ToListCollectionsMarshal(buffer, count);
            }
        }
        finally {
            // only clear if T can hold references (avoids clearing value types)
            pool.Return(buffer, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }

        yield break;

        // Creates a List from a buffer segment using CollectionsMarshal for efficient bulk copy.
        static List<T> ToListCollectionsMarshal(T[] buffer, int count) {
            var list = new List<T>(count);
            CollectionsMarshal.SetCount(list, count);
            buffer.AsSpan(0, count).CopyTo(CollectionsMarshal.AsSpan(list));
            return list;
        }
    }

    // -------------------------------------------------------------------------
    // 4. Spread Syntax Implementation
    //    ArrayPool + [..span] collection expression instead of CollectionsMarshal
    // -------------------------------------------------------------------------
    public static async IAsyncEnumerable<List<T>> ReadBatches_SpreadSyntax<T>(
        this IAsyncEnumerable<T> source,
        int batchSize,
        TimeSpan timeout,
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
                            yield return [..buffer.AsSpan(0, count)];

                        yield break;
                    }

                    buffer[count++] = enumerator.Current;
                }

                if (count > 0)
                    yield return [..buffer.AsSpan(0, count)];
            }
        }
        finally {
            // only clear if T can hold references (avoids clearing value types)
            pool.Return(buffer, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }
}
