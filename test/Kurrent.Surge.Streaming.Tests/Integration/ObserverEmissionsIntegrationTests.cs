using System.Diagnostics.Metrics;
using Kurrent.Surge.Client.Streams;
using Kurrent.Processors.Configuration;
using Kurrent.Surge.Streaming.Models;
using Kurrent.Surge.Streaming.Tests.Fakes;

namespace Kurrent.Surge.Streaming.Tests;

[Category("Processors")]
[Category("Integration")]
[Timeout(120_000)]
public class ObserverEmissionsIntegrationTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask user_observer_registered_via_observe_receives_events(CancellationToken ct) {
        // Arrange — an observer registered through opts.Observe sits on the seam beside the built-in
        // telemetry and must receive a ready/completed pair for every record the handler processes.
        var data     = await Fixture.SeedTestStream(50, ct);
        var observer = new CountingObserver();

        var processor = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(Fixture.ConnectionString, client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"observe-{data.Stream}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.Interval          = TimeSpan.FromHours(1);
            opts.Observe(observer);
        });

        // Act
        await drone.RunAsync(ct);

        // Assert — every record dispatched to the handler reached the observer, ready then completed
        await Assert.That(observer.Ready).IsGreaterThan(0);
        await Assert.That(observer.Completed).IsEqualTo(observer.Ready);
    }

    [Test]
    public async ValueTask meter_factory_routes_processor_metrics(CancellationToken ct) {
        // Arrange — the caller-supplied IMeterFactory must be the one the built-in telemetry records on,
        // so records_processed measurements arrive on the meter this factory creates rather than the
        // shared fallback.
        var data    = await Fixture.SeedTestStream(50, ct);
        var factory = new TestMeterFactory();

        var recorded = 0L;

        using var listener = new MeterListener {
            // Scope to this test's factory: the meter name is shared process-wide, so filter by identity.
            InstrumentPublished = (instrument, l) => {
                if (factory.Meters.Contains(instrument.Meter))
                    l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => {
            if (instrument.Name == "kurrent_processor_records_processed")
                recorded += value;
        });

        listener.Start();

        var processor = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(Fixture.ConnectionString, client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"meterfactory-{data.Stream}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.Interval          = TimeSpan.FromHours(1);
            opts.MeterFactory                 = factory;
        });

        // Act
        await drone.RunAsync(ct);

        // Assert — the records_processed counter fired on the caller's meter
        await Assert.That(recorded).IsGreaterThan(0L);
    }

    [Test]
    public async ValueTask source_lag_samples_reach_observers(CancellationToken ct) {
        // Arrange — with TrackSourceLag on, the probe reads the log head on its refresh interval and the
        // sample must reach observers on the seam. A short interval keeps the test fast; the processor
        // never stops, so the live subscription stays open for the probe to tick against.
        var data     = await Fixture.SeedTestStream(50, ct);
        var sampled  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new LagObserver(sampled);

        var processor = new TestableRecordProcessor();

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(Fixture.ConnectionString, client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"sourcelag-{data.Stream}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.Interval          = TimeSpan.FromHours(1);
            opts.TrackSourceLag               = true;
            opts.SourceLagRefreshInterval     = TimeSpan.FromMilliseconds(100);
            opts.Observe(observer);
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var run = drone.RunAsync(cts.Token).AsTask();

        // Act — wait for the first sample, then stop the drone
        await sampled.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        await cts.CancelAsync();

        try {
            await run;
        }
        catch (OperationCanceledException) { }
        catch (AggregateException) { }

        // Assert
        await Assert.That(observer.Samples).IsGreaterThan(0);
    }
}

/// <summary>Counts the record ready/completed events the seam delivers to a user observer.</summary>
file sealed class CountingObserver : IProcessorObserver {
    public int Ready     { get; private set; }
    public int Completed { get; private set; }

    // The seam dispatches synchronously on the single processing thread, so plain counters are safe.
    public void OnRecordReady(in RecordReady evt)         => Ready++;
    public void OnRecordCompleted(in RecordCompleted evt) => Completed++;
}

/// <summary>Signals the first source-lag sample so the test can stop the otherwise-live drone.</summary>
file sealed class LagObserver(TaskCompletionSource sampled) : IProcessorObserver {
    public int Samples { get; private set; }

    public void OnSourceLagSampled(in SourceLagSampled evt) {
        Samples++;
        sampled.TrySetResult();
    }
}
