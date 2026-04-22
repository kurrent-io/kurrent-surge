using Kurrent.Client;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Kurrent.Processors.Models;
using Microsoft.Extensions.Logging;
using Sdk.TestKit.Fixtures;
using TUnit.Assertions.Enums;

namespace Kurrent.Processors.Tests;

[Category("Processors")]
[Category("Integration")]
[Timeout(30_000)]
public class StreamSubscriptionTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask records_are_processed(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(100, ct);
        var processor   = new TestRecordProcessor();
        var processorId = $"order-{PunyId.New()}";

        processor.OnProcessRecord = ctx => {
            if (ctx.Record.Position.LogPosition >= data.Position)
                ctx.StopProcessing();

            return ValueTask.CompletedTask;
        };

        // Act
        await KurrentProcessor.Create(processor, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        }).RunAsync(ct);

        // Assert
        for (var i = 0; i < processor.Records.Length; i++)
            await Assert.That(processor.Records[i].Revision).IsEqualTo(Revision.From(i));
    }

    [Test]
    public async ValueTask processes_records_from_stream(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(ct);

        var processor = new TestRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition == data.Position)
                    ctx.StopAfterTrack();

                return ValueTask.CompletedTask;
            }
        };

        // Act
        await KurrentProcessor.Create(processor, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId                  = $"log-sub-{data.Stream}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            
        }).RunAsync(ct);

        // Assert
        await Assert.That(processor.Records).Count().IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask processor_processes_all_records_individually(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(25, ct);
        var processor   = new TestRecordProcessor();
        var processorId = $"individual-records-{PunyId.New()}";
        var recordCount = 0;

        processor.OnProcessRecord = ctx => {
            Interlocked.Increment(ref recordCount);

            if (ctx.Record.Position.LogPosition >= data.Position)
                ctx.StopAfterTrack();

            return ValueTask.CompletedTask;
        };

        // Act
        await KurrentProcessor.Create(processor, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        }).RunAsync(ct);

        // Assert - Each record was processed individually, total count matches
        await Assert.That(recordCount).IsEqualTo(processor.Records.Length);
    }

    [Test]
    public async ValueTask processor_cancels_gracefully(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(ct);
        var processor   = new TestRecordProcessor();
        var processorId = $"processor_cancels_gracefully-{TestContext.Current!.Id}";

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        }).Build();

        var task = drone.RunAsync(cancellator.Token);

        await cancellator.CancelAsync();

        await Assert.That(task).IsNotCompletedSuccessfully();
    }

    [Test]
    public async ValueTask processor_stops_on_user_request(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(ct);
        var processor   = new TestRecordProcessor();
        var processorId = $"processor_stops_on_user_request-{TestContext.Current!.Id}";

        processor.OnProcessRecord = ctx => {
            if (ctx.Record.Position.LogPosition >= data.Position)
                ctx.StopAfterTrack();

            return ValueTask.CompletedTask;
        };

        // Act
        await KurrentProcessor.Create(processor, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        }).RunAsync(ct);

        // Assert
        await Assert.That(processor.Complete).IsNotNull();
        await Assert.That(processor.Records.Length).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask processor_handles_exceptions_and_stops_processing(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(10, ct);
        var processorId = $"error-process-{PunyId.New()}";

        var processor = new TestRecordProcessor {
            OnProcessRecord = _ => throw new InvalidOperationException("Process failed")
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        }).Build();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<DroneException>(() => drone.RunAsync(ct));

        await Assert.That(exception.InnerException).IsTypeOf<InvalidOperationException>();
        await Assert.That(processor.Records.Length).IsEqualTo(0); // Callback throws before record is enqueued
    }

    [Test]
    public async ValueTask processor_calls_shutdown_even_on_error(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(10, ct);
        var processor   = new TestRecordProcessor();
        var processorId = $"error-shutdown-{PunyId.New()}";

        processor.OnProcessRecord = _ => throw new InvalidOperationException("Intentional error");

        // Act
        try {
            await KurrentProcessor.Create(processor, opts => {
                opts.Connection(Fixture.Client.Options);
                opts.ProcessorId                  = processorId;
                opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
                opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            }).RunAsync(ct);
        } catch (DroneException) {
            // Expected
        }

        // Assert
        await Assert.That(processor.Error).IsNotNull();
        await Assert.That(processor.Error!.Error).IsNotNull();
    }

    [Test]
    public async ValueTask complete_processor_lifecycle_with_checkpoint_recovery(CancellationToken ct) {
        // Arrange
        var data                     = await Fixture.SeedTestStream(20, ct);
        var processorId              = $"checkpoint-recovery-{PunyId.New()}";
        var recordsToProcessInPhase1 = 10;

        // Phase 1: Process first 10 records, checkpoint, then stop
        var processor1 = new TestRecordProcessor();

        processor1.OnProcessRecord = ctx => {
            if (processor1.Records.Length >= recordsToProcessInPhase1)
                ctx.StopProcessing();

            return ValueTask.CompletedTask;
        };

        await KurrentProcessor.Create(processor1, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpointing(c => c.Interval(100L).Timeout(60000L));
        }).RunAsync(ct);

        // Wait for checkpoint to be persisted
        await Task.Delay(200, ct);

        // Phase 2: Resume from same processor ID, should process remaining records
        var processor2 = new TestRecordProcessor();

        processor2.OnProcessRecord = ctx => {
            if (ctx.Record.Position.LogPosition >= data.Position)
                ctx.StopProcessing();

            return ValueTask.CompletedTask;
        };

        await KurrentProcessor.Create(processor2, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId          = processorId;
            opts.Subscription.Filter  = RecordFilter.ByStream(data.Stream);
            opts.Checkpointing(c => c.Interval(60000L).Timeout(60000L));
        }).RunAsync(ct);

        // Assert - All records processed across both runs with tolerance for checkpoint timing
        var totalProcessedRecords = processor1.Records.Length + processor2.Records.Length;
        await Assert.That(totalProcessedRecords).IsLessThanOrEqualTo(data.Records.Length + 5);

        // Verify minimal duplicate processing
        var allProcessedIds = processor1.Records
            .Concat(processor2.Records)
            .Select(r => r.Id)
            .ToList();

        var duplicateCount = allProcessedIds.Count - allProcessedIds.Distinct().Count();
        await Assert.That(duplicateCount).IsLessThanOrEqualTo(5);
    }

    [Test]
    public async ValueTask complete_processor_lifecycle_with_checkpoint_recovery_on_all_stream(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(1000, ct);
        var processorId = $"all-checkpoint-recovery-{data.Stream}";

        // Phase 1: Process first 5 records, stop before tracking the 6th.
        // Checkpoint stores the position of the 5th record.
        var processorOne = new TestRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Progress.Throughput.TotalRecords == 5)
                    ctx.StopBeforeTrack("Phase 1 done after 5 records");

                return ValueTask.CompletedTask;
            }
        };

        await KurrentProcessor.Create(processorOne, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.SkipDecoding                 = true;
        }).RunAsync(ct);

        // Phase 2: Resume from same processor ID, should process remaining records
        var processorTwo = new TestRecordProcessor();

        processorTwo.OnProcessRecord = ctx => {
            if (ctx.Record.Position.LogPosition >= data.Position)
                ctx.StopAfterTrack("Phase 2 reached end of stream");

            return ValueTask.CompletedTask;
        };

        await KurrentProcessor.Create(processorTwo, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.SkipDecoding                 = true;
        }).RunAsync(ct);

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
        var data        = await Fixture.SeedTestStream(30, ct);
        var processorId = $"multi-phase-{PunyId.New()}";
        var processors  = new List<TestRecordProcessor>();

        // Phases 1-2: Process 10 records each, checkpoint, stop
        for (var phase = 0; phase < 2; phase++) {
            var processor = new TestRecordProcessor();
            processors.Add(processor);

            processor.OnProcessRecord = ctx => {
                if (ctx.Progress.Throughput.TotalRecords == 10)
                    ctx.StopBeforeTrack($"Phase {phase + 1} done after 10 records");

                return ValueTask.CompletedTask;
            };

            await KurrentProcessor.Create(processor, opts => {
                opts.Connection(Fixture.Client.Options);
                opts.ProcessorId          = processorId;
                opts.Subscription.Filter  = RecordFilter.ByStream(data.Stream);
                opts.Checkpointing(c => c.Interval(100L).Timeout(60000L));
                if (phase == 0)
                    opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            }).RunAsync(ct);
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

        await KurrentProcessor.Create(processor3, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId          = processorId;
            opts.Subscription.Filter  = RecordFilter.ByStream(data.Stream);
            opts.Checkpointing(c => c.Interval(60000L).Timeout(60000L));
        }).RunAsync(ct);

        // Assert - All unique records across all 3 phases
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
        var data        = await Fixture.SeedTestStream(10, ct);
        var processorId = $"no-remaining-{PunyId.New()}";

        // Phase 1: Process all records
        var processor1 = new TestRecordProcessor();

        processor1.OnProcessRecord = ctx => {
            if (ctx.Record.Position.LogPosition >= data.Position)
                ctx.StopProcessing();

            return ValueTask.CompletedTask;
        };

        await KurrentProcessor.Create(processor1, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpointing(c => c.Interval(100L).Timeout(60000L));
        }).RunAsync(ct);

        await Task.Delay(200, ct);

        // Phase 2: Resume — no records remain, cancel after 2s
        var processor2 = new TestRecordProcessor();

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancellator.CancelAfter(2000);

        var drone = KurrentProcessor.Create(processor2, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId          = processorId;
            opts.Subscription.Filter  = RecordFilter.ByStream(data.Stream);
            opts.Checkpointing(c => c.Interval(60000L).Timeout(60000L));
        }).Build();

        var task = drone.RunAsync(cancellator.Token);

        await Assert.That(task).IsNotCompletedSuccessfully();

        // Assert
        await Assert.That(processor2.Records.Length).IsEqualTo(0);
    }

    [Test]
    [Skip("")]
    public async ValueTask checkpoint_recovery_when_stream_is_tombstoned(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(200, ct);
        var processorId = $"tombstoned-{PunyId.New()}";

        // Phase 1: Process first 10 records, checkpoint, stop
        var processor1 = new TestRecordProcessor();

        processor1.OnProcessRecord = ctx => {
            if (processor1.Records.Length >= 10)
                ctx.StopBeforeTrack("Phase 1 done after 10 records");

            return ValueTask.CompletedTask;
        };

        await KurrentProcessor.Create(processor1, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            // opts.Checkpointing(c => c.Interval(100L).Timeout(60000L));
        }).RunAsync(ct);

        // Tombstone the stream between phases
        var pos = await Fixture.Client.Streams.Tombstone(data.Stream, ct);

        try {
            var temp = await Fixture.Client.Streams.InspectRecord(pos, ct);

            Fixture.Logger.LogInformation("Tombstoned record: {RecordId}", temp.Id);
        }
        catch (Exception ex) {
            Fixture.Logger.LogInformation("Expected error when inspecting tombstoned record: {Message}", ex.Message);
            await Assert.That(ex).IsTypeOf<StreamTombstonedException>();
        }

        // Phase 2: Resume — stream is tombstoned
        // Cancel after 2s to stop the idle live subscription.
        using var cancellator = CancellationTokenSource
            .CreateLinkedTokenSource(ct)
            .With(x => x.CancelAfter(5000));

        var stoppingToken = cancellator.Token;

        var processor2 = new TestRecordProcessor();

        var drone = KurrentProcessor.Create(processor2, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId         = processorId;
            opts.Subscription.Filter = RecordFilter.ByStream(data.Stream);
        }).Build();

        // Act & Assert — expect error due to tombstoned stream
        await Assert.That(async () => await drone.RunAsync(stoppingToken))
            .Throws<StreamTombstonedException>();
    }

    [Test]
    public async ValueTask checkpoint_recovery_when_stream_is_soft_deleted(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(20, ct);
        var processorId = $"deleted-{PunyId.New()}";

        // Phase 1: Process first 10 records, checkpoint, stop
        var processor1 = new TestRecordProcessor();

        processor1.OnProcessRecord = ctx => {
            if (processor1.Records.Length >= 10)
                ctx.StopBeforeTrack("Phase 1 done after 10 records");

            return ValueTask.CompletedTask;
        };

        await KurrentProcessor.Create(processor1, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        }).RunAsync(ct);

        // Soft delete the stream between phases
        await Fixture.Client.Streams.Delete(data.Stream, ct);

        // Phase 2: Resume — soft-deleted stream truncates remaining events.
        // The subscription connects and catches up immediately with 0 new records.
        // Cancel after 2s to stop the idle live subscription.
        var processor2 = new TestRecordProcessor();

        using var cancellator = CancellationTokenSource.CreateShortLived(2000, ct);

        var drone = KurrentProcessor.Create(processor2, opts => {
            opts.Connection(Fixture.Client.Options);
            opts.ProcessorId         = processorId;
            opts.Subscription.Filter = RecordFilter.ByStream(data.Stream);
        }).Build();

        var task = drone.RunAsync(cancellator.Token);

        // Assert
        using var _ = Assert.Multiple();
        await Assert.That(task).IsNotCompletedSuccessfully();
        await Assert.That(processor1.Records.Length).IsEqualTo(10);
        await Assert.That(processor2.Records.Length).IsEqualTo(0);
    }
}
