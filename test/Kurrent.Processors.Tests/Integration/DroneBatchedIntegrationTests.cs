using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;
using Sdk.TestKit.Fixtures;

namespace Kurrent.Processors.Tests;

[Category("Processors")]
[Category("Integration")]
[Timeout(30_000)]
public class DroneBatchedIntegrationTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    #region ->> processing <<-

    [Test]
    public async ValueTask records_are_processed_in_batches(CancellationToken ct) {
        // Arrange
        const int seedCount    = 50;
        const int maxBatchSize = 10;
        var       batchWindow  = TimeSpan.FromMilliseconds(500);

        var data = await Fixture.SeedTestStream(seedCount, ct);

        using var cancellator   = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var       totalReceived = 0;

        var processor = new TestRecordBatchProcessor(maxBatchSize, batchWindow) {
            OnProcessBatch = batchCtx => {
                Interlocked.Add(ref totalReceived, batchCtx.Records.Count);

                if (totalReceived >= seedCount)
                    cancellator.Cancel();

                return ValueTask.CompletedTask;
            }
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.KurrentDB.AuthenticatedConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-batched-{PunyId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        });

        // Act
        await drone.RunAsync(cancellator.Token);

        // Assert
        await Assert.That(processor.Records.Count).IsEqualTo(seedCount);

        // Every batch except possibly the last must hit the size threshold
        for (var i = 0; i < processor.Batches.Length - 1; i++)
            await Assert.That(processor.Batches[i].Count).IsEqualTo(maxBatchSize);

        await Assert.That(processor.Batches[^1].Count).IsLessThanOrEqualTo(maxBatchSize);
    }

    [Test]
    public async ValueTask batch_window_yields_partial_batch_when_size_not_reached(CancellationToken ct) {
        // Arrange
        const int seedCount    = 3;
        const int maxBatchSize = 100; // intentionally larger than the seed
        var       batchWindow  = TimeSpan.FromMilliseconds(250);

        var data = await Fixture.SeedTestStream(seedCount, ct);

        using var cancellator   = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var       totalReceived = 0;

        var processor = new TestRecordBatchProcessor(maxBatchSize, batchWindow) {
            OnProcessBatch = batchCtx => {
                Interlocked.Add(ref totalReceived, batchCtx.Records.Count);

                if (totalReceived >= seedCount)
                    cancellator.Cancel();

                return ValueTask.CompletedTask;
            }
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.KurrentDB.AuthenticatedConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-window-{PunyId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        });

        // Act
        await drone.RunAsync(cancellator.Token);

        // Assert — first batch must be partial (window-driven), since seed < max
        await Assert.That(processor.Records.Count).IsEqualTo(seedCount);
        await Assert.That(processor.Batches[0].Count).IsLessThan(maxBatchSize);
    }

    #endregion

    #region ->> error handling <<-

    [Test]
    public async ValueTask processor_handles_exceptions_in_batch_and_stops_processing(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        var processor = new TestRecordBatchProcessor(maxBatchSize: 5, batchWindow: TimeSpan.FromMilliseconds(500)) {
            OnProcessBatch = _ => throw new InvalidOperationException("Process batch failed")
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.KurrentDB.AuthenticatedConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-batch-error-{PunyId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        });

        // Act & Assert — Drone wraps in AggregateException, inner is DroneException
        var aex = await Assert.That(async () => await drone.RunAsync(ct))
            .Throws<AggregateException>();

        await Assert.That(aex!.InnerExceptions[0]).IsTypeOf<DroneException>();
    }

    #endregion

    #region ->> status <<-

    [Test]
    public async ValueTask status_is_stopped_after_graceful_shutdown(CancellationToken ct) {
        // Arrange
        const int seedCount = 5;
        var       data      = await Fixture.SeedTestStream(seedCount, ct);

        using var cancellator   = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var       totalReceived = 0;

        var processor = new TestRecordBatchProcessor(maxBatchSize: 10, batchWindow: TimeSpan.FromMilliseconds(250)) {
            OnProcessBatch = batchCtx => {
                Interlocked.Add(ref totalReceived, batchCtx.Records.Count);

                if (totalReceived >= seedCount)
                    cancellator.Cancel();

                return ValueTask.CompletedTask;
            }
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.KurrentDB.AuthenticatedConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-batch-status-{PunyId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        }).Build();

        // Act
        await drone.RunAsync(cancellator.Token);

        // Assert
        await Assert.That(drone.Status).IsEqualTo(DroneStatus.Stopped);
    }

    #endregion

    #region ->> checkpointing <<-

    [Test]
    public async ValueTask interval_checkpoint_fires_during_batched_dispatch(CancellationToken ct) {
        // Arrange — short interval relative to batch window so multiple Interval markers
        // can race with batch dispatch. Verifies HandleCheckpoint flows through the
        // batched pipeline without breaking batch processing.
        const int seedCount    = 30;
        const int maxBatchSize = 10;

        var data = await Fixture.SeedTestStream(seedCount, ct);

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var totalReceived = 0;

        var processor = new TestRecordBatchProcessor(maxBatchSize, batchWindow: TimeSpan.FromMilliseconds(250)) {
            OnProcessBatch = async batchCtx => {
                Interlocked.Add(ref totalReceived, batchCtx.Records.Count);
                // Slow each batch so the 50ms interval timer can fire between batches.
                await Task.Delay(150, batchCtx.CancellationToken);

                if (totalReceived >= seedCount)
                    cancellator.Cancel();
            }
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.KurrentDB.AuthenticatedConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-batch-interval-{data.Stream}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.Interval          = TimeSpan.FromMilliseconds(50);
        });

        // Act
        await drone.RunAsync(cancellator.Token);

        // Assert — all records dispatched in batches, and at least one Interval checkpoint
        // fired during the run (Shutdown will also be in the list).
        await Assert.That(processor.Records.Count).IsEqualTo(data.Records.Length);
        await Assert.That(processor.Checkpoints.Any(c => c.Trigger == CheckpointTrigger.Interval)).IsTrue();
    }

    [Test]
    public async ValueTask shutdown_checkpoint_fires_with_positions_and_persists_for_recovery(CancellationToken ct) {
        // Arrange — Phase 1 processes everything and exits gracefully.
        // Shutdown checkpoint must persist the final positions so Phase 2 sees no records.
        const int seedCount   = 15;
        var       data        = await Fixture.SeedTestStream(seedCount, ct);
        var       processorId = $"drone-batch-shutdown-{PunyId.New()}";

        using var cancellator   = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var       totalReceived = 0;

        var phase1 = new TestRecordBatchProcessor(maxBatchSize: 5, batchWindow: TimeSpan.FromMilliseconds(250)) {
            OnProcessBatch = batchCtx => {
                Interlocked.Add(ref totalReceived, batchCtx.Records.Count);
                if (totalReceived >= seedCount)
                    cancellator.Cancel();
                return ValueTask.CompletedTask;
            }
        };

        var drone1 = KurrentProcessor.Create(phase1, opts => {
            opts.Connection(
                Fixture.KurrentDB.AuthenticatedConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        });

        // Act — Phase 1
        await drone1.RunAsync(cancellator.Token);

        // Assert — Phase 1 saw all records and committed positions (via any checkpoint trigger)
        await Assert.That(phase1.Records.Count).IsEqualTo(seedCount);
        await Assert.That(phase1.Checkpoints.Count).IsGreaterThan(0);

        var lastCheckpoint = phase1.Checkpoints[^1];
        await Assert.That(lastCheckpoint.Positions.Length).IsGreaterThan(0);

        // Arrange — Phase 2 resumes from Phase 1's checkpoint
        await Task.Delay(200, ct);

        var phase2 = new TestRecordBatchProcessor(maxBatchSize: 5, batchWindow: TimeSpan.FromMilliseconds(250));

        using var phase2Cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        phase2Cts.CancelAfter(2000);

        var drone2 = KurrentProcessor.Create(phase2, opts => {
            opts.Connection(
                Fixture.KurrentDB.AuthenticatedConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId         = processorId;
            opts.Subscription.Filter = RecordFilter.ByStream(data.Stream);
        });

        // Act — Phase 2 should see nothing because Phase 1's shutdown checkpoint already
        // persisted positions past the seed.
        await drone2.RunAsync(phase2Cts.Token);

        // Assert
        await Assert.That(phase2.Records.Count).IsEqualTo(0);
    }

    [Test]
    public async ValueTask pre_connected_cancellation_does_not_fire_on_checkpoint(CancellationToken ct) {
        // Arrange — cancel before the gRPC subscription confirms. The Connected-gate (B)
        // ensures the user does NOT receive OnCheckpoint(Shutdown) when OnConnected
        // never fired.
        var data = await Fixture.SeedTestStream(5, ct);

        // Cancel synchronously before RunAsync starts, so by the time the source
        // registers its callback, ct is already canceled. The registration fires
        // immediately, but _connectedObserved is still 0 → channel faulted, drone
        // takes the OperationCanceledException path through InitiateShutdown.
        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await cancellator.CancelAsync();

        var processor = new TestRecordBatchProcessor(maxBatchSize: 10, batchWindow: TimeSpan.FromMilliseconds(250));

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.KurrentDB.AuthenticatedConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-batch-pre-conn-{PunyId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        });

        // Act
        try { await drone.RunAsync(cancellator.Token); }
        catch (OperationCanceledException) { /* expected */ }

        // Assert — neither OnConnected nor OnCheckpoint fired (Shutdown gate skipped)
        await Assert.That(processor.Connected).IsNull();
        await Assert.That(processor.Checkpoints.Count).IsEqualTo(0);
    }

    #endregion

    #region ->> status (more) <<-

    [Test]
    public async ValueTask status_is_error_after_batch_failure(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        var processor = new TestRecordBatchProcessor(maxBatchSize: 5, batchWindow: TimeSpan.FromMilliseconds(500)) {
            OnProcessBatch = _ => throw new InvalidOperationException("boom")
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.KurrentDB.AuthenticatedConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-batch-errstat-{PunyId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        }).Build();

        // Act
        await Assert.That(async () => await drone.RunAsync(ct))
            .Throws<AggregateException>();

        // Assert
        await Assert.That(drone.Status).IsEqualTo(DroneStatus.Error);
    }

    #endregion
}
