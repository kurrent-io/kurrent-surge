using DuckDB.NET.Data;
using Kurrent.Client;
using Kurrent.Client.Streams;
using Kurrent.Processors.Models;
using Kurrent.Projections;
using Kurrent.Projections.DuckDB;
using Sdk.TestKit.Fixtures;

namespace Kurrent.Processors.Tests.Projections.DuckDB;

[Category("Projections")]
[Category("DuckDB")]
[Category("Integration")]
[Timeout(30_000)]
public class DuckDBProjectionTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [ClassDataSource<DuckDBTestFixture>(Shared = SharedType.None)]
    public required DuckDBTestFixture DuckDB { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask duckdb_projection_runs_setup_and_persists_all_records_via_fallback(CancellationToken ct) {
        // Arrange
        var seeded = await Fixture.SeedTestStream(20, ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var projection = new TestDuckDBProjection(seeded.Position, cts);

        // Act
        try {
            await KurrentProjector.Create(projection, opts => {
                opts.ProcessorId  = $"duckdb-projection-{PunyId.New()}";
                opts.SkipDecoding = true;
                opts.Connection(Fixture.KurrentDB.AuthenticatedConnectionString, client => {
                    client.LoggerFactory      = Fixture.LoggerFactory;
                    client.Schema.PreValidate = false;
                    client.Resilience         = KurrentClientResilienceOptions.NoResilience;
                });
                opts.Subscription.Filter          = RecordFilter.ByStream(seeded.Stream);
                opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
                opts.ResolveClient                = _ => ValueTask.FromResult(DuckDB.ConnectionProvider);
            }).RunAsync(cts.Token);
        }
        catch (OperationCanceledException) {
            // Projection self-cancelled via CTS once it reached seeded.Position — expected.
        }

        // Assert
        await Assert.That(projection.SetupRan).IsTrue();

        using var db = DuckDB.GetConnection();
        await using var countCmd = db.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM projected_records";
        var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
        await Assert.That(count).IsEqualTo(20);
    }

    sealed class TestDuckDBProjection : DuckDBProjection {
        public bool SetupRan { get; private set; }

        public TestDuckDBProjection(LogPosition stopAt, CancellationTokenSource stopSignal) {
            Setup(async (provider, setupCt) => {
                await using var cmd = provider.GetConnection().CreateCommand();
                cmd.CommandText = "CREATE TABLE IF NOT EXISTS projected_records (record_id UUID PRIMARY KEY)";
                await cmd.ExecuteNonQueryAsync(setupCt);
                SetupRan = true;
            });

            Fallback(async (provider, ctx) => {
                await using var cmd = provider.GetConnection().CreateCommand();
                cmd.CommandText = "INSERT INTO projected_records VALUES ($id)";
                cmd.Parameters.Add(new DuckDBParameter("id", ctx.Record.Id));
                await cmd.ExecuteNonQueryAsync(ctx.CancellationToken);

                if (ctx.Record.Position.LogPosition >= stopAt)
                    stopSignal.Cancel();
            });
        }
    }
}
