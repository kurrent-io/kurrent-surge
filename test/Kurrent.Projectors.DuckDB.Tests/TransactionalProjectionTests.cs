using DuckDB.NET.Data;
using Kurrent.Projectors.Transactional;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming;
using Kurrent.Surge.Streaming.Models;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Testing.ArcadeRacer;

namespace Kurrent.Projectors.DuckDB.Tests;

/// <summary>
/// Covers the claim the transactional projector exists to make: projected rows and the resume position
/// are one durable fact. Every test here asserts against both tables, because either one alone proves
/// nothing — rows without a position replay, a position without rows skips.
/// </summary>
[Category("Projections")]
[Category("DuckDB")]
[Category("Integration")]
[Timeout(60_000)]
public class TransactionalProjectionTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [ClassDataSource<DuckDBTestFixture>(Shared = SharedType.None)]
    public required DuckDBTestFixture DuckDB { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask commits_projected_rows_and_resume_position_together(CancellationToken ct) {
        // Arrange
        var seeded      = await Fixture.SeedTestStream(20, ct);
        var processorId = $"tx-projection-{TestId.New()}";

        // Act
        await RunToCompletion(new CountingProjection(seeded.Position), processorId, seeded, ct);

        // Assert — rows landed, and the checkpoint advanced to the last record the projection saw.
        await Assert.That(await CountRows(ct)).IsEqualTo(20);
        await Assert.That(await StoredPosition(processorId, ct)).IsEqualTo(seeded.Position.Value);
    }

    [Test]
    public async ValueTask leaves_position_untouched_when_the_epoch_is_rolled_back(CancellationToken ct) {
        // Arrange — fail on the very first batch, so nothing has ever been committed.
        var seeded      = await Fixture.SeedTestStream(20, ct);
        var processorId = $"tx-projection-{TestId.New()}";

        var projection = new CountingProjection(seeded.Position) { FailAfter = 5 };

        // Act
        var failure = await RunExpectingFailure(projection, processorId, seeded, ct);

        // Assert — the writes that did happen were discarded with the transaction, and because nothing
        // committed there is no checkpoint row at all. A resuming run starts from the beginning.
        await Assert.That(failure).IsNotNull();
        await Assert.That(await CountRows(ct)).IsEqualTo(0);
        await Assert.That(await StoredPosition(processorId, ct)).IsNull();
    }

    [Test]
    public async ValueTask replays_the_discarded_span_exactly_once_after_a_crash(CancellationToken ct) {
        // Arrange
        var seeded      = await Fixture.SeedTestStream(20, ct);
        var processorId = $"tx-projection-{TestId.New()}";

        // Act — first run dies partway through; second run resumes against the same DuckDB file.
        await RunExpectingFailure(new CountingProjection(seeded.Position) { FailAfter = 5 }, processorId, seeded, ct);

        var rowsAfterCrash = await CountRows(ct);

        await RunToCompletion(new CountingProjection(seeded.Position), processorId, seeded, ct);

        // Assert — the crashed run contributed nothing, and the replay produced every record once.
        // A duplicate would show as more than 20; a gap as fewer.
        await Assert.That(rowsAfterCrash).IsEqualTo(0);
        await Assert.That(await CountRows(ct)).IsEqualTo(20);
        await Assert.That(await DistinctRows(ct)).IsEqualTo(20);
        await Assert.That(await StoredPosition(processorId, ct)).IsEqualTo(seeded.Position.Value);
    }

    #region ->> Running <<-

    async ValueTask RunToCompletion(
        CountingProjection projection, string processorId, SeededTestStream seeded, CancellationToken ct
    ) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        projection.StopSignal = cts;

        try {
            await Build(projection, processorId, seeded).RunAsync(cts.Token);
        }
        catch (OperationCanceledException) {
            // Expected — the projection cancels itself once it has seen the last seeded record.
        }
    }

    async ValueTask<Exception?> RunExpectingFailure(
        CountingProjection projection, string processorId, SeededTestStream seeded, CancellationToken ct
    ) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        projection.StopSignal = cts;

        try {
            await Build(projection, processorId, seeded).RunAsync(cts.Token);
            return null;
        }
        catch (Exception ex) {
            return ex;
        }
    }

    KurrentProcessor Build(CountingProjection projection, string processorId, SeededTestStream seeded) =>
        DuckDBProjector.Create(projection, opts => {
            opts.ProjectionId        = processorId;
            opts.ClientProvider     = DuckDB.ConnectionProvider;

            opts.Connection(Fixture.ConnectionString, client => {
                client.LoggerFactory         = Fixture.LoggerFactory;
                client.Schema.ValidationMode = SchemaValidationMode.Disabled;
                client.Resilience            = KurrentClientResilienceOptions.NoResilience;

                // CountingProjection only dispatches decoded records. The ArcadeRacer contracts are
                // discovered but not always-active, so without this every record is skipped — which
                // surfaces as the test hanging on a stop signal that never fires, not as an error.
                client.Messages.UseTestkitCatalog();
            });

            opts.Subscription.Filter          = RecordFilter.ByStream(seeded.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        });

    #endregion

    #region ->> Reading back <<-

    async ValueTask<int> CountRows(CancellationToken ct) =>
        Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM projected_records", ct));

    async ValueTask<int> DistinctRows(CancellationToken ct) =>
        Convert.ToInt32(await Scalar("SELECT COUNT(DISTINCT record_id) FROM projected_records", ct));

    async ValueTask<long?> StoredPosition(string processorId, CancellationToken ct) {
        // The checkpoint table only exists once a projection has bootstrapped against this database.
        var exists = Convert.ToInt64(await Scalar(
            $"""SELECT COUNT(*) FROM duckdb_tables() WHERE table_name = '{DuckDBProjectionCheckpointStore.TableName}'""", ct));

        if (exists == 0)
            return null;

        var value = await Scalar(
            $"""SELECT MAX(log_position) FROM "{DuckDBProjectionCheckpointStore.TableName}" WHERE projection_id = '{processorId}'""",
            ct);

        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    async ValueTask<object?> Scalar(string sql, CancellationToken ct) {
        await using var connection = DuckDB.GetConnection();
        await using var command    = connection.CreateCommand();

        command.CommandText = sql;

        return await command.ExecuteScalarAsync(ct);
    }

    #endregion

    /// <summary>
    /// Writes one row per record and stops itself at a known position. <see cref="FailAfter"/> makes it
    /// throw mid-epoch so the rollback path can be observed.
    /// </summary>
    sealed class CountingProjection : DuckDBProjection {
        readonly LogPosition _stopAt;

        int _applied;

        public CountingProjection(LogPosition stopAt) {
            _stopAt = stopAt;

            Setup(client => {
                using var command = client.CreateCommand();
                command.CommandText = "CREATE TABLE IF NOT EXISTS projected_records (record_id UUID PRIMARY KEY)";
                command.ExecuteNonQuery();
            });

            // Every type the dataset can emit needs a registration — there is no fallback, and an
            // unregistered type is skipped silently.
            Count<GridFormed>();
            Count<RaceStarted>();
            Count<LapCompleted>();
            Count<OvertakePerformed>();
            Count<PitStopPerformed>();
            Count<IncidentOccurred>();
            Count<CarRetired>();
            Count<WeatherChanged>();
            Count<RaceFinished>();
            Count<RaceInsight>();
        }

        void Count<TMessage>() =>
            Project<TMessage>(async (_, context) => {
                if (FailAfter is { } limit && _applied >= limit)
                    throw new InvalidOperationException($"Deliberate failure after {limit} records.");

                var connection = await context.Clients.GetClient(context.CancellationToken);

                using (var command = connection.CreateCommand()) {
                    command.CommandText = "INSERT INTO projected_records VALUES ($id) ON CONFLICT DO NOTHING";
                    command.Parameters.Add(new DuckDBParameter("id", context.Record.Id.ToGuid()));
                    command.ExecuteNonQuery();
                }

                _applied++;

                if (context.Record.Position.LogPosition >= _stopAt)
                    StopSignal?.Cancel();
            });

        /// <summary>Throw once this many records have been applied. Null never fails.</summary>
        public int? FailAfter { get; init; }

        public CancellationTokenSource? StopSignal { get; set; }
    }
}
