// #pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
//
// namespace Kurrent.Client.Tests.Extensions;
//
// public class ReadBatchedTests {
//     // Classification helper
//     static BatchAction<TestItem, object> Classify(object msg) =>
//         msg switch {
//             TestItem item => BatchAction<TestItem, object>.Batch(item),
//             TestTrigger   => BatchAction<TestItem, object>.FlushThenYield(msg),
//             _             => throw new InvalidOperationException($"Unknown message type: {msg.GetType()}")
//         };
//
//     // Batch converter
//     static object ToBatch(TestItem[] items) => new TestBatch(items);
//
//     static async IAsyncEnumerable<T> AsAsyncEnumerable<T>(params T[] items) {
//         foreach (var item in items) yield return item;
//     }
//
//     #region Fast Path
//
//     [Test]
//     public async ValueTask synchronous_source_hits_fast_path() {
//         // A synchronous source should complete without async delays
//         // This tests that IsCompletedSuccessfully branch is taken
//         var source = Enumerable.Range(0, 100)
//             .Select(i => (object)new TestItem(i))
//             .ToAsyncEnumerable();
//
//         var sw = System.Diagnostics.Stopwatch.StartNew();
//
//         var results = new List<object>();
//         await foreach (var result in source.ReadBatched(
//                            10,
//                            TimeSpan.FromSeconds(30),
//                            Classify,
//                            ToBatch
//                        ))
//             results.Add(result);
//
//         sw.Stop();
//
//         // Should complete nearly instantly (fast path)
//         await Assert.That(sw.ElapsedMilliseconds).IsLessThan(1000);
//         await Assert.That(results).Count().IsEqualTo(10); // 100 items / 10 batch size
//     }
//
//     #endregion
//
//     // Test types (no dependencies on production code)
//     record TestItem(int Value);
//
//     record TestTrigger(int Id);
//
//     record TestBatch(TestItem[] Items);
//
//     #region Basic Batching
//
//     [Test]
//     public async ValueTask single_item_yields_batch_of_one() {
//         var source = AsAsyncEnumerable(new TestItem(1));
//
//         var results = new List<object>();
//         await foreach (var result in source.ReadBatched(10, TimeSpan.FromSeconds(10), Classify, ToBatch))
//             results.Add(result);
//
//         await Assert.That(results).Count().IsEqualTo(1);
//         await Assert.That(results[0]).IsTypeOf<TestBatch>()
//             .Because($"expected TestBatch but got {results[0].GetType().Name}");
//
//         var batch = (TestBatch)results[0];
//         await Assert.That(batch.Items).Count().IsEqualTo(1);
//         await Assert.That(batch.Items[0].Value).IsEqualTo(1);
//     }
//
//     [Test]
//     public async ValueTask exact_batch_size_yields_one_full_batch() {
//         var source = AsAsyncEnumerable<object>(new TestItem(1), new TestItem(2), new TestItem(3));
//
//         var results = new List<object>();
//         await foreach (var result in source.ReadBatched(3, TimeSpan.FromSeconds(10), Classify, ToBatch))
//             results.Add(result);
//
//         await Assert.That(results).Count().IsEqualTo(1);
//         await Assert.That(results[0]).IsTypeOf<TestBatch>()
//             .Because($"expected TestBatch but got {results[0].GetType().Name}");
//
//         var batch = (TestBatch)results[0];
//         await Assert.That(batch.Items).Count().IsEqualTo(3);
//     }
//
//     [Test]
//     [MethodDataSource(nameof(BatchingTestCases))]
//     public async ValueTask items_are_batched_correctly(int itemCount, int batchSize, int[][] expectedBatches) {
//         // Arrange: create items 1..itemCount
//         var source = AsAsyncEnumerable(Enumerable.Range(1, itemCount).Select(object (i) => new TestItem(i)).ToArray());
//
//         // Act
//         var results = new List<object>();
//         await foreach (var result in source.ReadBatched(batchSize, TimeSpan.FromSeconds(10), Classify, ToBatch))
//             results.Add(result);
//
//         // Assert: correct number of batches
//         await Assert.That(results).Count().IsEqualTo(expectedBatches.Length)
//             .Because($"expected {expectedBatches.Length} batches for {itemCount} items with batch size {batchSize}");
//
//         // Assert: each batch contains expected items in order
//         for (var i = 0; i < expectedBatches.Length; i++) {
//             await Assert.That(results[i]).IsTypeOf<TestBatch>()
//                 .Because($"batch {i} should be TestBatch");
//
//             var batch        = (TestBatch)results[i];
//             var actualValues = batch.Items.Select(item => item.Value).ToArray();
//
//             await Assert.That(actualValues).IsEquivalentTo(expectedBatches[i])
//                 .Because($"batch {i} should contain [{string.Join(", ", expectedBatches[i])}]");
//         }
//     }
//
//     public static IEnumerable<Func<(int itemCount, int batchSize, int[][] expectedBatches)>> BatchingTestCases() {
//         // Exact fit: 6 items, batch size 2 → 3 batches
//         yield return () => (6, 2, [[1, 2], [3, 4], [5, 6]]);
//
//         // Exact fit: 9 items, batch size 3 → 3 batches
//         yield return () => (9, 3, [[1, 2, 3], [4, 5, 6], [7, 8, 9]]);
//
//         // Remainder: 7 items, batch size 3 → 3 batches (last partial)
//         yield return () => (7, 3, [[1, 2, 3], [4, 5, 6], [7]]);
//
//         // Remainder: 10 items, batch size 4 → 3 batches (last partial)
//         yield return () => (10, 4, [[1, 2, 3, 4], [5, 6, 7, 8], [9, 10]]);
//
//         // Single batch: 5 items, batch size 5 → 1 batch
//         yield return () => (5, 5, [[1, 2, 3, 4, 5]]);
//
//         // Single batch (under size): 3 items, batch size 10 → 1 batch
//         yield return () => (3, 10, [[1, 2, 3]]);
//
//         // Single item: 1 item, batch size 1 → 1 batch
//         yield return () => (1, 1, [[1]]);
//     }
//
//     [Test]
//     public async ValueTask empty_source_yields_nothing() {
//         var source = AsAsyncEnumerable<object>();
//
//         var results = new List<object>();
//         await foreach (var result in source.ReadBatched(10, TimeSpan.FromSeconds(10), Classify, ToBatch))
//             results.Add(result);
//
//         await Assert.That(results).IsEmpty();
//     }
//
//     #endregion
//
//     #region Flush Trigger Behavior
//
//     [Test]
//     public async ValueTask flush_trigger_flushes_pending_batch_then_yields_trigger() {
//         // Input:  [Item(1), Item(2), Trigger(100), Item(3)]
//         // Output: [Batch([1,2]), Trigger(100), Batch([3])]
//         var source = AsAsyncEnumerable<object>(
//             new TestItem(1),
//             new TestItem(2),
//             new TestTrigger(100),
//             new TestItem(3)
//         );
//
//         var results = new List<object>();
//         await foreach (var result in source.ReadBatched(10, TimeSpan.FromSeconds(10), Classify, ToBatch))
//             results.Add(result);
//
//         await Assert.That(results).Count().IsEqualTo(3);
//
//         // First: batch with items 1, 2
//         await Assert.That(results[0]).IsTypeOf<TestBatch>()
//             .Because($"expected TestBatch but got {results[0].GetType().Name}");
//
//         var batch1 = (TestBatch)results[0];
//         await Assert.That(batch1.Items.Select(i => i.Value)).IsEquivalentTo([1, 2]);
//
//         // Second: the trigger
//         await Assert.That(results[1]).IsTypeOf<TestTrigger>()
//             .Because($"expected TestTrigger but got {results[1].GetType().Name}");
//
//         var trigger = (TestTrigger)results[1];
//         await Assert.That(trigger.Id).IsEqualTo(100);
//
//         // Third: batch with item 3
//         await Assert.That(results[2]).IsTypeOf<TestBatch>()
//             .Because($"expected TestBatch but got {results[2].GetType().Name}");
//
//         var batch2 = (TestBatch)results[2];
//         await Assert.That(batch2.Items.Select(i => i.Value)).IsEquivalentTo([3]);
//     }
//
//     [Test]
//     public async ValueTask flush_trigger_with_no_pending_items_yields_only_trigger() {
//         var source = AsAsyncEnumerable<object>(new TestTrigger(1), new TestItem(10));
//
//         var results = new List<object>();
//         await foreach (var result in source.ReadBatched(10, TimeSpan.FromSeconds(10), Classify, ToBatch))
//             results.Add(result);
//
//         await Assert.That(results).Count().IsEqualTo(2);
//         await Assert.That(results[0]).IsTypeOf<TestTrigger>();
//         await Assert.That(results[1]).IsTypeOf<TestBatch>();
//     }
//
//     [Test]
//     public async ValueTask consecutive_flush_triggers_yield_separately() {
//         var source = AsAsyncEnumerable<object>(
//             new TestItem(1), new TestTrigger(100), new TestTrigger(200),
//             new TestItem(2)
//         );
//
//         var results = new List<object>();
//         await foreach (var result in source.ReadBatched(10, TimeSpan.FromSeconds(10), Classify, ToBatch))
//             results.Add(result);
//
//         await Assert.That(results).Count().IsEqualTo(4);
//
//         // Batch(1), Trigger(100), Trigger(200), Batch(2)
//         await Assert.That(results[0]).IsTypeOf<TestBatch>();
//         await Assert.That(results[1]).IsTypeOf<TestTrigger>();
//         await Assert.That(((TestTrigger)results[1]).Id).IsEqualTo(100);
//         await Assert.That(results[2]).IsTypeOf<TestTrigger>();
//         await Assert.That(((TestTrigger)results[2]).Id).IsEqualTo(200);
//         await Assert.That(results[3]).IsTypeOf<TestBatch>();
//     }
//
//     #endregion
//
//     #region Timeout Behavior
//
//     [Test]
//     public async ValueTask partial_batch_yields_on_timeout() {
//         // Source yields 3 items quickly, then blocks for a finite time (until cancelled)
//         using var cts     = new CancellationTokenSource();
//         var       results = new List<object>();
//
//         await foreach (var result in YieldThenBlock(3, cts.Token).ReadBatched(100, TimeSpan.FromMilliseconds(100), Classify, ToBatch, cts.Token)) {
//             results.Add(result);
//             // After getting first batch, cancel to exit
//             await cts.CancelAsync();
//             break;
//         }
//
//         await Assert.That(results).Count().IsEqualTo(1);
//         await Assert.That(results[0]).IsTypeOf<TestBatch>()
//             .Because($"expected TestBatch but got {results[0].GetType().Name}");
//
//         var batch = (TestBatch)results[0];
//         await Assert.That(batch.Items).Count().IsEqualTo(3);
//     }
//
//     // Helper: yields N items then blocks until cancellation
//     static IAsyncEnumerable<object> YieldThenBlock(int count, CancellationToken ct) => new YieldThenBlockEnumerable(count, ct);
//
//     sealed class YieldThenBlockEnumerable(int count, CancellationToken ct) : IAsyncEnumerable<object> {
//         public IAsyncEnumerator<object> GetAsyncEnumerator(CancellationToken cancellationToken = default) => new Enumerator(count, ct);
//
//         sealed class Enumerator(int count, CancellationToken ct) : IAsyncEnumerator<object> {
//             int _index;
//
//             public object Current { get; private set; } = null!;
//
//             public async ValueTask<bool> MoveNextAsync() {
//                 if (_index < count) {
//                     Current = new TestItem(_index + 1);
//                     _index++;
//                     return true;
//                 }
//
//                 // Block until cancellation (finite timeout as safety)
//                 try {
//                     await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
//                 }
//                 catch (OperationCanceledException) {
//                     // Expected - just exit
//                 }
//
//                 return false;
//             }
//
//             public ValueTask DisposeAsync() => ValueTask.CompletedTask;
//         }
//     }
//
//     [Test]
//     public async ValueTask zero_timeout_with_sync_source_batches_normally() {
//         // With synchronous source (fast path), zero timeout shouldn't matter
//         // because items arrive without async delay
//         var source = AsAsyncEnumerable<object>(new TestItem(1), new TestItem(2), new TestItem(3));
//
//         var results = new List<object>();
//         await foreach (var result in source.ReadBatched(2, TimeSpan.Zero, Classify, ToBatch))
//             results.Add(result);
//
//         // Should still batch properly due to fast path
//         await Assert.That(results.Count).IsGreaterThanOrEqualTo(1);
//
//         var totalItems = results
//             .OfType<TestBatch>()
//             .SelectMany(b => b.Items)
//             .Count();
//
//         await Assert.That(totalItems).IsEqualTo(3);
//     }
//
//     #endregion
//
//     #region Cancellation
//
//     [Test]
//     public async ValueTask cancellation_while_waiting_exits_cleanly() {
//         // This test verifies that the "park and resume" pattern correctly handles cancellation
//         // while MoveNextAsync is pending. The fix uses Task.WhenAny instead of WaitAsync to
//         // avoid abandoning the pending task, then awaits it in the finally block before disposal.
//         //
//         // See: dotnet/runtime#61236, Stephen Toub's MSDN article on async enumerables
//
//         using var cts = new CancellationTokenSource();
//         cts.CancelAfter(TimeSpan.FromMilliseconds(100));
//
//         var results = new List<object>();
//
//         var sw = System.Diagnostics.Stopwatch.StartNew();
//
//         try {
//             await foreach (var result in BlockForever(cts.Token).ReadBatched(
//                                10,
//                                TimeSpan.FromSeconds(30),
//                                Classify,
//                                ToBatch,
//                                cts.Token
//                            ))
//                 results.Add(result);
//         }
//         catch (OperationCanceledException) {
//             // Expected - cancellation propagates cleanly
//         }
//
//         sw.Stop();
//
//         // Should exit much faster than the 30s batch timeout
//         await Assert.That(sw.ElapsedMilliseconds).IsLessThan(5000);
//         await Assert.That(results).IsEmpty();
//     }
//
//     // Helper: blocks until cancelled (yields nothing)
//     static IAsyncEnumerable<object> BlockForever(CancellationToken ct) => new BlockForeverEnumerable(ct);
//
//     sealed class BlockForeverEnumerable(CancellationToken ct) : IAsyncEnumerable<object> {
//         public IAsyncEnumerator<object> GetAsyncEnumerator(CancellationToken cancellationToken = default) => new Enumerator(ct);
//
//         sealed class Enumerator(CancellationToken ct) : IAsyncEnumerator<object> {
//             public object Current => null!;
//
//             public async ValueTask<bool> MoveNextAsync() {
//                 // Block until cancellation (finite timeout as safety)
//                 try {
//                     await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
//                 }
//                 catch (OperationCanceledException) {
//                     // Expected - just exit
//                 }
//
//                 return false;
//             }
//
//             public ValueTask DisposeAsync() => ValueTask.CompletedTask;
//         }
//     }
//
//     [Test]
//     public async ValueTask cancellation_stops_enumeration_gracefully() {
//         var source = AsAsyncEnumerable<object>(
//             new TestItem(1), new TestItem(2), new TestItem(3),
//             new TestItem(4), new TestItem(5), new TestItem(6)
//         );
//
//         using var cts = new CancellationTokenSource();
//
//         var batchCount = 0;
//
//         try {
//             await foreach (var _ in source.ReadBatched(2, TimeSpan.FromSeconds(10), Classify, ToBatch, cts.Token)) {
//                 batchCount++;
//                 if (batchCount >= 2)
//                     await cts.CancelAsync();
//             }
//         }
//         catch (OperationCanceledException) {
//             // Expected when cancelled
//         }
//
//         await Assert.That(batchCount).IsGreaterThanOrEqualTo(2);
//     }
//
//     #endregion
//
//     #region Ordering Guarantees
//
//     [Test]
//     public async ValueTask preserves_item_order_within_and_across_batches() {
//         var items = Enumerable.Range(0, 25)
//             .Select(object (i) => new TestItem(i))
//             .ToArray();
//
//         var source = AsAsyncEnumerable(items);
//
//         var results = new List<object>();
//         await foreach (var result in source.ReadBatched(7, TimeSpan.FromSeconds(10), Classify, ToBatch))
//             results.Add(result);
//
//         // Flatten all batched items
//         var allValues = results
//             .OfType<TestBatch>()
//             .SelectMany(b => b.Items.Select(i => i.Value))
//             .ToList();
//
//         await Assert.That(allValues).Count().IsEqualTo(25);
//
//         // Verify order is preserved
//         for (var i = 0; i < allValues.Count; i++)
//             await Assert.That(allValues[i]).IsEqualTo(i);
//     }
//
//     [Test]
//     public async ValueTask preserves_order_with_interleaved_flush_triggers() {
//         // Items: 0, 1, Trigger, 2, 3, 4, Trigger, 5
//         var messages = new object[] {
//             new TestItem(0), new TestItem(1), new TestTrigger(100),
//             new TestItem(2), new TestItem(3), new TestItem(4), new TestTrigger(200),
//             new TestItem(5)
//         };
//
//         var source = AsAsyncEnumerable(messages);
//
//         var results = new List<object>();
//         await foreach (var result in source.ReadBatched(10, TimeSpan.FromSeconds(10), Classify, ToBatch))
//             results.Add(result);
//
//         // Expected: Batch([0,1]), Trigger(100), Batch([2,3,4]), Trigger(200), Batch([5])
//         await Assert.That(results).Count().IsEqualTo(5);
//
//         await Assert.That(results[0]).IsTypeOf<TestBatch>();
//         var batch1 = (TestBatch)results[0];
//         await Assert.That(batch1.Items.Select(i => i.Value)).IsEquivalentTo([0, 1]);
//
//         await Assert.That(results[1]).IsTypeOf<TestTrigger>();
//         await Assert.That(((TestTrigger)results[1]).Id).IsEqualTo(100);
//
//         await Assert.That(results[2]).IsTypeOf<TestBatch>();
//         var batch2 = (TestBatch)results[2];
//         await Assert.That(batch2.Items.Select(i => i.Value)).IsEquivalentTo([2, 3, 4]);
//
//         await Assert.That(results[3]).IsTypeOf<TestTrigger>();
//         await Assert.That(((TestTrigger)results[3]).Id).IsEqualTo(200);
//
//         await Assert.That(results[4]).IsTypeOf<TestBatch>();
//         var batch3 = (TestBatch)results[4];
//         await Assert.That(batch3.Items.Select(i => i.Value)).IsEquivalentTo([5]);
//     }
//
//     #endregion
//
//     #region Memory Safety
//
//     [Test]
//     public async ValueTask pool_buffer_returned_on_normal_completion() {
//         // This test verifies the code path completes without leaking
//         // (ArrayPool returns happen in finally block)
//         var source = AsAsyncEnumerable<object>(new TestItem(1), new TestItem(2), new TestItem(3));
//
//         var results = new List<object>();
//         await foreach (var result in source.ReadBatched(10, TimeSpan.FromSeconds(10), Classify, ToBatch))
//             results.Add(result);
//
//         await Assert.That(results).Count().IsEqualTo(1);
//         // If we get here without exception, the finally block executed
//     }
//
//     [Test]
//     public async ValueTask pool_buffer_returned_on_exception_in_source() {
//         var throwAfter = 3;
//         var count      = 0;
//
//         Exception? caught = null;
//
//         try {
//             await foreach (var _ in ThrowingSource().ReadBatched(10, TimeSpan.FromSeconds(10), Classify, ToBatch)) {
//                 // Just consume - we're testing cleanup on exception
//             }
//         }
//         catch (InvalidOperationException ex) {
//             caught = ex;
//         }
//
//         await Assert.That(caught).IsNotNull();
//         await Assert.That(caught!.Message).IsEqualTo("Test exception");
//
//         return;
//
//         // If we get here, the finally block executed despite the exception
//
//         async IAsyncEnumerable<object> ThrowingSource() {
//             while (true) {
//                 if (count++ >= throwAfter)
//                     throw new InvalidOperationException("Test exception");
//
//                 yield return new TestItem(count);
//             }
//         }
//     }
//
//     #endregion
// }
