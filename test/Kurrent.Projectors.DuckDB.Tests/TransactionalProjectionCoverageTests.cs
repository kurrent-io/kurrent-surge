using DuckDB.NET.Data;
using Kurrent.Processors.Configuration;
using Kurrent.Projectors.Transactional;
using Kurrent.Quack;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming;
using Kurrent.Surge.Streaming.Models;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Testing.ArcadeRacer;

namespace Kurrent.Projectors.DuckDB.Tests;

/// <summary>
/// The paths <see cref="TransactionalProjectionTests"/> does not reach: the appender shape, the
/// rollback-then-replay sequence, and more than one partition.
/// </summary>
[Category("Projections")]
[Category("DuckDB")]
[Category("Integration")]
[Timeout(60_000)]
public class TransactionalProjectionCoverageTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [ClassDataSource<DuckDBTestFixture>(Shared = SharedType.None)]
    public required DuckDBTestFixture DuckDB { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask rolls_back_rows_and_position_when_the_epoch_fails(CancellationToken ct) {
        // Arrange — the transaction is a value-type TransactionScope held in a non-nullable field, and
        // TransactionScope.Dispose commits or rolls back based on a bool the struct carries. This is the
        // test that catches Close() failing to roll back, or CommitOnDispose being applied to a copy:
        // both would leave the failed epoch's rows behind.
        var seeded      = await Fixture.SeedTestStream(20, ct);
        var processorId = $"rollback-projection-{TestId.New()}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var failing = new CountingProjection(seeded.Position) { FailAfter = 5, StopSignal = cts };

        // Act
        await RunExpectingFailure(ProjectorFor(failing, processorId, seeded), cts.Token);

        var rowsAfterCrash     = await CountRows(ct);
        var positionAfterCrash = await StoredPosition(processorId, ct);

        using var replayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var replay = new CountingProjection(seeded.Position) { StopSignal = replayCts };

        await RunIgnoringCancellation(ProjectorFor(replay, processorId, seeded), replayCts.Token);

        // Assert — the failed epoch left neither rows nor a position, and the transaction it rolled back was
        // still usable: the replay ran the whole stream and committed the final position.
        await Assert.That(rowsAfterCrash).IsEqualTo(0);
        await Assert.That(positionAfterCrash).IsNull();
        await Assert.That(await CountRows(ct)).IsEqualTo(20);
        await Assert.That(await StoredPosition(processorId, ct)).IsEqualTo(seeded.Position.Value);
    }

    [Test]
    public async ValueTask returns_its_connection_to_the_pool_when_the_run_ends(CancellationToken ct) {
        // Arrange — the transaction borrows through GetScopedConnection and releases the handle on
        // disposal. If it held on instead, the pool would have nothing to hand back and the second run
        // would mint a fresh connection that nothing ever closes.
        var seeded = await Fixture.SeedTestStream(20, ct);

        using var firstCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var first = new CountingProjection(seeded.Position) { StopSignal = firstCts };

        await RunIgnoringCancellation(
            ProjectorFor(first, $"reuse-first-{TestId.New()}", seeded), firstCts.Token);

        using var secondCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var second = new CountingProjection(seeded.Position) { StopSignal = secondCts };

        // Act
        await RunIgnoringCancellation(
            ProjectorFor(second, $"reuse-second-{TestId.New()}", seeded), secondCts.Token);

        // Assert
        await Assert.That(first.Client).IsNotNull();
        await Assert.That(second.Client).IsSameReferenceAs(first.Client);
    }

    [Test]
    public async ValueTask appender_rows_roll_back_with_the_epoch(CancellationToken ct) {
        // Arrange — the appender only joins the transaction because Apply scopes it with `using`; this
        // is the test that would catch someone hoisting it to a field.
        var seeded      = await Fixture.SeedTestStream(20, ct);
        var processorId = $"append-projection-{TestId.New()}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var projection = new AppendingProjection(seeded.Position) { FailAfter = 5, StopSignal = cts };

        // Act
        await RunExpectingFailure(
            AppendProjector(projection, processorId, seeded),
            cts.Token);

        var afterCrash = await CountRows(ct);

        using var replayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var replay = new AppendingProjection(seeded.Position) { StopSignal = replayCts };

        await RunIgnoringCancellation(AppendProjector(replay, processorId, seeded), replayCts.Token);

        // Assert — nothing survived the rolled-back epoch, and the replay appended each record once.
        await Assert.That(afterCrash).IsEqualTo(0);
        await Assert.That(await CountRows(ct)).IsEqualTo(20);
    }

    [Test]
    public async ValueTask writes_one_checkpoint_row_per_owned_partition(CancellationToken ct) {
        // Arrange
        var seeded      = await Fixture.SeedTestStream(20, ct);
        var processorId = $"partitioned-projection-{TestId.New()}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var projection = new CountingProjection(seeded.Position) { StopSignal = cts };

        // Act
        await RunIgnoringCancellation(ProjectorFor(projection, processorId, seeded), cts.Token);

        // Assert — the store keys rows by (projection_id, partition), so a single-partition run must
        // produce exactly one row. Multi-partition cannot be covered from here: ProcessorOptions
        // .MaxParallelism is internal to Kurrent.Surge.Core.
        await Assert.That(await CountRows(ct)).IsEqualTo(20);
        await Assert.That(await CheckpointRowCount(processorId, ct)).IsEqualTo(1);
    }

    #region ->> Running <<-

    KurrentProcessor ProjectorFor(CountingProjection projection, string processorId, SeededTestStream seeded) =>
        DuckDBProjector.Create(projection, opts => {
            opts.ProjectionId    = processorId;
            opts.ClientProvider = DuckDB.ConnectionProvider;

            ConfigureConnection(opts, seeded);
        });

    KurrentProcessor AppendProjector(AppendingProjection projection, string processorId, SeededTestStream seeded) =>
        DuckDBProjector.Create(projection, opts => {
            opts.ProjectionId        = processorId;
            opts.ClientProvider     = DuckDB.ConnectionProvider;

            ConfigureConnection(opts, seeded);
        });

    void ConfigureConnection(DuckDBProjectorOptions options, SeededTestStream seeded) {
        options.Connection(Fixture.ConnectionString, client => {
            client.LoggerFactory      = Fixture.LoggerFactory;
            client.Schema.PreValidate = false;
            client.Resilience         = KurrentClientResilienceOptions.NoResilience;

            // The ArcadeRacer contracts are discovered but not always-active. AppendProjection has no
            // fallback and only dispatches decoded records, so without this every record is skipped —
            // which shows up as the test hanging on a stop signal that never fires, not as an error.
            client.Messages.UseTestkitCatalog();
        });

        options.Subscription.Filter          = RecordFilter.ByStream(seeded.Stream);
        options.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
    }

    static async ValueTask RunIgnoringCancellation(KurrentProcessor processor, CancellationToken ct) {
        try {
            await processor.RunAsync(ct);
        }
        catch (OperationCanceledException) {
            // Expected — the projection cancels itself once it has seen the last seeded record.
        }
    }

    static async ValueTask RunExpectingFailure(KurrentProcessor processor, CancellationToken ct) {
        try {
            await processor.RunAsync(ct);
        }
        catch (Exception) {
            // Expected — the projection throws mid-epoch on purpose.
        }
    }

    #endregion

    #region ->> Reading back <<-

    async ValueTask<int> CountRows(CancellationToken ct) =>
        Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM projected_records", ct));

    async ValueTask<int> CheckpointRowCount(string processorId, CancellationToken ct) =>
        Convert.ToInt32(await Scalar(
            $"""SELECT COUNT(*) FROM "{DuckDBProjectionCheckpointStore.TableName}" WHERE projection_id = '{processorId}'""",
            ct));

    async ValueTask<long?> StoredPosition(string processorId, CancellationToken ct) {
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

    #region ->> Projections <<-

    sealed class CountingProjection : DuckDBProjection {
        int _applied;

        public CountingProjection(LogPosition stopAt) {
            Setup(client => {
                Client = client;

                using var command = client.CreateCommand();
                command.CommandText = "CREATE TABLE IF NOT EXISTS projected_records (record_id UUID PRIMARY KEY)";
                command.ExecuteNonQuery();
            });

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
            return;

            void Count<TMessage>() =>
                Project<TMessage>((_, context) => {
                    if (FailAfter is { } limit && _applied >= limit)
                        throw new InvalidOperationException($"Deliberate failure after {limit} records.");

                    using (var command = context.Client.CreateCommand()) {
                        command.CommandText = "INSERT INTO projected_records VALUES ($id) ON CONFLICT DO NOTHING";
                        command.Parameters.Add(new DuckDBParameter("id", context.Record.Id));
                        command.ExecuteNonQuery();
                    }

                    _applied++;

                    if (context.Record.Position.LogPosition >= stopAt)
                        StopSignal?.Cancel();
                });
        }

        /// <summary>The connection this run was handed, captured so a caller can compare runs.</summary>
        public DuckDBAdvancedConnection? Client { get; private set; }

        /// <summary>Throw once this many records have been applied. Null never fails.</summary>
        public int? FailAfter { get; init; }

        public CancellationTokenSource? StopSignal { get; init; }
    }

    sealed class AppendingProjection
        : DuckDBAppendProjection<AppendingProjection.Row, AppendingProjection.RowBinder> {
        int _applied;

        // AppendProjection dispatches on the record's concrete runtime type and has no fallback, so every
        // event the dataset can emit needs a registration — a missing one silently appends nothing and
        // the stop signal never fires.
        public AppendingProjection(LogPosition stopAt) {
            Setup(client => {
                using var command = client.CreateCommand();
                command.CommandText = "CREATE TABLE IF NOT EXISTS projected_records (record_id UUID)";
                command.ExecuteNonQuery();
            });

            Map<GridFormed>();
            Map<RaceStarted>();
            Map<LapCompleted>();
            Map<OvertakePerformed>();
            Map<PitStopPerformed>();
            Map<IncidentOccurred>();
            Map<CarRetired>();
            Map<WeatherChanged>();
            Map<RaceFinished>();
            Map<RaceInsight>();
            return;

            void Map<TMessage>() =>
                Project<TMessage>((_, context) => {
                    if (FailAfter is { } limit && _applied >= limit)
                        throw new InvalidOperationException($"Deliberate failure after {limit} records.");

                    _applied++;

                    if (context.Record.Position.LogPosition >= stopAt)
                        StopSignal?.Cancel();

                    return new Row(context.Record.Id);
                });
        }

        public int?                     FailAfter  { get; init; }
        public CancellationTokenSource? StopSignal { get; init; }

        protected override string Table => "projected_records";

        public readonly record struct Row(Guid Id);

        public readonly struct RowBinder
            : IRowBinder<Row, Kurrent.Quack.Appender, Kurrent.Quack.Appender.Row> {
            // Quack's appender has no UUID overload, so the id goes over as text and DuckDB casts it to
            // the column's UUID type on append.
            public static Kurrent.Quack.Appender.Row Bind(in Row row, Kurrent.Quack.Appender source) =>
                new(source) {
                    row.Id.ToString(),
                };
        }
    }


    #endregion
}
