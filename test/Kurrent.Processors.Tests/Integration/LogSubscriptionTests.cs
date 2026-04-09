using Kurrent.Client.Streams;
using Sdk.TestKit.Fixtures;
using TUnit.Assertions.Enums;

namespace Kurrent.Processors.Tests;

[Category("Processors")]
[Category("Integration")]
[Timeout(30_000)]
public class LogSubscriptionTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask records_are_processed(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(100, ct);
        var processor    = new TestRecordProcessor();
        var processorId  = $"order-log-{data.Stream}";

        processor.OnProcessRecord = ctx => {
            if (ctx.Record.Position.LogPosition >= data.Position)
                ctx.StopProcessing();

            return ValueTask.CompletedTask;
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilterExpression($"properties.stream = '{data.Stream}'")
            .StartFrom(lastPosition)
            .RunAsync(ct);

        // Assert
        for (var i = 0; i < processor.Records.Length; i++)
            await Assert.That(processor.Records[i].Revision).IsEqualTo(Revision.From(i));
    }

    [Test]
    public async ValueTask processes_records_from_log(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(ct);

        var processor = new TestRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition == data.Position)
                    ctx.StopAfterTrack();

                return ValueTask.CompletedTask;
            }
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId($"log-sub-{data.Stream}")
            .WithFilterExpression($"properties.stream = '{data.Stream}'")
            .StartFrom(lastPosition)
            .RunAsync(ct);

        // Assert
        await Assert.That(processor.Records).Count().IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask processor_processes_all_records_individually(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(25, ct);
        var processor    = new TestRecordProcessor();
        var processorId  = $"individual-records-log-{data.Stream}";
        var recordCount  = 0;

        processor.OnProcessRecord = ctx => {
            Interlocked.Increment(ref recordCount);

            if (ctx.Record.Position.LogPosition >= data.Position)
                ctx.StopAfterTrack();

            return ValueTask.CompletedTask;
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilterExpression($"properties.stream = '{data.Stream}'")
            .StartFrom(lastPosition)
            .RunAsync(ct);

        // Assert
        await Assert.That(recordCount).IsEqualTo(processor.Records.Length);
    }

    [Test]
    public async ValueTask processor_cancels_gracefully(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(ct);
        var processor    = new TestRecordProcessor();
        var processorId  = $"processor_cancels_gracefully_log-{TestContext.Current!.Id}";

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        var worker = KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilterExpression($"properties.stream = '{data.Stream}'")
            .StartFrom(lastPosition)
            .Build();

        var task = worker.RunAsync(cancellator.Token);

        await cancellator.CancelAsync();

        await Assert.That(task).IsNotCompletedSuccessfully();
    }

    [Test]
    public async ValueTask processor_stops_on_user_request(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(ct);
        var processor    = new TestRecordProcessor();
        var processorId  = $"processor_stops_on_user_request_log-{TestContext.Current!.Id}";

        processor.OnProcessRecord = ctx => {
            if (ctx.Record.Position.LogPosition >= data.Position)
                ctx.StopAfterTrack();

            return ValueTask.CompletedTask;
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilterExpression($"properties.stream = '{data.Stream}'")
            .StartFrom(lastPosition)
            .RunAsync(ct);

        // Assert
        await Assert.That(processor.Complete).IsNotNull();
        await Assert.That(processor.Records.Length).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask processor_handles_exceptions_and_stops_processing(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(10, ct);
        var processorId  = $"error-process-log-{PunyId.New()}";

        var processor = new TestRecordProcessor {
            OnProcessRecord = _ => throw new InvalidOperationException("Process failed")
        };

        var worker = KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilterExpression($"properties.stream = '{data.Stream}'")
            .StartFrom(lastPosition)
            .Build();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KurrentWorkerException>(() => worker.RunAsync(ct));

        await Assert.That(exception.InnerException).IsTypeOf<InvalidOperationException>();
        await Assert.That(processor.Records.Length).IsEqualTo(0);
    }

    [Test]
    public async ValueTask processor_calls_shutdown_even_on_error(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(10, ct);
        var processor    = new TestRecordProcessor();
        var processorId  = $"error-shutdown-log-{PunyId.New()}";

        processor.OnProcessRecord = _ => throw new InvalidOperationException("Intentional error");

        // Act
        try {
            await KurrentProcessors.Create(processor)
                .UseClient(Fixture.Client)
                .WithProcessorId(processorId)
                .WithFilterExpression($"properties.stream = '{data.Stream}'")
                .StartFrom(lastPosition)
                .RunAsync(ct);
        } catch (KurrentWorkerException) {
            // Expected
        }

        // Assert
        await Assert.That(processor.Error).IsNotNull();
        await Assert.That(processor.Error!.Error).IsNotNull();
    }

    [Test]
    public async ValueTask complete_processor_lifecycle_with_checkpoint_recovery(CancellationToken ct) {
        // Arrange
        var lastPosition             = await Fixture.LatestRecordPosition(ct);
        var data                     = await Fixture.SeedTestStream(20, ct);
        var processorId              = $"checkpoint-recovery-log-{PunyId.New()}";
        var filter                   = $"properties.stream = '{data.Stream}'";
        var recordsToProcessInPhase1 = 10;

        // Phase 1: Process first 10 records, checkpoint, then stop
        var processor1 = new TestRecordProcessor();

        processor1.OnProcessRecord = ctx => {
            if (processor1.Records.Length >= recordsToProcessInPhase1)
                ctx.StopProcessing();

            return ValueTask.CompletedTask;
        };

        await KurrentProcessors.Create(processor1)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilterExpression(filter)
            .StartFrom(lastPosition)
            .ConfigureCheckpointing(c => c.Interval(100).Timeout(60000))
            .RunAsync(ct);

        await Task.Delay(200, ct);

        // Phase 2: Resume from same processor ID, should process remaining records
        var processor2 = new TestRecordProcessor();

        processor2.OnProcessRecord = ctx => {
            if (ctx.Record.Position.LogPosition >= data.Position)
                ctx.StopProcessing();

            return ValueTask.CompletedTask;
        };

        await KurrentProcessors.Create(processor2)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilterExpression(filter)
            .ConfigureCheckpointing(c => c.Interval(60000).Timeout(60000))
            .RunAsync(ct);

        // Assert
        var totalProcessedRecords = processor1.Records.Length + processor2.Records.Length;
        await Assert.That(totalProcessedRecords).IsLessThanOrEqualTo(data.Records.Length + 5);

        var allProcessedIds = processor1.Records
            .Concat(processor2.Records)
            .Select(r => r.Id)
            .ToList();

        var duplicateCount = allProcessedIds.Count - allProcessedIds.Distinct().Count();
        await Assert.That(duplicateCount).IsLessThanOrEqualTo(5);
    }

    [Test]
    public async ValueTask checkpoint_recovery_on_log_subscription(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(ct);
        var processorId  = $"prx-{data.Stream}";
        var filter       = RecordFilter.ByExpression($"properties.stream = '{data.Stream}'");

        // Phase 1: Process 1 record, checkpoint, stop
        var processorOne = new TestRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Progress.Throughput.TotalRecords == 1)
                    ctx.StopBeforeTrack("Phase 1 done after 1 record");
                return ValueTask.CompletedTask;
            }
        };

        await KurrentProcessors.Create(processorOne)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(filter)
            .StartFrom(lastPosition)
            .ConfigureWorker(x => x.SkipDecoding())
            .RunAsync(ct);

        // Phase 2: Resume from checkpoint, process remaining records
        var processorTwo = new TestRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopAfterTrack("Phase 2 reached end of stream");

                return ValueTask.CompletedTask;
            }
        };

        await KurrentProcessors.Create(processorTwo)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(filter)
            .ConfigureWorker(x => x.SkipDecoding())
            .RunAsync(ct);

        // Assert - All records processed across both runs
        var streamRecordIds    = data.Records.Select(x => x.RecordId).ToList();
        var processedRecordIds = processorOne.Records.Concat(processorTwo.Records).Select(r => r.Id).ToList();
        var duplicateCount     = processedRecordIds.Count - processedRecordIds.Distinct().Count();

        using var _ = Assert.Multiple();
        await Assert.That(processedRecordIds).IsEquivalentTo(streamRecordIds, CollectionOrdering.Matching);
        await Assert.That(duplicateCount).IsLessThanOrEqualTo(5);
    }

    [Test]
    public async ValueTask checkpoint_recovery_across_multiple_phases(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(30, ct);
        var processorId  = $"multi-phase-log-{PunyId.New()}";
        var filter       = $"properties.stream = '{data.Stream}'";
        var processors   = new List<TestRecordProcessor>();

        // Phases 1-2: Process 10 records each, checkpoint, stop
        for (var phase = 0; phase < 2; phase++) {
            var processor = new TestRecordProcessor();
            processors.Add(processor);

            processor.OnProcessRecord = ctx => {
                if (ctx.Progress.Throughput.TotalRecords == 10)
                    ctx.StopBeforeTrack($"Phase {phase + 1} done after 10 records");

                return ValueTask.CompletedTask;
            };

            var builder = KurrentProcessors.Create(processor)
                .UseClient(Fixture.Client)
                .WithProcessorId(processorId)
                .WithFilterExpression(filter)
                .ConfigureCheckpointing(c => c.Interval(100).Timeout(60000));

            if (phase == 0)
                builder.StartFrom(lastPosition);

            await builder.RunAsync(ct);
            await Task.Delay(200, ct);
        }

        // Phase 3: Process remaining records
        var processor3 = new TestRecordProcessor();
        processors.Add(processor3);

        processor3.OnProcessRecord = ctx => {
            if (ctx.Record.Position.LogPosition >= data.Position)
                ctx.StopAfterTrack("Phase 3 reached end of stream");

            return ValueTask.CompletedTask;
        };

        await KurrentProcessors.Create(processor3)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilterExpression(filter)
            .ConfigureCheckpointing(c => c.Interval(60000).Timeout(60000))
            .RunAsync(ct);

        // Assert
        var allProcessedIds = processors
            .SelectMany(p => p.Records)
            .Select(r => r.Id)
            .ToList();

        var uniqueCount    = allProcessedIds.Distinct().Count();
        var duplicateCount = allProcessedIds.Count - uniqueCount;

        await Assert.That(uniqueCount).IsGreaterThanOrEqualTo(data.Records.Length);
        await Assert.That(duplicateCount).IsLessThanOrEqualTo(5);
    }

    [Test]
    public async ValueTask checkpoint_recovery_with_no_remaining_records(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(10, ct);
        var processorId  = $"no-remaining-log-{PunyId.New()}";
        var filter       = $"properties.stream = '{data.Stream}'";

        // Phase 1: Process all records
        var processor1 = new TestRecordProcessor();

        processor1.OnProcessRecord = ctx => {
            if (ctx.Record.Position.LogPosition >= data.Position)
                ctx.StopProcessing();

            return ValueTask.CompletedTask;
        };

        await KurrentProcessors.Create(processor1)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilterExpression(filter)
            .StartFrom(lastPosition)
            .ConfigureCheckpointing(c => c.Interval(100).Timeout(60000))
            .RunAsync(ct);

        await Task.Delay(200, ct);

        // Phase 2: Resume — no records remain, cancel after 2s
        var processor2 = new TestRecordProcessor();

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancellator.CancelAfter(2000);

        var worker = KurrentProcessors.Create(processor2)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilterExpression(filter)
            .ConfigureCheckpointing(c => c.Interval(60000).Timeout(60000))
            .Build();

        var task = worker.RunAsync(cancellator.Token);

        await Assert.That(task).IsNotCompletedSuccessfully();

        // Assert
        await Assert.That(processor2.Records.Length).IsEqualTo(0);
    }

    [Test]
    public async ValueTask checkpoint_recovery_when_stream_is_tombstoned(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(20, ct);
        var processorId  = $"tombstoned-log-{PunyId.New()}";
        var filter       = $"properties.stream = '{data.Stream}'";

        // Phase 1: Process first 10 records, checkpoint, stop
        var processor1 = new TestRecordProcessor();

        processor1.OnProcessRecord = ctx => {
            if (processor1.Records.Length >= 10)
                ctx.StopBeforeTrack("Phase 1 done after 10 records");

            return ValueTask.CompletedTask;
        };

        await KurrentProcessors.Create(processor1)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilterExpression(filter)
            .StartFrom(lastPosition)
            .RunAsync(ct);

        // Tombstone the stream between phases
        await Fixture.Client.Streams.Tombstone(data.Stream, ct);

        // Phase 2: Resume — log subscription still sees all events (they exist in $all).
        // Tombstone only prevents stream-level access, not log-level reads.
        var processor2 = new TestRecordProcessor();

        processor2.OnProcessRecord = ctx => {
            if (ctx.Record.Position.LogPosition >= data.Position)
                ctx.StopAfterTrack("Phase 2 reached end of stream");

            return ValueTask.CompletedTask;
        };

        await KurrentProcessors.Create(processor2)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilterExpression(filter)
            .RunAsync(ct);

        // Assert — all records processed across both phases
        var allProcessedIds = processor1.Records
            .Concat(processor2.Records)
            .Select(r => r.Id)
            .ToList();

        var duplicateCount = allProcessedIds.Count - allProcessedIds.Distinct().Count();

        using var _ = Assert.Multiple();
        await Assert.That(allProcessedIds.Distinct().Count()).IsEqualTo(data.Records.Length);
        await Assert.That(duplicateCount).IsLessThanOrEqualTo(5);
    }

    [Test]
    public async ValueTask checkpoint_recovery_when_stream_is_soft_deleted(CancellationToken ct) {
        // Arrange
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(20, ct);
        var processorId  = $"soft-deleted-log-{PunyId.New()}";
        var filter       = $"properties.stream = '{data.Stream}'";

        // Phase 1: Process first 10 records, checkpoint, stop
        var processor1 = new TestRecordProcessor();

        processor1.OnProcessRecord = ctx => {
            if (processor1.Records.Length >= 10)
                ctx.StopBeforeTrack("Phase 1 done after 10 records");

            return ValueTask.CompletedTask;
        };

        await KurrentProcessors.Create(processor1)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilterExpression(filter)
            .StartFrom(lastPosition)
            .RunAsync(ct);

        // Soft delete the stream between phases
        await Fixture.Client.Streams.Delete(data.Stream, ct);

        // Phase 2: Resume — log subscription still sees all events (they exist in $all).
        // Soft delete only affects stream-level access, not log-level reads.
        var processor2 = new TestRecordProcessor();

        processor2.OnProcessRecord = ctx => {
            if (ctx.Record.Position.LogPosition >= data.Position)
                ctx.StopAfterTrack("Phase 2 reached end of stream");

            return ValueTask.CompletedTask;
        };

        await KurrentProcessors.Create(processor2)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilterExpression(filter)
            .RunAsync(ct);

        // Assert — all records processed across both phases
        var allProcessedIds = processor1.Records
            .Concat(processor2.Records)
            .Select(r => r.Id)
            .ToList();

        var duplicateCount = allProcessedIds.Count - allProcessedIds.Distinct().Count();

        using var _ = Assert.Multiple();
        await Assert.That(allProcessedIds.Distinct().Count()).IsEqualTo(data.Records.Length);
        await Assert.That(duplicateCount).IsLessThanOrEqualTo(5);
    }

    [Test]
    [Timeout(60_000)]
    public async ValueTask tombstoned_stream_events_not_visible_after_scavenge(CancellationToken ct) {
        // Arrange — seed events, tombstone the stream, scavenge, then try to subscribe
        var lastPosition = await Fixture.LatestRecordPosition(ct);
        var data         = await Fixture.SeedTestStream(10, ct);
        var processorId  = $"scavenge-tombstone-{PunyId.New()}";
        var filter       = $"properties.stream = '{data.Stream}'";

        // Tombstone + scavenge before subscribing
        await Fixture.Client.Streams.Tombstone(data.Stream, ct);
        await Fixture.Client.Admin.StartScavenge(cancellationToken: ct);
        await Task.Delay(5000, ct); // give scavenge time to run on small dataset

        // Act — subscribe from before the events were written.
        // After scavenge, tombstoned events should be removed from $all.
        // Cancel after 3s since the processor will sit idle.
        // SkipDecoding is required because $all may contain system events (e.g. $scavengeStarted)
        // that cannot be deserialized — the client-side filter handles them.
        var processor = new TestRecordProcessor();

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancellator.CancelAfter(3000);

        var worker = KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilterExpression(filter)
            .StartFrom(lastPosition)
            .ConfigureWorker(x => x.SkipDecoding())
            .Build();

        var task = worker.RunAsync(cancellator.Token);
        await Assert.That(task).IsNotCompletedSuccessfully();

        // Assert — scavenge removed the tombstoned events from $all
        await Assert.That(processor.Records.Length).IsEqualTo(0);
    }
}
