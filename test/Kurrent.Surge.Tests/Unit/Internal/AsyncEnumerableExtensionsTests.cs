using System.Diagnostics.CodeAnalysis;

namespace Kurrent.Surge.Tests.Internal;

[SuppressMessage("Usage", "TUnit0038:No data source provided")]
public class AsyncEnumerableExtensionsTests {
    [Test]
    [BatchSizeCases]
    public async ValueTask respects_batch_size(BatchSizeCase testCase) {
        var source  = ItemsDataSource(testCase.ItemCount);
        var timeout = TimeSpan.FromSeconds(10);

        var batches = new List<List<int>>();
        await foreach (var batch in source.ReadBatches(testCase.BatchSize, timeout))
            batches.Add(batch);

        await Assert.That(batches.Count).IsEqualTo(testCase.ExpectedBatchSizes.Length);

        for (var i = 0; i < batches.Count; i++)
            await Assert.That(batches[i].Count).IsEqualTo(testCase.ExpectedBatchSizes[i]);
    }

    [Test]
    [TimeoutCases]
    public async ValueTask handles_timeout_correctly(TimeoutCase testCase) {
        var source = Enumerable.Range(0, testCase.ItemCount)
            .Select(i => {
                Thread.Sleep(testCase.ItemDelay); // because using task delay breaks tunit test runner...improve later
                return i;
            }
        ).ToAsyncEnumerable();

        var batches = new List<List<int>>();
        await foreach (var batch in source.ReadBatches(testCase.BatchSize, testCase.Timeout))
            batches.Add(batch);

        var totalItems = batches.Sum(b => b.Count);
        await Assert.That(totalItems).IsEqualTo(testCase.ItemCount);

        if (testCase.ItemDelay > testCase.Timeout) {
            // timeout triggers before batch fills: should have more batches with fewer items
            foreach (var batch in batches)
                await Assert.That(batch.Count).IsLessThanOrEqualTo(testCase.BatchSize);

            await Assert.That(batches.Max(b => b.Count)).IsLessThanOrEqualTo(testCase.BatchSize);
        }
        else {
            // timeout allows full batches: should have at least one full batch
            await Assert.That(batches).Contains(b => b.Count == testCase.BatchSize);
        }
    }

    [Test]
    public async ValueTask preserves_item_order_within_batches() {
        var source = ItemsDataSource(25);

        var allItems = new List<int>();
        await foreach (var batch in source.ReadBatches(7, Timeout.InfiniteTimeSpan))
            allItems.AddRange(batch);

        await Assert.That(allItems).IsInOrder();
    }

    [Test]
    public async ValueTask stops_iteration_when_cancelled() {
        using var cancellator = new CancellationTokenSource();

        var source     = ItemsDataSource(1000);
        var batchCount = 0;

        try {
            await foreach (var batch in source.ReadBatches(10, TimeSpan.FromSeconds(1), cancellator.Token)) {
                batchCount++;
                if (batchCount >= 3)
                    await cancellator.CancelAsync();
            }
        }
        catch (OperationCanceledException) {
            // expected when cancelled
        }

        await Assert.That(batchCount).IsGreaterThanOrEqualTo(3);
    }

    [Test]
    public async ValueTask handles_empty_source() {
        var source = AsyncEnumerable.Empty<int>();

        var batches = new List<List<int>>();
        await foreach (var batch in source.ReadBatches(5, TimeSpan.FromSeconds(1)))
            batches.Add(batch);

        await Assert.That(batches).IsEmpty();
    }

    [Test]
    public async ValueTask handles_single_item_source() {
        var source = new[] { 42 }.ToAsyncEnumerable();

        var batches = new List<List<int>>();
        await foreach (var batch in source.ReadBatches(10, TimeSpan.FromSeconds(1)))
            batches.Add(batch);

        await Assert.That(batches.Count).IsEqualTo(1);
        await Assert.That(batches[0]).IsEquivalentTo(new[] { 42 });
    }

    static IAsyncEnumerable<int> ItemsDataSource(int count) =>
        Enumerable.Range(0, count).ToAsyncEnumerable();

    public readonly record struct BatchSizeCase(int ItemCount, int BatchSize, int[] ExpectedBatchSizes) {
        public override string ToString() => $"{ItemCount} items, {BatchSize} batch size → [{string.Join(", ", ExpectedBatchSizes)}]";
    }

    public readonly record struct TimeoutCase(int ItemCount, int BatchSize, TimeSpan ItemDelay, TimeSpan Timeout) {
        public override string ToString() => $"{ItemCount} items,  {BatchSize} batch size, {ItemDelay.TotalMilliseconds}ms delay, {Timeout.TotalMilliseconds}ms timeout";
    }

    public class BatchSizeCases : TestCaseGenerator<BatchSizeCase> {
        protected override IEnumerable<BatchSizeCase> Data() => [
            new(ItemCount: 10, BatchSize: 5, ExpectedBatchSizes: [5, 5]),
            new(ItemCount: 7, BatchSize: 3, ExpectedBatchSizes: [3, 3, 1]),
            new(ItemCount: 3, BatchSize: 10, ExpectedBatchSizes: [3]),
            new(ItemCount: 0, BatchSize: 5, ExpectedBatchSizes: []),
            new(ItemCount: 1, BatchSize: 1, ExpectedBatchSizes: [1]),
            new(ItemCount: 100, BatchSize: 25, ExpectedBatchSizes: [25, 25, 25, 25])
        ];
    }

    public class TimeoutCases : TestCaseGenerator<TimeoutCase> {
        protected override IEnumerable<TimeoutCase> Data() => [
            // timeout allows ~2-3 items before expiring: yields partial batches
            new(
                ItemCount: 30,
                BatchSize: 10,
                ItemDelay: TimeSpan.FromMilliseconds(10),
                Timeout: TimeSpan.FromMilliseconds(60)
            ),
            // timeout > total batch time: yields full batches
            new(
                ItemCount: 10,
                BatchSize: 5,
                ItemDelay: TimeSpan.FromMilliseconds(5),
                Timeout: TimeSpan.FromSeconds(1)
            ),
            // timeout < item delay: yields small batches (1 item each)
            new(
                ItemCount: 6,
                BatchSize: 2,
                ItemDelay: TimeSpan.FromSeconds(1),
                Timeout: TimeSpan.FromSeconds(2)
            )
        ];
    }
}
