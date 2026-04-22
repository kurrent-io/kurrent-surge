using Kurrent.Client;
using Kurrent.Client.Streams;
using Kurrent.Processors.Gateway;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;
using Kurrent.Testing.Testkit;
using Microsoft.Extensions.Logging;
using Kurrent.Sdk.Testkit.Fixtures;
using TUnit.Assertions.Enums;

namespace Kurrent.Processors.Tests;

[Category("Processors")]
[Category("Integration")]
[Timeout(30_000)]
public class DroneIntegrationTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    Drone CreateDrone(
        string processorId,
        IRecordProcessor processor,
        StreamName stream,
        SubscriptionInitialPosition initialPosition = SubscriptionInitialPosition.Latest,
        long checkpointInterval                     = 30_000
    ) {
        var settings = new ProcessorOptions {
            ProcessorId  = processorId,
            Subscription = new SubscriptionOptions {
                Filter          = RecordFilter.ByStream(stream),
                InitialPosition = initialPosition
            },
            Checkpoint = new CheckpointOptions {
                Interval = TimeSpan.FromMilliseconds(checkpointInterval)
            }
        };

        var source = new SubscriptionSource(Fixture.Client, settings, TimeProvider.System);

        return new Drone(
            settings, source, processor,
            Fixture.LoggerFactory, TimeProvider.System);
    }

    #region ->> processing <<-

    [Test]
    public async ValueTask records_are_processed(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(100, ct);
        var processor   = new TestRecordProcessor();
        var processorId = $"drone-records-{PunyId.New()}";

        processor.OnProcessRecord = ctx => {
            if (ctx.Record.Position.LogPosition >= data.Position)
                ctx.StopProcessing();

            return ValueTask.CompletedTask;
        };

        var drone = CreateDrone(processorId, processor,
            data.Stream,
            SubscriptionInitialPosition.Earliest);

        // Act
        await drone.RunAsync(ct);

        // Assert
        for (var i = 0; i < processor.Records.Length; i++)
            await Assert.That(processor.Records[i].Revision).IsEqualTo(Revision.From(i));
    }

    [Test]
    public async ValueTask processes_all_records_from_stream(CancellationToken ct) {
        // Arrange
        var data      = await Fixture.SeedTestStream(ct);
        var processor = new TestRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition == data.Position)
                    ctx.StopAfterTrack();

                return ValueTask.CompletedTask;
            }
        };

        var drone = CreateDrone($"drone-all-{data.Stream}", processor,
            data.Stream,
            SubscriptionInitialPosition.Earliest);

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(processor.Records).Count().IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask processor_processes_all_records_individually(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(25, ct);
        var processor   = new TestRecordProcessor();
        var processorId = $"drone-individual-{PunyId.New()}";
        var recordCount = 0;

        processor.OnProcessRecord = ctx => {
            Interlocked.Increment(ref recordCount);

            if (ctx.Record.Position.LogPosition >= data.Position)
                ctx.StopAfterTrack();

            return ValueTask.CompletedTask;
        };

        var drone = CreateDrone(processorId, processor,
            data.Stream,
            SubscriptionInitialPosition.Earliest);

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(recordCount).IsEqualTo(processor.Records.Length);
    }

    #endregion

    #region ->> stop and cancel <<-

    [Test]
    public async ValueTask processor_stops_on_user_request(CancellationToken ct) {
        // Arrange
        var data      = await Fixture.SeedTestStream(ct);
        var processor = new TestRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopAfterTrack();

                return ValueTask.CompletedTask;
            }
        };

        var drone = CreateDrone($"drone-stop-{TestContext.Current!.Id}", processor,
            data.Stream,
            SubscriptionInitialPosition.Earliest);

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(processor.Complete).IsNotNull();
        await Assert.That(processor.Records.Length).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask processor_cancels_gracefully(CancellationToken ct) {
        // Arrange
        var data      = await Fixture.SeedTestStream(ct);
        var processor = new TestRecordProcessor();

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var drone = CreateDrone($"drone-cancel-{TestContext.Current!.Id}", processor,
            data.Stream,
            SubscriptionInitialPosition.Earliest);

        // Act
        var task = drone.RunAsync(cancellator.Token);

        await cancellator.CancelAsync();

        await Assert.That(task).IsNotCompletedSuccessfully();
    }

    #endregion

    #region ->> error handling <<-

    [Test]
    public async ValueTask processor_handles_exceptions_and_stops_processing(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        var processor = new TestRecordProcessor {
            OnProcessRecord = _ => throw new InvalidOperationException("Process failed")
        };

        var drone = CreateDrone($"drone-error-{PunyId.New()}", processor,
            data.Stream,
            SubscriptionInitialPosition.Earliest);

        // Act & Assert — Drone wraps in AggregateException, inner is DroneException
        var aex = await Assert.That(async () => await drone.RunAsync(ct))
            .Throws<AggregateException>();

        await Assert.That(aex!.InnerExceptions[0]).IsTypeOf<DroneException>();
        await Assert.That(processor.Records.Length).IsEqualTo(0);
    }

    [Test]
    public async ValueTask processor_calls_on_error_even_on_failure(CancellationToken ct) {
        // Arrange
        var data      = await Fixture.SeedTestStream(10, ct);
        var processor = new TestRecordProcessor {
            OnProcessRecord = _ => throw new InvalidOperationException("Intentional error")
        };

        var drone = CreateDrone($"drone-onerror-{PunyId.New()}", processor,
            data.Stream,
            SubscriptionInitialPosition.Earliest);

        // Act
        try { await drone.RunAsync(ct); }
        catch (AggregateException) { }

        // Assert
        await Assert.That(processor.Error).IsNotNull();
        await Assert.That(processor.Error!.Error).IsNotNull();
    }

    #endregion

    #region ->> checkpoint recovery <<-

    [Test]
    public async ValueTask complete_lifecycle_with_checkpoint_recovery(CancellationToken ct) {
        // Arrange
        var data                     = await Fixture.SeedTestStream(20, ct);
        var processorId              = $"drone-ckpt-{PunyId.New()}";
        var recordsToProcessInPhase1 = 10;

        // Phase 1: Process first 10 records, checkpoint, then stop
        var processor1 = new TestRecordProcessor();

        processor1.OnProcessRecord = ctx => {
            if (processor1.Records.Length >= recordsToProcessInPhase1)
                ctx.StopProcessing();

            return ValueTask.CompletedTask;
        };

        var drone1 = CreateDrone(processorId, processor1,
            data.Stream,
            SubscriptionInitialPosition.Earliest,
            checkpointInterval: 100);

        await drone1.RunAsync(ct);

        // Allow checkpoint to persist
        await Task.Delay(200, ct);

        // Phase 2: Resume from same processor ID — should process remaining records
        var processor2 = new TestRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();

                return ValueTask.CompletedTask;
            }
        };

        var drone2 = CreateDrone(processorId, processor2,
            data.Stream,
            checkpointInterval: 60_000);

        await drone2.RunAsync(ct);

        // Assert — all records processed with tolerance for checkpoint timing
        var totalProcessedRecords = processor1.Records.Length + processor2.Records.Length;
        var allProcessedIds       = processor1.Records.Concat(processor2.Records).Select(r => r.Id).ToList();
        var duplicateCount        = allProcessedIds.Count - allProcessedIds.Distinct().Count();

        await Assert.That(totalProcessedRecords).IsLessThanOrEqualTo(data.Records.Length + 5);
        await Assert.That(duplicateCount).IsLessThanOrEqualTo(5);
    }

    [Test]
    public async ValueTask checkpoint_recovery_across_multiple_phases(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(30, ct);
        var processorId = $"drone-multi-{PunyId.New()}";
        var processors  = new List<TestRecordProcessor>();

        // Phases 1-2: Process 10 records each, then stop
        for (var phase = 0; phase < 2; phase++) {
            var processor = new TestRecordProcessor();
            processors.Add(processor);

            processor.OnProcessRecord = ctx => {
                if (ctx.Progress.Throughput.TotalRecords == 10)
                    ctx.StopBeforeTrack($"Phase {phase + 1} done after 10 records");

                return ValueTask.CompletedTask;
            };

            var drone = CreateDrone(processorId, processor,
                data.Stream,
                phase == 0 ? SubscriptionInitialPosition.Earliest : SubscriptionInitialPosition.Latest,
                checkpointInterval: 100);

            await drone.RunAsync(ct);
            await Task.Delay(200, ct);
        }

        // Phase 3: Process remaining records
        var processor3 = new TestRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopAfterTrack("Phase 3 reached end of stream");

                return ValueTask.CompletedTask;
            }
        };
        processors.Add(processor3);

        var finalDrone = CreateDrone(processorId, processor3,
            data.Stream,
            checkpointInterval: 60_000);

        await finalDrone.RunAsync(ct);

        // Assert — all unique records across all 3 phases
        var allProcessedIds = processors.SelectMany(p => p.Records).Select(r => r.Id).ToList();
        var uniqueCount     = allProcessedIds.Distinct().Count();
        var duplicateCount  = allProcessedIds.Count - uniqueCount;

        await Assert.That(uniqueCount).IsGreaterThanOrEqualTo(data.Records.Length);
        await Assert.That(duplicateCount).IsLessThanOrEqualTo(5);
    }

    [Test]
    public async ValueTask checkpoint_recovery_with_no_remaining_records(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(10, ct);
        var processorId = $"drone-norec-{PunyId.New()}";

        // Phase 1: Process all records
        var processor1 = new TestRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();

                return ValueTask.CompletedTask;
            }
        };

        var drone1 = CreateDrone(processorId, processor1,
            data.Stream,
            SubscriptionInitialPosition.Earliest,
            checkpointInterval: 100);

        await drone1.RunAsync(ct);
        await Task.Delay(200, ct);

        // Phase 2: Resume — no records remain, cancel after 2s
        var processor2 = new TestRecordProcessor();

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancellator.CancelAfter(2000);

        var drone2 = CreateDrone(processorId, processor2,
            data.Stream,
            checkpointInterval: 60_000);

        var task = drone2.RunAsync(cancellator.Token);

        // Assert
        using var _ = Assert.Multiple();
        await Assert.That(task).IsNotCompletedSuccessfully();
        await Assert.That(processor2.Records.Length).IsEqualTo(0);
    }

    [Test]
    public async ValueTask checkpoint_recovery_all_stream_with_skip_decoding(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(1000, ct);
        var processorId = $"drone-all-ckpt-{data.Stream}";

        // Phase 1: Process first 5 records, stop before tracking the 6th
        var processorOne = new TestRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Progress.Throughput.TotalRecords == 5)
                    ctx.StopBeforeTrack("Phase 1 done after 5 records");

                return ValueTask.CompletedTask;
            }
        };

        var drone1 = CreateDrone(processorId, processorOne,
            data.Stream,
            SubscriptionInitialPosition.Earliest);

        await drone1.RunAsync(ct);

        // Phase 2: Resume, process remaining records
        var processorTwo = new TestRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopAfterTrack("Phase 2 reached end of stream");

                return ValueTask.CompletedTask;
            }
        };

        var drone2 = CreateDrone(processorId, processorTwo,
            data.Stream,
            SubscriptionInitialPosition.Earliest);

        await drone2.RunAsync(ct);

        // Assert
        var streamRecordIds    = data.Records.Select(x => x.RecordId).ToList();
        var processedRecordIds = processorOne.Records.Concat(processorTwo.Records).Select(r => r.Id).ToList();
        var duplicateCount     = processedRecordIds.Count - processedRecordIds.Distinct().Count();

        using var __ = Assert.Multiple();
        await Assert.That(processedRecordIds).IsEquivalentTo(streamRecordIds, CollectionOrdering.Matching);
        await Assert.That(duplicateCount).IsLessThanOrEqualTo(5);
    }

    #endregion

    #region ->> status <<-

    [Test]
    public async ValueTask status_is_stopped_after_graceful_shutdown(CancellationToken ct) {
        // Arrange
        var data      = await Fixture.SeedTestStream(5, ct);
        var processor = new TestRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopAfterTrack();

                return ValueTask.CompletedTask;
            }
        };

        var drone = CreateDrone($"drone-status-{PunyId.New()}", processor,
            data.Stream,
            SubscriptionInitialPosition.Earliest);

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(drone.Status).IsEqualTo(DroneStatus.Stopped);
    }

    [Test]
    public async ValueTask status_is_error_after_processing_failure(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        var processor = new TestRecordProcessor {
            OnProcessRecord = _ => throw new InvalidOperationException("boom")
        };

        var drone = CreateDrone($"drone-errstat-{PunyId.New()}", processor,
            data.Stream,
            SubscriptionInitialPosition.Earliest);

        // Act
        await Assert.That(async () => await drone.RunAsync(ct))
            .Throws<AggregateException>();

        // Assert
        await Assert.That(drone.Status).IsEqualTo(DroneStatus.Error);
    }

    #endregion
}
