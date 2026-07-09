using System.Text.RegularExpressions;
using Kurrent.Surge.Client.Processors.Partitioning;
using Kurrent.Surge.Client.Streams;
using Kurrent.Processors.Configuration;
using Kurrent.Streaming.Models;
using Kurrent.Streaming.Options;
using Testing.ArcadeRacer;

namespace Kurrent.Streaming.Tests;

[Category("Processors")]
[Category("Integration")]
[Timeout(120_000)]
public class DroneCommanderIntegrationTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask distributes_records_across_partitions_and_preserves_per_stream_order(CancellationToken ct) {
        // Arrange — many distinct streams so the key-hash spreads them across partitions; each gets its own
        // child drone, one logical partition each. The commander reads ONE subscription and fans records out.
        const int streamCount = 12;
        const int parallelism = 4;

        var seeded       = await Fixture.SeedManyTestStreams(streamCount, ct);
        var total        = seeded.Sum(s => s.Records.Length);
        var lastPosition = seeded.Max(s => s.Position);

        var processor = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= lastPosition) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var options = BuildOptions(
            $"drone-commander-{TestId.New()}", parallelism, "stream",
            StreamsFilter(seeded.Select(s => s.Stream.ToString())));

        // Act — the processor stops the run once the last record is reached; the commander tears down.
        await new DroneCommander(options, () => processor).RunAsync(ct);

        // Assert — every record processed, each stream on the key-hash partition, in revision order.
        await AssertPartitionedInOrder(processor.Records, seeded, parallelism, total);
    }

    [Test]
    public async ValueTask honors_the_partition_key_selector(CancellationToken ct) {
        // Arrange — ONE stream, selector keyed on the record's revision. The partition must come from that
        // key, scattering one stream's records across partitions — not collapsing into a single partition.
        const int parallelism = 4;

        var data         = await Fixture.SeedTestStream(40, ct);
        var total        = data.Records.Length;
        var lastPosition = data.Position;

        var processor = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= lastPosition) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var options = BuildOptions(
            $"drone-commander-key-{TestId.New()}", parallelism, "revision",
            RecordFilter.ByStream(data.Stream));

        // Act
        await new DroneCommander(options, () => processor).RunAsync(ct);

        // Assert — each record's stamped partition is the one the selector produces (recomputed with the same
        // public extractor on the same record), and the records genuinely spread across partitions.
        var extractor = PartitionKeyExtractor.Create("revision");
        var records   = processor.Records;

        await Assert.That(records.Length).IsEqualTo(total);

        foreach (var record in records) {
            var expectedPartition = PartitionCalculator.XXHash.Calculate(extractor.Extract(record)!, parallelism);
            await Assert.That(record.Position.Partition.Value).IsEqualTo(expectedPartition);
        }

        var distinctPartitions = records.Select(r => r.Position.Partition.Value).Distinct().Count();
        await Assert.That(distinctPartitions).IsGreaterThan(1);
    }

    [Test]
    public async ValueTask processes_batches_across_partitions(CancellationToken ct) {
        // Arrange — the child supports both processor shapes; this drives the IRecordBatchProcessor path.
        const int streamCount = 8;
        const int parallelism = 4;

        var seeded       = await Fixture.SeedManyTestStreams(streamCount, ct);
        var total        = seeded.Sum(s => s.Records.Length);
        var lastPosition = seeded.Max(s => s.Position);

        var processor = new TestRecordBatchProcessor {
            OnProcessBatch = ctx => {
                if (ctx.Records.Any(r => r.Position.LogPosition >= lastPosition)) {
                    ctx.Track();
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var options = BuildOptions(
            $"drone-commander-batch-{TestId.New()}", parallelism, "stream",
            StreamsFilter(seeded.Select(s => s.Stream.ToString())));

        // Act
        await new DroneCommander(options, () => processor).RunAsync(ct);

        // Assert — batches dispatched per partition; every record processed, in per-stream order.
        await AssertPartitionedInOrder(processor.Records, seeded, parallelism, total);
    }

    [Test]
    public async ValueTask resumes_from_the_aggregated_checkpoint(CancellationToken ct) {
        // Arrange — many streams (records across partitions), one processor id reused across two runs. The
        // commander commits one aggregated checkpoint per barrier; the second run must resume from it and
        // cover the rest with no loss (at-least-once: a bounded replay is allowed).
        const int streamCount = 8;
        const int parallelism = 4;

        var seeded       = await Fixture.SeedManyTestStreams(streamCount, ct);
        var total        = seeded.Sum(s => s.Records.Length);
        var lastPosition = seeded.Max(s => s.Position);
        var filter       = StreamsFilter(seeded.Select(s => s.Stream.ToString()));
        var processorId  = $"drone-commander-resume-{TestId.New()}";
        var firstPhase   = total / 2;

        // Phase 1 — process ~half, committing on a short interval, then stop.
        var phase1 = new TestableRecordProcessor();
        phase1.OnProcessRecord = ctx => {
            if (phase1.Records.Length >= firstPhase) {
                ctx.Track(ctx.Record);
                ctx.StopProcessing();
            }

            return ValueTask.CompletedTask;
        };

        var options1 = BuildOptions(processorId, parallelism, "stream", filter, TimeSpan.FromMilliseconds(100));
        await new DroneCommander(options1, () => phase1).RunAsync(ct);

        // Phase 2 — resume on the same processor id and run to the end of the stream.
        var phase2 = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= lastPosition) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var options2 = BuildOptions(processorId, parallelism, "stream", filter, TimeSpan.FromMilliseconds(100));
        await new DroneCommander(options2, () => phase2).RunAsync(ct);

        // Assert — phase 1 was a partial run, and the two phases together cover every record (no loss).
        var processedIds = phase1.Records.Concat(phase2.Records).Select(r => r.Id).ToHashSet();

        await Assert.That(phase1.Records.Length).IsGreaterThan(0).And.IsLessThan(total);
        await Assert.That(processedIds.Count).IsEqualTo(total);
    }

    [Test]
    public async ValueTask stops_the_fleet_gracefully_when_a_processor_requests_stop(CancellationToken ct) {
        // Arrange — a processor that stops after a few records. A user stop is fleet-wide and must complete the
        // run cleanly (no fault, no hang); whatever each partition processed is left as an in-order prefix.
        const int parallelism = 4;
        const int stopAfter   = 5;

        var seeded    = await Fixture.SeedManyTestStreams(8, ct);
        var processed = 0;

        var processor = new TestableRecordProcessor();
        processor.OnProcessRecord = ctx => {
            if (Interlocked.Increment(ref processed) >= stopAfter) {
                ctx.Track(ctx.Record);
                ctx.StopProcessing();
            }

            return ValueTask.CompletedTask;
        };

        var options = BuildOptions(
            $"drone-commander-stop-{TestId.New()}", parallelism, "stream",
            StreamsFilter(seeded.Select(s => s.Stream.ToString())));

        // Act — completes gracefully; a throw or a hang fails the test.
        await new DroneCommander(options, () => processor).RunAsync(ct);

        // Assert — it stopped (not zero), and each touched stream is a clean, in-order prefix.
        await Assert.That(processor.Records.Length).IsGreaterThanOrEqualTo(stopAfter);

        foreach (var stream in seeded) {
            var streamRecords = processor.Records.Where(r => r.Stream.Value == stream.Stream.Value).ToArray();

            for (var i = 0; i < streamRecords.Length; i++)
                await Assert.That(streamRecords[i].Revision).IsEqualTo(Revision.From(i));
        }
    }

    [Test]
    public async ValueTask surfaces_a_processor_fault(CancellationToken ct) {
        // Arrange — a processor that throws. A drone fault tears the fleet down AND surfaces from RunAsync.
        const int parallelism = 4;

        var seeded = await Fixture.SeedManyTestStreams(8, ct);

        var processor = new TestableRecordProcessor {
            OnProcessRecord = _ => throw new InvalidOperationException("processor boom")
        };

        var options = BuildOptions(
            $"drone-commander-fault-{TestId.New()}", parallelism, "stream",
            StreamsFilter(seeded.Select(s => s.Stream.ToString())));

        // Act & Assert
        await Assert.That(async () => await new DroneCommander(options, () => processor).RunAsync(ct))
            .Throws<AggregateException>();
    }

    [Test]
    public async ValueTask stops_when_cancelled(CancellationToken ct) {
        // Arrange — no stop condition; the only way out is external cancellation, which must not complete
        // the run successfully (the host treats the OperationCanceledException as a clean stop).
        const int parallelism = 4;

        var seeded    = await Fixture.SeedManyTestStreams(8, ct);
        var processor = new TestableRecordProcessor();

        var options = BuildOptions(
            $"drone-commander-cancel-{TestId.New()}", parallelism, "stream",
            StreamsFilter(seeded.Select(s => s.Stream.ToString())));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        var run = new DroneCommander(options, () => processor).RunAsync(cts.Token);
        await cts.CancelAsync();

        // Assert
        await Assert.That(run).IsNotCompletedSuccessfully();
    }

    #region ->> Helpers <<-

    ProcessorOptions BuildOptions(
        string processorId, int parallelism, string partitionKeySelector, RecordFilter filter, TimeSpan? interval = null
    ) {
        var options = new ProcessorOptions();
        options.Connection(Fixture.ConnectionString, client => client.LoggerFactory = Fixture.LoggerFactory);

        options.ProcessorId                       = processorId;
        options.MaxParallelism                    = parallelism;
        options.Subscription.PartitionKeySelector = partitionKeySelector;
        options.Subscription.Filter               = filter;
        options.Subscription.InitialPosition      = SubscriptionInitialPosition.Earliest;
        options.Checkpoint.Interval               = interval ?? TimeSpan.FromHours(1);

        return options;
    }

    static RecordFilter StreamsFilter(IEnumerable<string> streamNames) =>
        RecordFilter.ByStreamPattern($"^({string.Join("|", streamNames.Select(Regex.Escape))})$");

    // Every record processed, each stream pinned to its key-hash partition, and each stream in revision order.
    static async Task AssertPartitionedInOrder(
        IReadOnlyList<Record> records, IReadOnlyList<SeededTestStream> seeded, int parallelism, int total
    ) {
        await Assert.That(records.Count).IsEqualTo(total);

        foreach (var stream in seeded) {
            var streamValue       = stream.Stream.Value;
            var expectedPartition = PartitionCalculator.XXHash.Calculate(streamValue, parallelism);
            var streamRecords     = records.Where(r => r.Stream.Value == streamValue).ToArray();

            await Assert.That(streamRecords.Length).IsEqualTo(stream.Records.Length);

            for (var i = 0; i < streamRecords.Length; i++) {
                await Assert.That(streamRecords[i].Position.Partition.Value).IsEqualTo(expectedPartition);
                await Assert.That(streamRecords[i].Revision).IsEqualTo(Revision.From(i));
            }
        }
    }

    #endregion
}
