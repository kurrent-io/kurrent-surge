using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Processors;

/// <summary>
/// Tests for processor batching behavior (MaxSize and timeout).
/// </summary>
[Category("Processors")]
[Category("Batching")]
[Category("Integration")]
public class BatchingTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask batches_records_up_to_max_size(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(25, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"batch-maxsize-{Guid.NewGuid():N}";
        var batchSizes = new List<int>();
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
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .WithBatching(new BatchingSettings { MaxSize = maxBatchSize, Timeout = 1000 })
            .RunAsync(cts.Token);

        // Assert - no batch should exceed max size
        foreach (var size in batchSizes) {
            size.ShouldBeLessThanOrEqualTo(maxBatchSize);
        }

        // Total records across all batches should equal processed
        batchSizes.Sum().ShouldBe(processor.ProcessedRecords.Count);
    }

    [Test]
    public async ValueTask delivers_batch_after_timeout(CancellationToken ct) {
        // Arrange - small number of records that won't fill a batch
        var simulation = await Fixture.SeedSmartHomeActivity(3, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"batch-timeout-{Guid.NewGuid():N}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnRecordProcessed = record => {
            if (record.Position.LogPosition >= simulation.Position)
                cts.Cancel();
        };

        // Act - large batch size, short timeout
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .WithBatching(new BatchingSettings { MaxSize = 100, Timeout = 50 })
            .RunAsync(cts.Token);

        // Assert - records should still be delivered (via timeout, not max size)
        processor.ProcessedRecords.Count.ShouldBe(simulation.Records.Count);
        processor.ProcessRecordsCalls.ShouldBeGreaterThan(0);
    }

    [Test]
    public async ValueTask delivers_single_record_batches_when_sparse(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(1, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"batch-sparse-{Guid.NewGuid():N}";

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
            .WithBatching(new BatchingSettings { MaxSize = 10, Timeout = 50 })
            .RunAsync(cts.Token);

        // Assert
        processor.ProcessedRecords.Count.ShouldBe(1);
    }

    [Test]
    [Arguments(1)]
    [Arguments(5)]
    [Arguments(10)]
    [Arguments(50)]
    public async ValueTask respects_various_batch_sizes(int maxBatchSize, CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(30, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"batch-sizes-{maxBatchSize}-{Guid.NewGuid():N}";
        var batchSizes = new List<int>();

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
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .WithBatching(new BatchingSettings { MaxSize = maxBatchSize, Timeout = 1000 })
            .RunAsync(cts.Token);

        // Assert
        foreach (var size in batchSizes) {
            size.ShouldBeLessThanOrEqualTo(maxBatchSize);
            size.ShouldBeGreaterThan(0);
        }
    }

    [Test]
    public async ValueTask multiple_batches_are_processed_for_large_record_count(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(30, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"batch-multiple-{Guid.NewGuid():N}";
        var maxBatchSize = 5;

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
            .WithBatching(new BatchingSettings { MaxSize = maxBatchSize, Timeout = 100 })
            .RunAsync(cts.Token);

        // Assert - with 30 records and batch size 5, expect at least 6 batches
        processor.ProcessRecordsCalls.ShouldBeGreaterThanOrEqualTo(simulation.Records.Count / maxBatchSize);
        processor.ProcessedRecords.Count.ShouldBe(simulation.Records.Count);
    }

    [Test]
    public async ValueTask batch_context_contains_correct_record_count(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(15, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"batch-context-{Guid.NewGuid():N}";
        var actualBatchRecordCounts = new List<int>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        processor.OnBatchProcessed = context => {
            actualBatchRecordCounts.Add(context.Records.Count);
            if (processor.ProcessedRecords.Count >= simulation.Records.Count)
                cts.Cancel();
        };

        // Act
        await KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .WithBatching(new BatchingSettings { MaxSize = 5, Timeout = 100 })
            .RunAsync(cts.Token);

        // Assert - each batch should have at least 1 record
        foreach (var count in actualBatchRecordCounts) {
            count.ShouldBeGreaterThan(0);
        }

        // Sum should equal total processed
        actualBatchRecordCounts.Sum().ShouldBe(processor.ProcessedRecords.Count);
    }
}
