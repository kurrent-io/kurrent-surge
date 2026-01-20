using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Processors;

/// <summary>
/// Tests for processor graceful shutdown and cancellation.
/// </summary>
[Category("Processors")]
[Category("Cancellation")]
[Category("Integration")]
public class CancellationTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask cancels_gracefully_on_token_cancellation(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(20, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"cancel-graceful-{Guid.NewGuid():N}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnRecordProcessed = _ => {
            if (processor.ProcessedRecords.Count >= 5)
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
        processor.ShutdownCalled.ShouldBeTrue();
        processor.ProcessedRecords.Count.ShouldBeGreaterThanOrEqualTo(5);
        processor.ProcessedRecords.Count.ShouldBeLessThan(simulation.Records.Count);
    }

    [Test]
    public async ValueTask reports_cancelled_shutdown_reason(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(15, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"cancel-reason-{Guid.NewGuid():N}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnRecordProcessed = _ => {
            if (processor.ProcessedRecords.Count >= 3)
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
        processor.ShutdownContext.ShouldNotBeNull();
        processor.ShutdownContext!.Reason.ShouldBe(ShutdownReason.Cancelled);
        processor.ShutdownContext.Error.ShouldBeNull();
    }

    [Test]
    public async ValueTask completes_current_batch_before_shutdown(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(20, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"cancel-batch-{Guid.NewGuid():N}";
        var recordsAtCancellation = 0;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnBatchProcessed = context => {
            if (processor.ProcessedRecords.Count >= 5) {
                recordsAtCancellation = processor.ProcessedRecords.Count;
                cts.Cancel();
            }
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .WithBatching(new BatchingSettings { MaxSize = 5, Timeout = 100 })
            .RunAsync(cts.Token);

        // Assert
        // After cancellation, processing should have completed the current batch
        // The final count should be at least the count at cancellation
        processor.ProcessedRecords.Count.ShouldBeGreaterThanOrEqualTo(recordsAtCancellation);
        processor.ShutdownCalled.ShouldBeTrue();
    }

    [Test]
    public async ValueTask processor_stops_processing_after_user_request(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(25, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"cancel-stop-{Guid.NewGuid():N}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnProcessRecordsCallback = context => {
            if (processor.ProcessedRecords.Count >= 10)
                context.StopProcessing(); // User-initiated stop
            return ValueTask.CompletedTask;
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(cts.Token);

        // Assert
        processor.ShutdownCalled.ShouldBeTrue();
        processor.ShutdownContext!.Reason.ShouldBe(ShutdownReason.Cancelled);
        processor.ProcessedRecords.Count.ShouldBeLessThan(simulation.Records.Count);
    }

    [Test]
    public async ValueTask cancellation_preserves_progress_metrics(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(15, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"cancel-metrics-{Guid.NewGuid():N}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnRecordProcessed = _ => {
            if (processor.ProcessedRecords.Count >= 7)
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
        var progress = processor.ShutdownContext!.Progress;
        progress.Throughput.TotalRecords.ShouldBe(processor.ProcessedRecords.Count);
        progress.Throughput.TotalBatches.ShouldBeGreaterThan(0);
        progress.Throughput.Elapsed.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Test]
    public async ValueTask immediate_cancellation_still_calls_shutdown(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(10, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"cancel-immediate-{Guid.NewGuid():N}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnInitializeCallback = _ => {
            cts.Cancel(); // Cancel immediately during initialization
            return ValueTask.CompletedTask;
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(cts.Token);

        // Assert
        processor.InitializeCalled.ShouldBeTrue();
        processor.ShutdownCalled.ShouldBeTrue();
        processor.ShutdownContext!.Reason.ShouldBe(ShutdownReason.Cancelled);
    }

    [Test]
    public async ValueTask timeout_triggers_cancellation(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(10, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"cancel-timeout-{Guid.NewGuid():N}";

        // Create a timeout after processing some records
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(linked.Token);

        // Assert
        processor.ShutdownCalled.ShouldBeTrue();
        processor.ShutdownContext!.Reason.ShouldBe(ShutdownReason.Cancelled);
    }
}
