using System.Collections.Concurrent;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Streaming.Models;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Testing.ArcadeRacer;

namespace Kurrent.Streaming.Tests;

[Category("Processors")]
[Category("Integration")]
[Timeout(120_000)]
public class MessageProcessorIntegrationTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    #region ->> typed dispatch <<-

    [Test]
    public async ValueTask dispatches_to_registered_async_handler(CancellationToken ct) {
        // Arrange
        var data     = await Fixture.SeedTestStream(10, ct);
        var captured = new ConcurrentBag<LapCompleted>();

        var processor = new MessageProcessor()
            .Process<LapCompleted>(async (msg, ctx) => {
                captured.Add(msg);

                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();

                await ValueTask.CompletedTask;
            })
            .Fallback(ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();
            });

        var drone = CreateDrone(processor, data);

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(captured.Count).IsGreaterThan(0);
        await Assert.That(captured.All(e => e.Lap > 0)).IsTrue();
    }

    [Test]
    public async ValueTask dispatches_to_registered_sync_handler(CancellationToken ct) {
        // Arrange
        var data     = await Fixture.SeedTestStream(10, ct);
        var captured = new ConcurrentBag<GridFormed>();

        var processor = new MessageProcessor()
            .Process<GridFormed>((msg, ctx) => {
                captured.Add(msg);

                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();
            })
            .Fallback(ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();
            });

        var drone = CreateDrone(processor, data);

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(captured.Count).IsEqualTo(1);
        await Assert.That(captured.First().Cars.Length).IsGreaterThan(0);
    }

    [Test]
    public async ValueTask dispatches_to_correct_handler_when_multiple_registered(CancellationToken ct) {
        // Arrange
        var data          = await Fixture.SeedTestStream(ct);
        var gridCount     = 0;
        var lapCount      = 0;
        var overtakeCount = 0;

        var processor = new MessageProcessor()
            .Process<GridFormed>((_, ctx) => {
                Interlocked.Increment(ref gridCount);

                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();
            })
            .Process<LapCompleted>((_, ctx) => {
                Interlocked.Increment(ref lapCount);

                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();
            })
            .Process<OvertakePerformed>((_, ctx) => {
                Interlocked.Increment(ref overtakeCount);

                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();
            })
            .Fallback(ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();
            });

        var drone = CreateDrone(processor, data);

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(gridCount).IsEqualTo(1);
        await Assert.That(lapCount).IsGreaterThan(0);

        var totalDispatched = gridCount + lapCount + overtakeCount;
        await Assert.That(totalDispatched).IsLessThanOrEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask last_registration_wins_for_same_type(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(10, ct);
        var firstCalled = 0;
        var lastCalled  = 0;

        var processor = new MessageProcessor()
            .Process<LapCompleted>((_, _) => { Interlocked.Increment(ref firstCalled); })
            .Process<LapCompleted>((_, ctx) => {
                Interlocked.Increment(ref lastCalled);

                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();
            })
            .Fallback(ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();
            });

        var drone = CreateDrone(processor, data);

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(firstCalled).IsEqualTo(0);
        await Assert.That(lastCalled).IsGreaterThan(0);
    }

    #endregion // typed dispatch

    #region ->> fallback <<-

    [Test]
    public async ValueTask invokes_fallback_for_unregistered_type(CancellationToken ct) {
        // Arrange
        var data          = await Fixture.SeedTestStream(10, ct);
        var fallbackCount = 0;

        // register a handler for a type that will never appear in the stream
        var processor = new MessageProcessor()
            .Process<RaceInsight>((_, _) => { })
            .Fallback(ctx => {
                Interlocked.Increment(ref fallbackCount);

                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();
            });

        var drone = CreateDrone(processor, data);

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(fallbackCount).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask does_not_invoke_fallback_when_handler_matches(CancellationToken ct) {
        // Arrange
        var data          = await Fixture.SeedTestStream(10, ct);
        var handlerCalled = 0;
        var fallbackCount = 0;

        var processor = new MessageProcessor()
            .Process<GridFormed>((_, ctx) => {
                Interlocked.Increment(ref handlerCalled);

                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();
            })
            .Fallback(ctx => {
                Interlocked.Increment(ref fallbackCount);

                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();
            });

        var drone = CreateDrone(processor, data);

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(handlerCalled).IsEqualTo(1);
        await Assert.That(fallbackCount).IsEqualTo(data.Records.Length - 1);
    }

    [Test]
    public async ValueTask completes_silently_when_no_handler_and_no_fallback(CancellationToken ct) {
        // Arrange
        var data           = await Fixture.SeedTestStream(5, ct);
        var processedCount = 0;

        // register only a handler for a type that won't appear, no fallback
        // the drone should process all records without error, dispatching none
        var processor = new MessageProcessor()
            .Process<RaceInsight>((_, _) => {
                Interlocked.Increment(ref processedCount);
            });

        // use TestableRecordProcessor wrapper to stop at the end
        var testable = new TestableRecordProcessor {
            OnProcessRecord = async ctx => {
                await ((IRecordProcessor)processor).ProcessRecord(ctx);

                if (ctx.Record.Position.LogPosition >= data.Position) {
                    // Track explicitly so TestableRecordProcessor still collects
                    // this record — StopProcessing suppresses auto-collection.
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }
            }
        };

        var drone = CreateDrone(testable, data);

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(processedCount).IsEqualTo(0);
        await Assert.That(testable.Records.Length).IsEqualTo(data.Records.Length);
    }

    #endregion // fallback

    #region ->> can process <<-

    [Test]
    public async ValueTask can_process_returns_true_for_registered_decoded_type(CancellationToken ct) {
        // Arrange
        var data   = await Fixture.SeedTestStream(10, ct);
        var result = false;

        var processor = new MessageProcessor()
            .Process<GridFormed>((_, _) => { });

        var testable = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.IsDecoded && ctx.Record.ValueType == typeof(GridFormed))
                    result = processor.CanProcess(ctx.Record);

                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();

                return ValueTask.CompletedTask;
            }
        };

        var drone = CreateDrone(testable, data);

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async ValueTask can_process_returns_false_for_unregistered_type(CancellationToken ct) {
        // Arrange
        var data            = await Fixture.SeedTestStream(10, ct);
        var checkedAnything = false;
        var allFalse        = true;

        // register only RaceInsight — which never appears in a normal race simulation
        var processor = new MessageProcessor()
            .Process<RaceInsight>((_, _) => { });

        var testable = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.IsDecoded && ctx.Record.ValueType != typeof(RaceInsight)) {
                    checkedAnything = true;

                    if (processor.CanProcess(ctx.Record))
                        allFalse = false;
                }

                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();

                return ValueTask.CompletedTask;
            }
        };

        var drone = CreateDrone(testable, data);

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(checkedAnything).IsTrue();
        await Assert.That(allFalse).IsTrue();
    }

    #endregion // can process

    #region ->> context access <<-

    [Test]
    public async ValueTask handler_receives_full_context(CancellationToken ct) {
        // Arrange
        var data     = await Fixture.SeedTestStream(10, ct);
        var captured = new ConcurrentBag<(GridFormed Message, ProcessRecordContext Context)>();

        var processor = new MessageProcessor()
            .Process<GridFormed>((msg, ctx) => { captured.Add((msg, ctx)); })
            .Fallback(ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();
            });

        var drone = CreateDrone(processor, data);

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(captured.Count).IsEqualTo(1);

        var (message, context) = captured.First();
        await Assert.That(context.Record.IsDecoded).IsTrue();
        await Assert.That(context.Record.Value).IsNotNull();
        await Assert.That(context.ProcessorId).IsNotEqualTo(ProcessorId.Unset);
        await Assert.That(message.Cars.Length).IsGreaterThan(0);
    }

    #endregion // context access

    #region ->> subclass pattern <<-

    [Test]
    public async ValueTask subclass_registration_works(CancellationToken ct) {
        // Arrange
        var data      = await Fixture.SeedTestStream(10, ct);
        var processor = new TestRaceProcessor(data.Position);

        var drone = CreateDrone(processor, data);

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(processor.Laps.Count).IsGreaterThan(0);
        await Assert.That(processor.Laps.All(l => l.Lap > 0)).IsTrue();
    }

    #endregion // subclass pattern

    #region ->> lifecycle hooks <<-

    [Test]
    public async ValueTask on_checkpoint_is_invoked(CancellationToken ct) {
        // Arrange
        var data             = await Fixture.SeedTestStream(5, ct);
        var checkpointCalled = false;

        var processor = new MessageProcessor()
            .Fallback(ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();
            })
            .OnCheckpoint(_ => { checkpointCalled = true; });

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"msg-ckpt-{TestId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.Interval          = TimeSpan.FromMilliseconds(100);
        });

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(checkpointCalled).IsTrue();
    }

    [Test]
    public async ValueTask lifecycle_hooks_are_noop_when_not_registered(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        // no OnCheckpoint registered — should not throw
        var processor = new MessageProcessor()
            .Fallback(ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();
            });

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"msg-noop-{TestId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.Interval          = TimeSpan.FromMilliseconds(100);
        });

        // Act & Assert — should complete without error
        await drone.RunAsync(ct);
    }

    #endregion // lifecycle hooks

    #region ->> Test helpers <<-

    KurrentProcessor CreateDrone(IRecordProcessor processor, SeededTestStream data) =>
        KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => {
                    client.LoggerFactory = Fixture.LoggerFactory;
                    // Typed handlers dispatch on the decoded message type, so this self-built client
                    // must opt into the Testkit catalog (race contracts) — discovered but not
                    // always-active — to resolve incoming records to RaceCar et al.
                    client.Messages.UseTestkitCatalog();
                });

            opts.ProcessorId                  = $"msg-proc-{TestId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.Interval          = TimeSpan.FromHours(1);
        });

    class TestRaceProcessor : MessageProcessor {
        public ConcurrentBag<LapCompleted> Laps { get; } = [];

        public TestRaceProcessor(LogPosition stopAfter) {
            Process<LapCompleted>((msg, ctx) => {
                Laps.Add(msg);

                if (ctx.Record.Position.LogPosition >= stopAfter)
                    ctx.StopProcessing();
            });

            Fallback(ctx => {
                if (ctx.Record.Position.LogPosition >= stopAfter)
                    ctx.StopProcessing();
            });
        }
    }

    #endregion // Test helpers
}
