using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Processors;

/// <summary>
/// Tests for processor position tracking accuracy.
/// </summary>
[Category("Processors")]
[Category("PositionTracking")]
[Category("Integration")]
public class PositionTrackingTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask reports_correct_last_tracked_position(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(10, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"position-tracked-{Guid.NewGuid():N}";
        LogPosition? lastObservedPosition = null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnRecordProcessed = record => {
            lastObservedPosition = record.Position.LogPosition;
            if (record.Position.LogPosition >= simulation.Position)
                cts.Cancel();
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(cts.Token);

        // Assert
        lastObservedPosition.ShouldNotBeNull();
        lastObservedPosition.ShouldBe(simulation.Position);
    }

    [Test]
    public async ValueTask partition_progress_is_available_in_context(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(12, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"position-partition-{Guid.NewGuid():N}";
        ProcessorProgress? lastProgress = null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnBatchProcessed = context => {
            lastProgress = context.Progress;
            if (processor.ProcessedRecords.Count >= simulation.Records.Count)
                cts.Cancel();
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(cts.Token);

        // Assert
        lastProgress.ShouldNotBeNull();
        lastProgress.Value.Throughput.TotalRecords.ShouldBeGreaterThan(0);
    }

    [Test]
    public async ValueTask progress_accumulates_across_batches(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(20, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"position-accumulate-{Guid.NewGuid():N}";
        var progressSnapshots = new List<long>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnBatchProcessed = context => {
            progressSnapshots.Add(context.Progress.Throughput.TotalRecords);
            if (processor.ProcessedRecords.Count >= simulation.Records.Count)
                cts.Cancel();
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .WithBatching(new BatchingSettings { MaxSize = 5, Timeout = 50 })
            .RunAsync(cts.Token);

        // Assert - progress should be monotonically increasing
        for (var i = 1; i < progressSnapshots.Count; i++) {
            progressSnapshots[i].ShouldBeGreaterThanOrEqualTo(progressSnapshots[i - 1]);
        }

        // Final progress should match total processed
        progressSnapshots.LastOrDefault().ShouldBe(processor.ProcessedRecords.Count);
    }

    [Test]
    public async ValueTask positions_track_correctly_during_processing(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(15, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"position-track-{Guid.NewGuid():N}";
        var positions = new List<LogPosition>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnRecordProcessed = record => {
            positions.Add(record.Position.LogPosition);
            if (record.Position.LogPosition >= simulation.Position)
                cts.Cancel();
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(cts.Token);

        // Assert - positions should be monotonically increasing
        for (var i = 1; i < positions.Count; i++) {
            positions[i].ShouldBeGreaterThan(positions[i - 1]);
        }

        // All positions should be valid
        foreach (var position in positions) {
            position.ShouldNotBe(LogPosition.Unset);
        }
    }

    [Test]
    public async ValueTask shutdown_context_contains_final_progress(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(10, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"position-final-{Guid.NewGuid():N}";

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
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(cts.Token);

        // Assert
        var finalProgress = processor.ShutdownContext!.Progress;
        finalProgress.Throughput.TotalRecords.ShouldBe(processor.ProcessedRecords.Count);
        finalProgress.Throughput.TotalBatches.ShouldBeGreaterThan(0);
        finalProgress.Throughput.Elapsed.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Test]
    public async ValueTask records_per_second_is_calculated(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(20, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"position-rps-{Guid.NewGuid():N}";

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
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(cts.Token);

        // Assert
        var finalProgress = processor.ShutdownContext!.Progress;

        // With records processed, RecordsPerSecond should be positive
        if (finalProgress.Throughput.TotalRecords > 0 &&
            finalProgress.Throughput.Elapsed > TimeSpan.Zero) {
            finalProgress.Throughput.RecordsPerSecond.ShouldBeGreaterThan(0);
        }
    }

    [Test]
    public async ValueTask average_batch_size_is_calculated(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(20, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"position-avgbatch-{Guid.NewGuid():N}";

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
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .WithBatching(new BatchingSettings { MaxSize = 5, Timeout = 50 })
            .RunAsync(cts.Token);

        // Assert
        var finalProgress = processor.ShutdownContext!.Progress;

        if (finalProgress.Throughput.TotalBatches > 0) {
            finalProgress.Throughput.AverageBatchSize.ShouldBeGreaterThan(0);
            finalProgress.Throughput.AverageBatchSize.ShouldBeLessThanOrEqualTo(5);
        }
    }
}
