using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Processors;

/// <summary>
/// Tests for processor lifecycle method invocation.
/// </summary>
[Category("Processors")]
[Category("Lifecycle")]
[Category("Integration")]
public class ProcessorLifecycleTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask initializes_before_processing_records(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(5, ct);
        var processor = new TrackingRecordProcessor();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnBatchProcessed = _ => cts.Cancel();

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId($"lifecycle-init-{Guid.NewGuid():N}")
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(cts.Token);

        // Assert
        processor.InitializeCalledFirst.ShouldBeTrue();
        processor.InitializeCalled.ShouldBeTrue();
    }

    [Test]
    public async ValueTask processes_records_from_seeded_stream(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(10, ct);
        var processor = new TrackingRecordProcessor();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnRecordProcessed = record => {
            if (record.Position.LogPosition >= simulation.Position)
                cts.Cancel();
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId($"lifecycle-process-{Guid.NewGuid():N}")
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(cts.Token);

        // Assert
        processor.ProcessedRecords.Count.ShouldBe(simulation.Records.Count);
    }

    [Test]
    public async ValueTask calls_shutdown_on_cancellation(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(20, ct);
        var processor = new TrackingRecordProcessor();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnRecordProcessed = _ => {
            if (processor.ProcessedRecords.Count >= 5)
                cts.Cancel();
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId($"lifecycle-shutdown-{Guid.NewGuid():N}")
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(cts.Token);

        // Assert
        processor.ShutdownCalled.ShouldBeTrue();
        processor.ShutdownContext.ShouldNotBeNull();
        processor.ShutdownContext!.Reason.ShouldBe(ShutdownReason.Cancelled);
    }

    [Test]
    public async ValueTask provides_correct_metrics_on_shutdown(CancellationToken ct) {
        // Arrange
        var recordCount = 7;
        var simulation = await Fixture.SeedSmartHomeActivity(recordCount, ct);
        var processor = new TrackingRecordProcessor();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnRecordProcessed = record => {
            if (record.Position.LogPosition >= simulation.Position)
                cts.Cancel();
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId($"lifecycle-metrics-{Guid.NewGuid():N}")
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(cts.Token);

        // Assert
        processor.ShutdownCalled.ShouldBeTrue();
        processor.ShutdownContext.ShouldNotBeNull();

        var progress = processor.ShutdownContext!.Progress;
        progress.Throughput.TotalRecords.ShouldBe(simulation.Records.Count);
        progress.Throughput.Elapsed.ShouldBeGreaterThan(TimeSpan.Zero);
        progress.Throughput.TotalBatches.ShouldBeGreaterThan(0);
    }

    [Test]
    public async ValueTask transitions_through_expected_states(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(5, ct);
        var processor = new TrackingRecordProcessor();
        var observedStatuses = new List<ProcessorWorkerStatus>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        var worker = KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId($"lifecycle-states-{Guid.NewGuid():N}")
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .Build();

        observedStatuses.Add(worker.Status); // Should be Initializing

        processor.OnRecordProcessed = record => {
            if (!observedStatuses.Contains(worker.Status))
                observedStatuses.Add(worker.Status);
            if (record.Position.LogPosition >= simulation.Position)
                cts.Cancel();
        };

        await using (worker) {
            await worker.RunAsync(cts.Token);
        }

        // Assert - should have transitioned through Initializing → Processing → Disconnected
        observedStatuses.ShouldContain(ProcessorWorkerStatus.Initializing);
        worker.Status.ShouldBeOneOf(ProcessorWorkerStatus.Disconnected, ProcessorWorkerStatus.Failed);
    }

    [Test]
    public async ValueTask uses_factory_to_create_processor(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(5, ct);
        var factory = new TrackingRecordProcessorFactory();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        await KurrentProcessors.Create(factory)
            .UseClient(Fixture.Client)
            .WithProcessorId($"lifecycle-factory-{Guid.NewGuid():N}")
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .ConfigureProcessing(settings => settings with {
                Batching = new BatchingSettings { MaxSize = 100, Timeout = 50 }
            })
            .RunAsync(cts.Token);

        // Assert
        factory.CreateCount.ShouldBe(1);
        factory.LastCreatedProcessor.ShouldNotBeNull();
    }

    [Test]
    public async ValueTask processor_id_and_worker_id_are_accessible(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(3, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"lifecycle-ids-{Guid.NewGuid():N}";
        var workerId = $"worker-{Guid.NewGuid():N}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnBatchProcessed = _ => cts.Cancel();

        // Act
        var worker = KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithWorkerId(workerId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .Build();

        await using (worker) {
            await worker.RunAsync(cts.Token);
        }

        // Assert
        worker.ProcessorId.ShouldBe(processorId);
        worker.WorkerId.ShouldBe(workerId);
    }

    [Test]
    [Arguments(1)]
    [Arguments(5)]
    [Arguments(10)]
    [Arguments(25)]
    public async ValueTask processes_correct_number_of_records(int recordCount, CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(recordCount, ct);
        var processor = new TrackingRecordProcessor();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnRecordProcessed = record => {
            if (record.Position.LogPosition >= simulation.Position)
                cts.Cancel();
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId($"lifecycle-count-{Guid.NewGuid():N}")
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(cts.Token);

        // Assert
        processor.ProcessedRecords.Count.ShouldBe(simulation.Records.Count);
        processor.ShutdownContext!.Progress.Throughput.TotalRecords.ShouldBe(simulation.Records.Count);
    }

    [Test]
    public async ValueTask records_are_processed_in_order(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(10, ct);
        var processor = new TrackingRecordProcessor();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnRecordProcessed = record => {
            if (record.Position.LogPosition >= simulation.Position)
                cts.Cancel();
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId($"lifecycle-order-{Guid.NewGuid():N}")
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(cts.Token);

        // Assert
        for (var i = 0; i < processor.ProcessedRecords.Count; i++) {
            var record = processor.ProcessedRecords[i];
            record.StreamRevision.ShouldBe(StreamRevision.From(i));
        }
    }
}
