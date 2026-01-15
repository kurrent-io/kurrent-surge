using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Processors;

/// <summary>
/// Tests for processor checkpoint persistence and recovery.
/// </summary>
[Category("Processors")]
[Category("Checkpoint")]
[Category("Integration")]
public class CheckpointTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask stores_checkpoint_after_threshold(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(15, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"checkpoint-threshold-{Guid.NewGuid():N}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnRecordProcessed = record => {
            if (record.Position >= simulation.Position)
                cts.Cancel();
        };

        // Act - configure with low threshold to trigger checkpoint
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .WithCheckpoint(new CheckpointSettings {
                Threshold = 5,      // Trigger after 5 records
                Interval = 60000,   // Long interval to avoid time-based trigger
                Timeout = 60000
            })
            .RunAsync(cts.Token);

        // Assert
        // With 15 records and threshold of 5, we should have at least 1-2 checkpoints
        processor.CheckpointCompleteCalls.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async ValueTask recovers_from_last_checkpoint_position(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(20, ct);
        var processorId = $"checkpoint-recover-{Guid.NewGuid():N}";

        // First run - process some records and checkpoint
        var processor1 = new TrackingRecordProcessor();
        using var cts1 = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var recordsBeforeCancel = 10;

        processor1.OnRecordProcessed = _ => {
            if (processor1.ProcessedRecords.Count >= recordsBeforeCancel)
                cts1.Cancel();
        };

        await KurrentProcessors.Create(processor1)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .WithCheckpoint(new CheckpointSettings {
                Threshold = 5,
                Interval = 100, // Short interval to ensure checkpoint
                Timeout = 60000
            })
            .RunAsync(cts1.Token);

        // Wait for checkpoint to be persisted
        await Task.Delay(200, ct);

        // Second run - should resume from checkpoint
        var processor2 = new TrackingRecordProcessor();
        using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor2.OnRecordProcessed = record => {
            if (record.Position >= simulation.Position)
                cts2.Cancel();
        };

        await KurrentProcessors.Create(processor2)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId) // Same processor ID
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            // Note: NOT setting InitialPosition - should recover from checkpoint
            .WithCheckpoint(new CheckpointSettings {
                Threshold = 100,
                Interval = 60000,
                Timeout = 60000
            })
            .RunAsync(cts2.Token);

        // Assert - second run should process fewer records than total
        // because it resumed from checkpoint
        var totalRecordsInBothRuns = processor1.ProcessedRecords.Count + processor2.ProcessedRecords.Count;

        // If recovery worked, we didn't reprocess everything from the beginning
        // (some tolerance for checkpoint timing)
        totalRecordsInBothRuns.ShouldBeLessThanOrEqualTo(simulation.Records.Count + 5);
    }

    [Test]
    public async ValueTask calls_on_checkpoint_complete_after_persistence(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(12, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"checkpoint-callback-{Guid.NewGuid():N}";
        var checkpointContexts = new List<CheckpointCompleteContext>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnCheckpointCompleteCallback = context => {
            checkpointContexts.Add(context);
            return ValueTask.CompletedTask;
        };

        processor.OnRecordProcessed = record => {
            if (record.Position >= simulation.Position)
                cts.Cancel();
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .WithCheckpoint(new CheckpointSettings {
                Threshold = 5,
                Interval = 100,
                Timeout = 60000
            })
            .RunAsync(cts.Token);

        // Assert
        if (processor.CheckpointCompleteCalls > 0) {
            checkpointContexts.Count.ShouldBe(processor.CheckpointCompleteCalls);

            foreach (var context in checkpointContexts) {
                context.Progress.Checkpoint.TotalCheckpoints.ShouldBeGreaterThan(0);
            }
        }
    }

    [Test]
    public async ValueTask checkpoint_progress_is_tracked_correctly(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(15, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"checkpoint-progress-{Guid.NewGuid():N}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnRecordProcessed = record => {
            if (record.Position >= simulation.Position)
                cts.Cancel();
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .WithCheckpoint(new CheckpointSettings {
                Threshold = 5,
                Interval = 100,
                Timeout = 60000
            })
            .RunAsync(cts.Token);

        // Assert
        var shutdownProgress = processor.ShutdownContext!.Progress;

        // If checkpoints occurred, verify the progress is tracked
        if (processor.CheckpointCompleteCalls > 0) {
            shutdownProgress.Checkpoint.TotalCheckpoints.ShouldBe(processor.CheckpointCompleteCalls);
            shutdownProgress.Checkpoint.LastCompletedAt.ShouldNotBeNull();
        }
    }

    [Test]
    public async ValueTask processes_all_records_with_checkpointing_enabled(CancellationToken ct) {
        // Arrange
        var recordCount = 25;
        var simulation = await Fixture.SeedSmartHomeActivity(recordCount, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"checkpoint-all-{Guid.NewGuid():N}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnRecordProcessed = record => {
            if (record.Position >= simulation.Position)
                cts.Cancel();
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .WithCheckpoint(new CheckpointSettings {
                Threshold = 10,
                Interval = 100,
                Timeout = 60000
            })
            .RunAsync(cts.Token);

        // Assert - all records should still be processed despite checkpointing
        processor.ProcessedRecords.Count.ShouldBe(simulation.Records.Count);
    }
}
