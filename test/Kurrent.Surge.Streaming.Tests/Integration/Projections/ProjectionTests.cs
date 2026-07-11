using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Models;
using Kurrent.Projectors;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;

namespace Kurrent.Surge.Streaming.Tests.Projections;

[Category("Projections")]
[Category("Integration")]
[Timeout(30_000)]
public class ProjectionTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask generic_projection_dispatches_typed_handlers_and_fallback(CancellationToken ct) {
        // Arrange
        var seeded  = await Fixture.SeedTestStream(30, ct);
        var counter = new TypeCounter();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var projection = new CountingProjection(seeded.Position, cts);

        // Act
        try {
            await KurrentProjector.Create(projection, opts => {
                opts.ProcessorId = $"typed-projection-{TestId.New()}";
                opts.Connection(Fixture.ConnectionString, client => {
                    client.LoggerFactory      = Fixture.LoggerFactory;
                    client.Schema.PreValidate = false;
                    client.Resilience         = KurrentClientResilienceOptions.NoResilience;
                    // ArcadeRacer contracts come from the testkit's message catalog, which is
                    // discovered but not always-active — opt in so typed handlers can resolve them.
                    client.Messages.UseTestkitCatalog();
                });
                opts.Subscription.Filter          = RecordFilter.ByStream(seeded.Stream);
                opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
                opts.ResolveClient                = _ => ValueTask.FromResult(counter);
            }).RunAsync(cts.Token);
        }
        catch (OperationCanceledException) {
            // Expected — projection self-cancels when reaching seeded.Position.
        }

        // Assert — all records dispatched somewhere, typed handlers picked their types, fallback picked the rest.
        var total = counter.GridFormedCount + counter.RaceStartedCount + counter.FallbackCount;
        await Assert.That(total).IsEqualTo(30);
        await Assert.That(counter.GridFormedCount).IsGreaterThan(0);
        await Assert.That(counter.FallbackCount).IsGreaterThan(0);
    }

    sealed class TypeCounter {
        public int GridFormedCount  { get; set; }
        public int RaceStartedCount { get; set; }
        public int FallbackCount    { get; set; }
    }

    sealed class CountingProjection : Projection<TypeCounter> {
        public CountingProjection(LogPosition stopAt, CancellationTokenSource stopSignal) {
            Project<GridFormed>((_, counter, ctx) => {
                counter.GridFormedCount++;
                MaybeStop(ctx, stopAt, stopSignal);
            });

            Project<RaceStarted>((_, counter, ctx) => {
                counter.RaceStartedCount++;
                MaybeStop(ctx, stopAt, stopSignal);
            });

            Fallback((counter, ctx) => {
                counter.FallbackCount++;
                MaybeStop(ctx, stopAt, stopSignal);
            });
        }

        static void MaybeStop(ProjectionContext<TypeCounter> ctx, LogPosition stopAt, CancellationTokenSource signal) {
            if (ctx.Record.Position.LogPosition >= stopAt)
                signal.Cancel();
        }
    }
}
