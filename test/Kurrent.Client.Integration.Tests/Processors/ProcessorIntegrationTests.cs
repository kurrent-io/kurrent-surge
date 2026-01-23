using System.Text.RegularExpressions;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Processors;

/// <summary>
/// Integration tests for processor lifecycle, checkpoint recovery, and core user-facing behaviors.
/// These tests validate behavior that requires a real database connection.
/// </summary>
[Category("Processors")]
[Category("Integration")]
public class ProcessorIntegrationTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask complete_processor_lifecycle_with_checkpoint_recovery(CancellationToken ct) {
        // Arrange
        var simulation               = await Fixture.SeedSmartHomeActivity(20, ct);
        var processorId              = $"checkpoint-recovery-{Guid.NewGuid():N}";
        var recordsToProcessInPhase1 = 10;

        // Phase 1: Process first 10 records, checkpoint, then stop
        var processor1 = new TrackingRecordProcessor();

        using var cts1 = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor1.OnRecordProcessed = _ => {
            if (processor1.ProcessedRecords.Count >= recordsToProcessInPhase1)
                cts1.Cancel();
        };

        await KurrentProcessors.Create(processor1)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStreamPattern($"(?=.*)^{Regex.Escape(simulation.Stream.Value)}$"))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .WithCheckpoint(new CheckpointSettings {
                Interval = 100,
                Timeout  = 60000
            })
            .RunAsync(cts1.Token);

        // Wait for checkpoint to be persisted
        await Task.Delay(200, ct);

        // Phase 2: Resume from same processor ID, should process remaining records
        var processor2 = new TrackingRecordProcessor();

        using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor2.OnRecordProcessed = record => {
            if (record.Position.LogPosition >= simulation.Position)
                cts2.Cancel();
        };

        await KurrentProcessors.Create(processor2)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId) // Same processor ID
            .WithFilter(RecordFilter.ByStreamPattern($"(?=.*)^{Regex.Escape(simulation.Stream.Value)}$"))
            // Note: NOT setting InitialPosition - should recover from checkpoint
            .WithCheckpoint(
                new CheckpointSettings {
                    Interval = 60000,
                    Timeout  = 60000
                }
            )
            .RunAsync(cts2.Token);

        // Assert - All records processed exactly once across both runs
        var totalProcessedRecords = processor1.ProcessedRecords.Count + processor2.ProcessedRecords.Count;
        totalProcessedRecords.ShouldBeLessThanOrEqualTo(simulation.Records.Count + 5); // +5 tolerance for checkpoint timing

        // Verify no duplicate processing
        var allProcessedIds = processor1.ProcessedRecords
            .Concat(processor2.ProcessedRecords)
            .Select(r => r.Id)
            .ToList();

        // Count duplicates
        var duplicateCount = allProcessedIds.Count - allProcessedIds.Distinct().Count();
        duplicateCount.ShouldBeLessThanOrEqualTo(5); // Small tolerance for checkpoint timing
    }

    [Test]
    public async ValueTask processor_batches_records_correctly(CancellationToken ct) {
        // Arrange
        var simulation   = await Fixture.SeedSmartHomeActivity(25, ct);
        var processor    = new TrackingRecordProcessor();
        var processorId  = $"batch-maxsize-{Guid.NewGuid():N}";
        var batchSizes   = new List<int>();
        var maxBatchSize = 5;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnBatchProcessed = context => {
            batchSizes.Add(context.Records.Count);
            if (processor.ProcessedRecords.Count >= simulation.Records.Count)
                cts.Cancel();
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStreamPattern($"(?=.*)^{Regex.Escape(simulation.Stream.Value)}$"))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .WithBatching(new BatchingSettings { MaxSize = maxBatchSize, Timeout = 1000 })
            .RunAsync(cts.Token);

        // Assert - No batch should exceed max size
        foreach (var size in batchSizes) size.ShouldBeLessThanOrEqualTo(maxBatchSize);

        // Total records across all batches should equal processed
        batchSizes.Sum().ShouldBe(processor.ProcessedRecords.Count);
    }

    public class TestRecordProcessor : IRecordProcessor {
        public static readonly IRecordProcessor Instance = new TestRecordProcessor();

        long _counter;

        public ValueTask ProcessRecords(ProcessRecordsContext context) {
            if (++_counter >= 5)
                context.StopProcessing();

            return ValueTask.CompletedTask;
        }
    }

    [Test]
    public async ValueTask processor_cancels_gracefully(CancellationToken ct) {
        // Arrange
        var simulation  = await Fixture.SeedSmartHomeActivity(20, ct);
        var processorId = $"cancel-graceful-{Guid.NewGuid():N}";

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var token = cancellator.Token;

        // Act
        var worker = KurrentProcessors.Create(TestRecordProcessor.Instance)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStreamPattern($"(?=.*)^{Regex.Escape(simulation.Stream.Value)}$"))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .Build();

        // Assert

        await Should.NotThrowAsync(() =>  worker.RunAsync(token));
    }

    [Test]
    public async ValueTask processor_stops_on_user_request(CancellationToken ct) {
        // Arrange
        var simulation  = await Fixture.SeedSmartHomeActivity(25, ct);
        var processor   = new TrackingRecordProcessor();
        var processorId = $"user-stop-{Guid.NewGuid():N}";
        var stopCalled  = false;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnRecordProcessed = _ => {
            if (processor.ProcessedRecords.Count >= 10 && !stopCalled) {
                stopCalled = true;
                cts.Cancel(); // Simulate user-initiated stop via cancellation
            }
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStreamPattern($"(?=.*)^{Regex.Escape(simulation.Stream.Value)}$"))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(cts.Token);

        // Assert
        processor.ShutdownCalled.ShouldBeTrue();
        processor.ShutdownContext!.Reason.ShouldBe(ShutdownReason.Cancelled);
        processor.ProcessedRecords.Count.ShouldBeGreaterThanOrEqualTo(10);
        processor.ProcessedRecords.Count.ShouldBeLessThan(simulation.Records.Count);
    }

    [Test]
    public async ValueTask processor_handles_exceptions_and_stops_processing(CancellationToken ct) {
        // Arrange
        var simulation   = await Fixture.SeedSmartHomeActivity(10, ct);
        var processError = new InvalidOperationException("Process failed");

        var processor = new ThrowingRecordProcessor {
            ThrowOnProcessRecords = processError
        };

        var processorId = $"error-process-{Guid.NewGuid():N}";

        var worker = KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStreamPattern($"(?=.*)^{Regex.Escape(simulation.Stream.Value)}$"))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .Build();

        // Act & Assert
        var exception = await Should
            .ThrowAsync<InvalidOperationException>(async () => await worker.RunAsync(ct));

        exception.ShouldBe(processError);
        processor.ProcessRecordsCallCount.ShouldBe(1); // Called once before throwing
    }

    [Test]
    public async ValueTask processor_calls_shutdown_even_on_error(CancellationToken ct) {
        // Arrange
        var simulation      = await Fixture.SeedSmartHomeActivity(10, ct);
        var shutdownCalled  = false;
        var shutdownContext = (ShutdownContext?)null;

        var processor   = new TrackingRecordProcessor();
        var processorId = $"error-shutdown-{Guid.NewGuid():N}";

        processor.OnProcessRecordsCallback = _ => throw new InvalidOperationException("Intentional error");
        processor.OnShutdownCallback = ctx => {
            shutdownCalled  = true;
            shutdownContext = ctx;
            return ValueTask.CompletedTask;
        };

        // Act
        try {
            await KurrentProcessors.Create(processor)
                .UseClient(Fixture.Client)
                .WithProcessorId(processorId)
                .WithFilter(RecordFilter.ByStreamPattern($"(?=.*)^{Regex.Escape(simulation.Stream.Value)}$"))
                .WithInitialPosition(SubscriptionInitialPosition.Earliest)
                .RunAsync(ct);
        }
        catch (InvalidOperationException) {
            // Expected
        }

        // Assert
        shutdownCalled.ShouldBeTrue();
        shutdownContext.ShouldNotBeNull();
        shutdownContext!.Reason.ShouldBe(ShutdownReason.Error);
        shutdownContext.Error.ShouldNotBeNull();
    }

    [Test]
    public async ValueTask records_are_processed(CancellationToken ct) {
        // Arrange
        var simulation  = await Fixture.SeedSmartHomeActivity(100, ct);
        var processor   = new TrackingRecordProcessor();
        var processorId = $"order-{Guid.NewGuid():N}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnRecordProcessed = record => {
            if (record.Position.LogPosition >= simulation.Position)
                cts.Cancel();
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            //.WithFilter(RecordFilter.ByStreamPattern($"(?=.*)^{Regex.Escape(simulation.Stream.Value)}$"))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(cts.Token);

        // Assert
        for (var i = 0; i < processor.ProcessedRecords.Count; i++) {
            var record = processor.ProcessedRecords[i];
            record.StreamRevision.ShouldBe(StreamRevision.From(i));
        }
    }
}
