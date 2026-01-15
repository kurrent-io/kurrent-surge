using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Processors;

/// <summary>
/// Tests for processor error handling and exception scenarios.
/// </summary>
[Category("Processors")]
[Category("ErrorHandling")]
[Category("Integration")]
public class ErrorHandlingTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask reports_error_shutdown_reason_on_exception(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(10, ct);
        var processor = new ThrowingRecordProcessor {
            ThrowOnProcessRecords = new InvalidOperationException("Test error")
        };
        var processorId = $"error-reason-{Guid.NewGuid():N}";

        // Act & Assert
        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await KurrentProcessors.Create(processor)
                .UseClient(Fixture.Client)
                .WithProcessorId(processorId)
                .WithFilter(RecordFilter.ByStream(simulation.Stream))
                .WithInitialPosition(SubscriptionInitialPosition.Earliest)
                .RunAsync(ct)
        );

        exception.Message.ShouldBe("Test error");
    }

    [Test]
    public async ValueTask shutdown_called_even_on_error(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(10, ct);
        var shutdownCalled = false;
        var shutdownContext = (ShutdownContext?)null;

        var processor = new TrackingRecordProcessor();
        var processorId = $"error-shutdown-{Guid.NewGuid():N}";

        processor.OnProcessRecordsCallback = _ => throw new InvalidOperationException("Intentional error");
        processor.OnShutdownCallback = ctx => {
            shutdownCalled = true;
            shutdownContext = ctx;
            return ValueTask.CompletedTask;
        };

        // Act
        try {
            await KurrentProcessors.Create(processor)
                .UseClient(Fixture.Client)
                .WithProcessorId(processorId)
                .WithFilter(RecordFilter.ByStream(simulation.Stream))
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
    public async ValueTask exception_in_initialize_stops_processing(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(5, ct);
        var initError = new InvalidOperationException("Init failed");

        var processor = new ThrowingRecordProcessor {
            ThrowOnInitialize = initError
        };
        var processorId = $"error-init-{Guid.NewGuid():N}";

        // Act & Assert
        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await KurrentProcessors.Create(processor)
                .UseClient(Fixture.Client)
                .WithProcessorId(processorId)
                .WithFilter(RecordFilter.ByStream(simulation.Stream))
                .WithInitialPosition(SubscriptionInitialPosition.Earliest)
                .RunAsync(ct)
        );

        exception.ShouldBe(initError);
        processor.ProcessRecordsCallCount.ShouldBe(0);
    }

    [Test]
    public async ValueTask exception_in_process_records_stops_processing(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(10, ct);
        var processError = new InvalidOperationException("Process failed");

        var processor = new ThrowingRecordProcessor {
            ThrowOnProcessRecords = processError
        };
        var processorId = $"error-process-{Guid.NewGuid():N}";

        // Act & Assert
        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await KurrentProcessors.Create(processor)
                .UseClient(Fixture.Client)
                .WithProcessorId(processorId)
                .WithFilter(RecordFilter.ByStream(simulation.Stream))
                .WithInitialPosition(SubscriptionInitialPosition.Earliest)
                .RunAsync(ct)
        );

        exception.ShouldBe(processError);
        processor.ProcessRecordsCallCount.ShouldBe(1); // Called once before throwing
    }

    [Test]
    public async ValueTask factory_exception_is_wrapped(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(5, ct);
        var factory = new FailingProcessorFactory();
        var processorId = $"error-factory-{Guid.NewGuid():N}";

        // Act & Assert
        await Should.ThrowAsync<FailedToCreateWorkerException>(async () =>
            await KurrentProcessors.Create(factory)
                .UseClient(Fixture.Client)
                .WithProcessorId(processorId)
                .WithFilter(RecordFilter.ByStream(simulation.Stream))
                .WithInitialPosition(SubscriptionInitialPosition.Earliest)
                .RunAsync(ct)
        );
    }

    [Test]
    public async ValueTask shutdown_error_context_contains_exception(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(5, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"error-context-{Guid.NewGuid():N}";
        var intentionalError = new InvalidOperationException("Intentional test error");

        processor.OnProcessRecordsCallback = _ => throw intentionalError;

        // Act
        try {
            await KurrentProcessors.Create(processor)
                .UseClient(Fixture.Client)
                .WithProcessorId(processorId)
                .WithFilter(RecordFilter.ByStream(simulation.Stream))
                .WithInitialPosition(SubscriptionInitialPosition.Earliest)
                .RunAsync(ct);
        }
        catch (InvalidOperationException) {
            // Expected
        }

        // Assert
        processor.ShutdownContext.ShouldNotBeNull();
        processor.ShutdownContext!.Error.ShouldNotBeNull();
        processor.ShutdownContext.Error.ShouldBe(intentionalError);
    }

    [Test]
    public async ValueTask multiple_errors_reports_first_error(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(10, ct);
        var processor = new TrackingRecordProcessor();
        var processorId = $"error-multi-{Guid.NewGuid():N}";
        var firstError = new InvalidOperationException("First error");

        var errorCount = 0;
        processor.OnProcessRecordsCallback = _ => {
            errorCount++;
            if (errorCount == 1)
                throw firstError;
            return ValueTask.CompletedTask;
        };

        // Act
        try {
            await KurrentProcessors.Create(processor)
                .UseClient(Fixture.Client)
                .WithProcessorId(processorId)
                .WithFilter(RecordFilter.ByStream(simulation.Stream))
                .WithInitialPosition(SubscriptionInitialPosition.Earliest)
                .RunAsync(ct);
        }
        catch (InvalidOperationException ex) {
            ex.ShouldBe(firstError);
        }

        // Assert - only one batch processed (first error)
        processor.ProcessRecordsCalls.ShouldBe(1);
    }

    [Test]
    public async ValueTask worker_status_is_failed_after_error(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedSmartHomeActivity(5, ct);
        var processor = new ThrowingRecordProcessor {
            ThrowOnProcessRecords = new InvalidOperationException("Test error")
        };
        var processorId = $"error-status-{Guid.NewGuid():N}";

        var worker = KurrentProcessors.Create(processor)
            .UseClient(Fixture.Client)
            .WithProcessorId(processorId)
            .WithFilter(RecordFilter.ByStream(simulation.Stream))
            .WithInitialPosition(SubscriptionInitialPosition.Earliest)
            .Build();

        // Act
        try {
            await using (worker) {
                await worker.RunAsync(ct);
            }
        }
        catch (InvalidOperationException) {
            // Expected
        }

        // Assert
        worker.Status.ShouldBe(ProcessorWorkerStatus.Failed);
    }

    /// <summary>
    /// A processor factory that always throws.
    /// </summary>
    sealed class FailingProcessorFactory : IRecordProcessorFactory {
        public IRecordProcessor CreateProcessor() =>
            throw new InvalidOperationException("Factory failed to create processor");
    }
}
