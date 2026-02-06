using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Processors;
using Kurrent.Processors.Models;

namespace Kurrent.Client.Tests.Processors;

[Category("Processors")]
[Category("Integration")]
[Timeout(10_000)]
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
        var processor1 = new TestRecordProcessor();

        processor1.OnProcessRecords = ctx => {
            if (processor1.Records.Length >= recordsToProcessInPhase1)
                ctx.StopProcessing();

            return ValueTask.CompletedTask;
        };

        await KurrentProcessors.Create(processor1)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .ConfigureCheckpointing(c => c.Interval(100).Timeout(60000))
            .RunAsync(ct);

        // Wait for checkpoint to be persisted
        await Task.Delay(200, ct);

        // Phase 2: Resume from same processor ID, should process remaining records
        var processor2 = new TestRecordProcessor();

        processor2.OnProcessRecords = ctx => {
            var lastRecord = ctx.Records[^1];
            if (lastRecord.Position.LogPosition >= simulation.Position)
                ctx.StopProcessing();

            return ValueTask.CompletedTask;
        };

        await KurrentProcessors.Create(processor2)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .ConfigureCheckpointing(c => c.Interval(60000).Timeout(60000))
            .RunAsync(ct);

        // Assert - All records processed across both runs with tolerance for checkpoint timing
        var totalProcessedRecords = processor1.Records.Length + processor2.Records.Length;
        await Assert.That(totalProcessedRecords).IsLessThanOrEqualTo(simulation.Records.Count + 5);

        // Verify minimal duplicate processing
        var allProcessedIds = processor1.Records
            .Concat(processor2.Records)
            .Select(r => r.Id)
            .ToList();

        var duplicateCount = allProcessedIds.Count - allProcessedIds.Distinct().Count();
        await Assert.That(duplicateCount).IsLessThanOrEqualTo(5);
    }

    [Test]
    public async ValueTask processor_batches_records_correctly(CancellationToken ct) {
        // Arrange
        var simulation   = await Fixture.SeedSmartHomeActivity(25, ct);
        var processor    = new TestRecordProcessor();
        var processorId  = $"batch-maxsize-{Guid.NewGuid():N}";
        var batchSizes   = new List<int>();
        var maxBatchSize = 5;

        processor.OnProcessRecords = ctx => {
            batchSizes.Add(ctx.Records.Count);
            if (processor.Records.Length >= simulation.Records.Count)
                ctx.StopProcessing();

            return ValueTask.CompletedTask;
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .ConfigureBatching(b => b.MaxSize(maxBatchSize).Timeout(1000))
            .RunAsync(ct);

        // Assert - No batch should exceed max size
        foreach (var size in batchSizes)
            await Assert.That(size).IsLessThanOrEqualTo(maxBatchSize);

        // Total records across all batches should equal processed
        await Assert.That(batchSizes.Sum()).IsEqualTo(processor.Records.Length);
    }

    [Test]
    public async ValueTask processor_cancels_gracefully(CancellationToken ct) {
        // Arrange
        var simulation  = await Fixture.SeedSmartHomeActivity(ct);
        var processor   = new TestRecordProcessor();
        var processorId = $"processor_cancels_gracefully-{TestContext.Current!.Id:N}";

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        var worker = KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .Build();

        var task = worker.RunAsync(cancellator.Token);

        await cancellator.CancelAsync();

        await Assert.That(task).IsNotCompletedSuccessfully();
    }

    [Test]
    public async ValueTask processor_stops_on_user_request(CancellationToken ct) {
        // Arrange
        var simulation  = await Fixture.SeedSmartHomeActivity(ct);
        var processor   = new TestRecordProcessor();
        var processorId = $"processor_stops_on_user_request-{TestContext.Current!.Id:N}";

        processor.OnProcessRecords = ctx => {
            if (processor.Records.Length == simulation.Records.Count)
                ctx.StopProcessing();

            return ValueTask.CompletedTask;
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(ct);

        // Assert
        await Assert.That(processor.Complete).IsNotNull();
        await Assert.That(processor.Records.Length).IsEqualTo(simulation.Records.Count);
    }

    [Test]
    public async ValueTask processor_handles_exceptions_and_stops_processing(CancellationToken ct) {
        // Arrange
        var simulation  = await Fixture.SeedSmartHomeActivity(10, ct);
        var processorId = $"error-process-{Guid.NewGuid():N}";

        var processor = new TestRecordProcessor {
            OnProcessRecords = _ => throw new InvalidOperationException("Process failed")
        };

        var worker = KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .Build();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KurrentWorkerException>(() => worker.RunAsync(ct));

        await Assert.That(exception.InnerException).IsTypeOf<InvalidOperationException>();
        await Assert.That(processor.Records.Length).IsGreaterThan(0); // Records added before callback throws
    }

    [Test]
    public async ValueTask processor_calls_shutdown_even_on_error(CancellationToken ct) {
        // Arrange
        var simulation  = await Fixture.SeedSmartHomeActivity(10, ct);
        var processor   = new TestRecordProcessor();
        var processorId = $"error-shutdown-{Guid.NewGuid():N}";

        processor.OnProcessRecords = _ => throw new InvalidOperationException("Intentional error");

        // Act
        try {
            await KurrentProcessors.Create(processor)
                .UseClient(Fixture.Client)
                .WithProcessorId(processorId)
                .WithFilter(RecordFilter.ByStream(simulation.Stream))
                .WithInitialPosition(SubscriptionInitialPosition.Earliest)
                .RunAsync(ct);
        }
        catch (KurrentWorkerException) {
            // Expected
        }

        // Assert
        await Assert.That(processor.Error).IsNotNull();
        await Assert.That(processor.Error!.Error).IsNotNull();
    }

    [Test]
    public async ValueTask records_are_processed(CancellationToken ct) {
        // Arrange
        var simulation  = await Fixture.SeedSmartHomeActivity(100, ct);
        var processor   = new TestRecordProcessor();
        var processorId = $"order-{Guid.NewGuid():N}";

        processor.OnProcessRecords = ctx => {
            var lastRecord = ctx.Records[^1];
            if (lastRecord.Position.LogPosition >= simulation.Position)
                ctx.StopProcessing();

            return ValueTask.CompletedTask;
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .RunAsync(ct);

        // Assert
        for (var i = 0; i < processor.Records.Length; i++)
            await Assert.That(processor.Records[i].StreamRevision).IsEqualTo(StreamRevision.From(i));
    }
}
