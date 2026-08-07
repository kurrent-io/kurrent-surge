using System.Threading.Channels;
using Bogus;
using Kurrent.Surge.Experimental;
using ValidationException = Kurrent.Validation.ValidationException;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Testkit.Datasets.ArcadeRacer.Fakers;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Surge.Streaming.Tests;

[Category("Appender")]
[Category("Integration")]
[Timeout(30_000)]
public class AppenderTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    // RaceCar is mapped via the testkit's message catalog (auto-discovered at client build) —
    // per-test mapper registration is gone: resolvers snapshot at client construction.

    AppendRecord NewRecord(StreamName stream, string? orderingKey = null) =>
        AppendRecord.Create(RaceCar.Generate(), r => {
            r.Stream = stream;
            r.OrderingKey(orderingKey);
        });

    AppendRecord[] NewRecords(StreamName stream, int count, string? orderingKey = null) =>
        Enumerable.Range(0, count).Select(_ => NewRecord(stream, orderingKey)).ToArray();

    #region ->> Basic Append <<-

    public class RecordCounts : TestCaseGenerator<int> {
        protected override IEnumerable<int> Data() => [1, 5, 20];
    }

    [Test]
    [RecordCounts]
    public async ValueTask appends_records_with_callback(int count, CancellationToken ct) {
        // Arrange
        var stream  = $"appender-cb-{TestId.New()}";
        var records = NewRecords(stream, count);

        await using var appender = new Appender(CreateOptions());

        var results = new List<AppendResult>();
        var done    = new TaskCompletionSource();

        // Act
        foreach (var record in records) {
            await appender.Append(record, result => {
                lock (results) {
                    results.Add(result);
                    if (results.Count == records.Length)
                        done.TrySetResult();
                }
            }, ct);
        }

        await done.Task.WaitAsync(ct);

        // Assert
        await Assert.That(results).Count().IsEqualTo(records.Length);

        foreach (var result in results)
            await Assert.That(result.IsSuccess).IsTrue();

        var readRecords = await ReadStream(stream, ct);
        await Assert.That(readRecords).Count().IsEqualTo(records.Length);
    }

    [Test]
    [RecordCounts]
    public async ValueTask appends_records_with_async_callback(int count, CancellationToken ct) {
        // Arrange
        var stream  = $"appender-async-cb-{TestId.New()}";
        var records = NewRecords(stream, count);

        await using var appender = new Appender(CreateOptions());

        var results = new List<AppendResult>();
        var done    = new TaskCompletionSource();

        // Act
        foreach (var record in records) {
            await appender.Append(record, result => {
                lock (results) {
                    results.Add(result);
                    if (results.Count == records.Length)
                        done.TrySetResult();
                }

                return ValueTask.CompletedTask;
            }, ct);
        }

        await done.Task.WaitAsync(ct);

        // Assert
        await Assert.That(results).Count().IsEqualTo(records.Length);

        foreach (var result in results)
            await Assert.That(result.IsSuccess).IsTrue();

        var readRecords = await ReadStream(stream, ct);
        await Assert.That(readRecords).Count().IsEqualTo(records.Length);
    }

    [Test]
    [RecordCounts]
    public async ValueTask appends_records_with_async_await(int count, CancellationToken ct) {
        // Arrange
        var stream  = $"appender-async-{TestId.New()}";
        var records = NewRecords(stream, count);

        await using var appender = new Appender(CreateOptions());

        // Act
        var results = await AppendAll(appender, records, ct);

        // Assert
        foreach (var result in results)
            await Assert.That(result.IsSuccess).IsTrue();

        var readRecords = await ReadStream(stream, ct);
        await Assert.That(readRecords).Count().IsEqualTo(records.Length);

        var readIds = readRecords.Select(r => r.Id).ToHashSet();

        foreach (var record in records)
            await Assert.That(readIds.Contains(record.RecordId)).IsTrue();
    }

    public class StreamCounts : TestCaseGenerator<int, int> {
        protected override IEnumerable<(int, int)> Data() => [
            (2, 5),
            (3, 10),
            (5, 3)
        ];
    }

    [Test]
    [StreamCounts]
    public async ValueTask appends_to_multiple_streams(int streamCount, int recordsPerStream, CancellationToken ct) {
        // Arrange
        var streams = Enumerable.Range(0, streamCount)
            .Select(i => (StreamName)$"appender-multi-{i}-{TestId.New()}")
            .ToArray();

        var allRecords = streams
            .SelectMany(stream => NewRecords(stream, recordsPerStream))
            .ToArray();

        await using var appender = new Appender(CreateOptions());

        // Act
        var results = await AppendAll(appender, allRecords, ct);

        // Assert
        foreach (var result in results)
            await Assert.That(result.IsSuccess).IsTrue();

        foreach (var stream in streams)
            await Assert.That(await ReadStream(stream, ct)).Count().IsEqualTo(recordsPerStream);
    }

    #endregion

    #region ->> Batching <<-

    public class BatchSizeCases : TestCaseDataGenerator<(int RecordCount, int MaxBatchCount)> {
        protected override IEnumerable<TestCase<(int RecordCount, int MaxBatchCount)>> Data() {
            yield return new((15, 3), DisplayName: "15 records, batch of 3");
            yield return new((30, 5), DisplayName: "30 records, batch of 5");
            yield return new((10, 1), DisplayName: "10 records, batch of 1");
        }
    }

    [Test]
    [BatchSizeCases]
    public async ValueTask dispatches_multiple_small_batches(int recordCount, int maxBatchCount, CancellationToken ct) {
        // Arrange
        var stream  = $"appender-batches-{TestId.New()}";
        var records = NewRecords(stream, recordCount);

        await using var appender = new Appender(CreateOptions(opts => {
            opts.MaxBatchCount = maxBatchCount;
            opts.LingerTimeout = TimeSpan.FromMilliseconds(1);
        }));

        // Act
        var results = await AppendAll(appender, records, ct);

        // Assert
        foreach (var result in results)
            await Assert.That(result.IsSuccess).IsTrue();

        await Assert.That(await ReadStream(stream, ct)).Count().IsEqualTo(recordCount);
    }

    public class ByteSizeCases : TestCaseDataGenerator<(int RecordCount, int MaxBatchBytes)> {
        protected override IEnumerable<TestCase<(int RecordCount, int MaxBatchBytes)>> Data() {
            // RaceCar serializes to ~200-400 bytes; these thresholds force multiple batches
            yield return new((20, 1024), DisplayName: "20 records, ~1 KB batch limit");
            yield return new((12, 2048), DisplayName: "12 records, ~2 KB batch limit");
            yield return new((10, 4096), DisplayName: "10 records, ~4 KB batch limit");
        }
    }

    [Test]
    [ByteSizeCases]
    public async ValueTask flushes_when_batch_byte_size_exceeded(
        int recordCount, int maxBatchBytes, CancellationToken ct
    ) {
        // Arrange — small MaxBatchBytes forces multiple flushes based on actual serialized size
        var stream  = $"appender-bytes-{TestId.New()}";
        var records = NewRecords(stream, recordCount);

        await using var appender = new Appender(CreateOptions(opts => {
            opts.MaxBatchCount = 1000;
            opts.MaxBatchBytes = maxBatchBytes;
            opts.LingerTimeout = TimeSpan.FromMilliseconds(1);
        }));

        // Act
        var results = await AppendAll(appender, records, ct);

        // Assert
        foreach (var result in results)
            await Assert.That(result.IsSuccess).IsTrue();

        await Assert.That(await ReadStream(stream, ct)).Count().IsEqualTo(recordCount);
    }

    [Test]
    [RecordCounts]
    public async ValueTask linger_timeout_flushes_partial_batch(int count, CancellationToken ct) {
        // Arrange — large batch size so the batch never fills; linger forces the flush
        var stream  = $"appender-linger-{TestId.New()}";
        var records = NewRecords(stream, count);

        await using var appender = new Appender(CreateOptions(opts => {
            opts.MaxBatchCount = 1000;
            opts.LingerTimeout = TimeSpan.FromMilliseconds(50);
        }));

        // Act
        var results = await AppendAll(appender, records, ct);

        // Assert
        foreach (var result in results)
            await Assert.That(result.IsSuccess).IsTrue();

        await Assert.That(await ReadStream(stream, ct)).Count().IsEqualTo(count);
    }

    [Test]
    [RecordCounts]
    public async ValueTask flush_drains_all_buffered_records(int count, CancellationToken ct) {
        // Arrange
        var stream  = $"appender-flush-{TestId.New()}";
        var records = NewRecords(stream, count);

        await using var appender = new Appender(CreateOptions(opts => {
            opts.LingerTimeout = TimeSpan.FromSeconds(30);
        }));

        // Act — append without awaiting, then flush
        foreach (var record in records)
            await appender.Append(record, _ => { }, ct);

        await appender.FlushAsync(ct);

        // Assert
        await Assert.That(await ReadStream(stream, ct)).Count().IsEqualTo(count);
    }

    [Test]
    public async ValueTask append_async_cancels_while_the_record_is_buffered(CancellationToken ct) {
        // Arrange — linger far above the test window so only cancellation can complete the await
        var stream = $"appender-cancel-{TestId.New()}";
        var record = NewRecord(stream);

        await using var appender = new Appender(CreateOptions(opts => {
            opts.MaxBatchCount = 1000;
            opts.LingerTimeout = TimeSpan.FromSeconds(30);
        }));

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        var pending = appender.AppendAsync(record, cancellator.Token);

        await cancellator.CancelAsync();

        // Assert
        await Assert.That(async () => await pending).Throws<TaskCanceledException>();
    }

    [Test]
    [RecordCounts]
    public async ValueTask dispose_flushes_buffered_records(int count, CancellationToken ct) {
        // Arrange — linger far above the test window so only disposal can flush
        var stream  = $"appender-dispose-{TestId.New()}";
        var records = NewRecords(stream, count);

        var results = new List<AppendResult>();

        var appender = new Appender(CreateOptions(opts => {
            opts.MaxBatchCount = 1000;
            opts.LingerTimeout = TimeSpan.FromSeconds(30);
        }));

        foreach (var record in records)
            await appender.Append(record, result => { lock (results) results.Add(result); }, ct);

        // Act
        await appender.DisposeAsync();

        // Assert — disposal drained the lanes: every callback fired and every record is durable
        await Assert.That(results).Count().IsEqualTo(records.Length);

        foreach (var result in results)
            await Assert.That(result.IsSuccess).IsTrue();

        await Assert.That(await ReadStream(stream, ct)).Count().IsEqualTo(records.Length);
    }

    [Test]
    [RecordCounts]
    public async ValueTask flush_reports_flushed_and_inflight_counts(int count, CancellationToken ct) {
        // Arrange — linger far above the test window so nothing flushes before the explicit call
        var stream  = $"appender-flush-counts-{TestId.New()}";
        var records = NewRecords(stream, count);

        var expected = (Flushed: count, Inflight: 0);

        await using var appender = new Appender(CreateOptions(opts => {
            opts.MaxBatchCount = 1000;
            opts.LingerTimeout = TimeSpan.FromSeconds(30);
        }));

        foreach (var record in records)
            await appender.Append(record, _ => { }, ct);

        // Act
        var result = await appender.FlushAsync(ct);

        // Assert
        await Assert.That(result).IsEqualTo(expected);
        await Assert.That(await ReadStream(stream, ct)).Count().IsEqualTo(count);
    }

    #endregion

    #region ->> Ordering Key <<-

    public class OrderingKeyCounts : TestCaseGenerator<int> {
        protected override IEnumerable<int> Data() => [5, 10, 20];
    }

    [Test]
    [OrderingKeyCounts]
    public async ValueTask ordering_key_preserves_order_within_same_key(int count, CancellationToken ct) {
        // Arrange
        var stream  = $"appender-okey-order-{TestId.New()}";
        var records = NewRecords(stream, count, orderingKey: "same-key");

        await using var appender = new Appender(CreateOptions(opts => {
            opts.MaxBatchCount = 3;
            opts.LingerTimeout = TimeSpan.FromMilliseconds(1);
        }));

        // Act
        var results = await AppendAll(appender, records, ct);

        // Assert
        foreach (var result in results)
            await Assert.That(result.IsSuccess).IsTrue();

        var readRecords = await ReadStream(stream, ct);
        await Assert.That(readRecords).Count().IsEqualTo(records.Length);

        for (var i = 0; i < records.Length; i++)
            await Assert.That(readRecords[i].Id).IsEqualTo(records[i].RecordId);
    }

    public class KeyCountCases : TestCaseGenerator<int, int> {
        protected override IEnumerable<(int, int)> Data() => [
            (2, 5),
            (3, 10),
            (4, 8)
        ];
    }

    [Test]
    [KeyCountCases]
    public async ValueTask different_ordering_keys_are_all_appended_in_order(
        int keyCount, int recordsPerKey, CancellationToken ct
    ) {
        // Arrange — N ordering keys on N separate streams, interleaved
        var data = Enumerable.Range(0, keyCount)
            .Select(k => (
                Stream:  (StreamName)$"appender-okey-{k}-{TestId.New()}",
                Key:     $"key-{k}",
                Records: (AppendRecord[])null!
            ))
            .Select(x => x with { Records = NewRecords(x.Stream, recordsPerKey, orderingKey: x.Key) })
            .ToArray();

        await using var appender = new Appender(CreateOptions(opts => {
            opts.MaxBatchCount = 3;
            opts.LingerTimeout = TimeSpan.FromMilliseconds(1);
        }));

        // Act — interleave records across all keys
        var tasks = new List<Task<AppendResult>>();

        for (var i = 0; i < recordsPerKey; i++)
            tasks.AddRange(data.Select(entry => appender.AppendAsync(entry.Records[i], ct).AsTask()));

        var results = await Task.WhenAll(tasks);

        // Assert — all acknowledged
        foreach (var result in results)
            await Assert.That(result.IsSuccess).IsTrue();

        // Verify ordering within each key
        foreach (var entry in data) {
            var readRecords = await ReadStream(entry.Stream, ct);
            await Assert.That(readRecords).Count().IsEqualTo(recordsPerKey);

            for (var i = 0; i < recordsPerKey; i++)
                await Assert.That(readRecords[i].Id).IsEqualTo(entry.Records[i].RecordId);
        }
    }

    [Test]
    [KeyCountCases]
    public async ValueTask mixed_keyed_and_unkeyed_records_all_appended_in_order(
        int keyCount, int recordsPerGroup, CancellationToken ct
    ) {
        // Arrange — unkeyed stream + N keyed streams, interleaved
        var unkeyedStream = (StreamName)$"appender-mixed-unkeyed-{TestId.New()}";
        var unkeyed       = NewRecords(unkeyedStream, recordsPerGroup);

        var keyed = Enumerable.Range(0, keyCount)
            .Select(k => (
                Stream:  (StreamName)$"appender-mixed-keyed-{k}-{TestId.New()}",
                Key:     $"key-{k}",
                Records: (AppendRecord[])null!
            ))
            .Select(x => x with { Records = NewRecords(x.Stream, recordsPerGroup, orderingKey: x.Key) })
            .ToArray();

        await using var appender = new Appender(CreateOptions(opts => {
            opts.MaxBatchCount = 3;
            opts.LingerTimeout = TimeSpan.FromMilliseconds(1);
        }));

        // Act — interleave keyed and unkeyed
        var tasks = new List<Task<AppendResult>>();

        for (var i = 0; i < recordsPerGroup; i++) {
            tasks.Add(appender.AppendAsync(unkeyed[i], ct).AsTask());

            foreach (var entry in keyed)
                tasks.Add(appender.AppendAsync(entry.Records[i], ct).AsTask());
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        foreach (var result in results)
            await Assert.That(result.IsSuccess).IsTrue();

        // Verify ordering for unkeyed
        var readUnkeyed = await ReadStream(unkeyedStream, ct);
        await Assert.That(readUnkeyed).Count().IsEqualTo(recordsPerGroup);

        for (var i = 0; i < recordsPerGroup; i++)
            await Assert.That(readUnkeyed[i].Id).IsEqualTo(unkeyed[i].RecordId);

        // Verify ordering for each keyed group
        foreach (var entry in keyed) {
            var readRecords = await ReadStream(entry.Stream, ct);
            await Assert.That(readRecords).Count().IsEqualTo(recordsPerGroup);

            for (var i = 0; i < recordsPerGroup; i++)
                await Assert.That(readRecords[i].Id).IsEqualTo(entry.Records[i].RecordId);
        }
    }

    #endregion

    #region ->> Failure <<-

    [Test]
    public async ValueTask rejected_batch_fails_callbacks_and_the_lane_survives(CancellationToken ct) {
        // Arrange — a raw-bytes record carries an empty wire schema name, which the server rejects;
        // the follow-up record is valid and must flow through the same lane afterwards.
        var stream = $"appender-rejected-{TestId.New()}";

        var invalid = AppendRecord.Create(Faker.Random.Bytes(8), r => {
            r.Stream     = stream;
            r.DataFormat = SchemaDataFormat.Bytes;
        });

        var valid = NewRecord(stream);

        await using var appender = new Appender(CreateOptions(opts => {
            opts.LingerTimeout = TimeSpan.FromMilliseconds(1);
        }));

        AppendResult? rejected = null;

        var rejectedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act — the rejected batch must fail its callback, and FlushAsync must return instead of hanging
        await appender.Append(invalid, result => {
            rejected = result;
            rejectedSignal.TrySetResult();
        }, ct);

        await appender.FlushAsync(ct);
        await rejectedSignal.Task.WaitAsync(ct);

        // Assert — the failure surfaced instead of being swallowed
        await Assert.That(rejected!.IsSuccess).IsFalse();
        await Assert.That(rejected.Error).IsNotNull();

        // Act — the lane is still alive: a valid record appends and acknowledges through it
        var ack = await appender.AppendAsync(valid, ct);

        // Assert
        await Assert.That(ack.IsSuccess).IsTrue();
        await Assert.That(await ReadStream(stream, ct)).Count().IsEqualTo(1);
    }

    #endregion

    #region ->> Options Validation <<-

    public class DropFullModes : TestCaseGenerator<BoundedChannelFullMode> {
        protected override IEnumerable<BoundedChannelFullMode> Data() =>
            Enum.GetValues<BoundedChannelFullMode>().Where(mode => mode is not BoundedChannelFullMode.Wait);
    }

    [Test]
    [DropFullModes]
    public async ValueTask rejects_full_modes_that_can_drop_records(BoundedChannelFullMode mode) {
        // Arrange — drop modes evict buffered records without completing their callbacks
        var options = CreateOptions(opts => opts.FullMode = mode);

        // Act & Assert
        await Assert.That(() => new Appender(options)).Throws<ValidationException>();
    }

    #endregion

    #region ->> Helpers <<-

    static async Task<AppendResult[]> AppendAll(Appender appender, AppendRecord[] records, CancellationToken ct) {
        var tasks = records
            .Select(record => appender.AppendAsync(record, ct))
            .ToList();

        return await Task.WhenAll(tasks.Select(t => t.AsTask()));
    }

    async Task<List<Record>> ReadStream(StreamName stream, CancellationToken ct) =>
        await Fixture.Client.Streams
            .ReadStream(stream, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

    AppenderOptions CreateOptions(Action<AppenderOptions>? configure = null) {
        var options = new AppenderOptions();

        options.Connection(
            Fixture.ConnectionString,
            client => {
                client.LoggerFactory = Fixture.LoggerFactory;
                client.Schema        = KurrentClientSchemaOptions.NoValidation;
                client.Resilience    = KurrentClientResilienceOptions.NoResilience;
                // The Appender builds its own client, so it must opt into the Testkit catalog (race
                // contracts) just like the shared fixture client does — it is discovered but not
                // always-active, so without this RaceCar appends fail to map.
                client.Messages.UseTestkitCatalog();
            });

        configure?.Invoke(options);

        return options;
    }

    #endregion
}
