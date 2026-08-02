using Bogus;
using DuckDB.NET.Data;
using Kurrent.Projectors.Transactional;
using Kurrent.Quack;
using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming;
using Kurrent.Surge.Streaming.Models;

namespace Kurrent.Projectors.DuckDB.Tests;

using static ProjectionTestRecords;

/// <summary>
/// Covers what <see cref="ProjectionProcessor{TClient}"/> does to an epoch that fails, driving the real
/// lifecycle — <c>OnInitialize</c>, <c>ProcessBatch</c>, <c>OnCheckpoint</c> — over a real transaction and
/// a real checkpoint store, with no stream behind it.
/// </summary>
/// <remarks>
/// The pipeline suites already prove a failed epoch leaves no rows and no position for two of the shapes.
/// What they never reach is the framework's own bookkeeping: that a replay after a rollback sees storage
/// as the rollback left it, and that the barrier which follows a failure refuses to commit rather than
/// storing positions for rows that were discarded.
/// </remarks>
[Category("Projections")]
[Category("DuckDB")]
[Category("Integration")]
[Timeout(30_000)]
public class ProjectionEpochTests {
    [ClassDataSource<DuckDBTestFixture>(Shared = SharedType.None)]
    public required DuckDBTestFixture DuckDB { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask commits_rows_and_positions_together_when_the_epoch_checkpoints(CancellationToken ct) {
        // Arrange — the positive control. Every other test here asserts an absence, and an absence proves
        // nothing unless the same harness can produce a presence.
        var table      = NewTableName();
        var projection = new TotalsProjection(table);

        await using var epoch = await Wire(projection, ct);

        var total = NewTotal();

        // Act
        await epoch.ProcessBatch(BatchOf(Decoded(total, NextPosition())));
        await epoch.Checkpoint(PositionsAt(_position));

        // Assert
        await Assert.That(ReadTotals(table)).IsEquivalentTo([total]);
        await Assert.That(epoch.StoredPosition()).IsEqualTo(_position);
    }

    [Test]
    public async ValueTask discards_every_batch_of_the_epoch_when_a_later_one_fails(CancellationToken ct) {
        // Arrange — the transaction spans the whole epoch, not one batch. A batch that succeeded before
        // the failure has to go with it, or the epoch commits partially.
        var table      = NewTableName();
        var projection = new TotalsProjection(table);

        await using var epoch = await Wire(projection, ct);

        // Act
        await epoch.ProcessBatch(BatchOf(Decoded(NewTotal(), NextPosition())));

        var failure = await epoch.ProcessBatchExpectingFailure(
            BatchOf(Decoded(new Rejected(NewCounterId()), NextPosition())));

        // Assert
        await Assert.That(failure).IsNotNull();
        await Assert.That(CountRows(table)).IsEqualTo(0);
    }

    [Test]
    public async ValueTask leaves_no_position_behind_when_the_epoch_fails(CancellationToken ct) {
        // Arrange — rows and positions are one durable fact. A position surviving a failed epoch would
        // skip the very records whose rows were just discarded.
        var table      = NewTableName();
        var projection = new TotalsProjection(table);

        await using var epoch = await Wire(projection, ct);

        // Act
        await epoch.ProcessBatchExpectingFailure(BatchOf(Decoded(new Rejected(NewCounterId()), NextPosition())));

        // Assert
        await Assert.That(epoch.StoredPosition()).IsNull();
    }

    [Test]
    public async ValueTask refuses_to_checkpoint_after_a_batch_failed(CancellationToken ct) {
        // Arrange — the drone still runs a barrier after a batch throws, to flush what an ordinary
        // processor tracked. For a projection that barrier must not commit: those positions describe
        // rows the rollback already threw away. Committing them silently skips the failed span forever.
        var table      = NewTableName();
        var projection = new TotalsProjection(table);

        await using var epoch = await Wire(projection, ct);

        await epoch.ProcessBatchExpectingFailure(BatchOf(Decoded(new Rejected(NewCounterId()), NextPosition())));

        // Act / Assert
        await Assert.That(async () => await epoch.Checkpoint(PositionsAt(_position)))
            .Throws<InvalidOperationException>();

        await Assert.That(epoch.StoredPosition()).IsNull();
    }

    [Test]
    public async ValueTask replays_against_storage_as_the_rollback_left_it(CancellationToken ct) {
        // Arrange — the document shape buffers nothing across batches, so a failed epoch leaves nothing
        // in memory to clear. What the replay must not see is the rolled-back epoch's rows: its load has
        // to find storage as the rollback left it, not the value the discarded epoch had built up.
        var table = NewTableName();

        await using var epoch = await Wire(
            transaction => new CounterProjection(new CounterStore(transaction), table), ct);

        var projection = epoch.Projection;
        var counterId  = NewCounterId();

        await epoch.ProcessBatch(BatchOf(Decoded(new Incremented(counterId, 9), NextPosition())));

        await epoch.ProcessBatchExpectingFailure(BatchOf(Decoded(new Rejected(counterId), NextPosition())));

        // Act — replay the key onto a fresh transaction. A fold that still saw the discarded 9 — from
        // memory or from a write that survived the rollback — would produce 10 instead of 1.
        await projection.OnProcessBatch(epoch.Connection, BatchOf(Decoded(new Incremented(counterId, 1), NextPosition())));

        // Assert — read on the open transaction's connection; the replayed row is not committed yet.
        await Assert.That(ReadTotal(epoch.Connection, table, counterId)).IsEqualTo(1);
    }

    #region ->> Epoch harness <<-

    /// <summary>
    /// Drives one projection through the lifecycle a drone would, over a real transaction and checkpoint
    /// store. <c>OnInitialize</c> runs on construction, so the projection's <c>Setup</c> has created its
    /// schema and its resume positions are loaded before the first batch.
    /// </summary>
    sealed class Epoch : IAsyncDisposable {
        readonly ProjectionProcessor<DuckDBAdvancedConnection> _projection;
        readonly DuckDBProjectionTransaction              _transaction;
        readonly DuckDBTestFixture                        _fixture;
        readonly string                                   _projectionId;
        readonly CancellationToken                        _cancellationToken;

        Epoch(
            ProjectionProcessor<DuckDBAdvancedConnection> projection,
            DuckDBProjectionTransaction transaction,
            DuckDBTestFixture fixture,
            string projectionId,
            CancellationToken cancellationToken
        ) {
            _projection        = projection;
            _transaction       = transaction;
            _fixture           = fixture;
            _projectionId      = projectionId;
            _cancellationToken = cancellationToken;
        }

        /// <summary>
        /// The projection this epoch is driving. A projection built from the transaction — anything with a
        /// document store — has no other handle back to the instance.
        /// </summary>
        public ProjectionProcessor<DuckDBAdvancedConnection> Projection => _projection;

        public static async ValueTask<Epoch> Start(
            Func<IProjectionTransaction<DuckDBAdvancedConnection>, ProjectionProcessor<DuckDBAdvancedConnection>> build,
            DuckDBTestFixture fixture,
            CancellationToken cancellationToken
        ) {
            var projectionId = $"epoch-{TestId.New()}";
            var transaction  = new DuckDBProjectionTransaction(fixture.ConnectionProvider);
            var checkpoints  = new DuckDBProjectionCheckpointStore(projectionId, TimeProvider.System);
            var projection   = build(transaction);

            projection.Wire(projectionId, transaction, checkpoints);

            await ((IProcessor)projection).OnInitialize(new InitializeContext {
                Partitions        = [Partition.From(0)],
                InitialPosition   = SubscriptionInitialPosition.Earliest,
                StartPosition     = LogPosition.Unset,
                CancellationToken = cancellationToken
            });

            return new Epoch(projection, transaction, fixture, projectionId, cancellationToken);
        }

        /// <summary>The connection the open transaction is running on.</summary>
        public DuckDBAdvancedConnection Connection =>
            _transaction.Open(_cancellationToken).GetAwaiter().GetResult();

        public ValueTask ProcessBatch(BatchContext context) =>
            ((IRecordBatchProcessor)_projection).ProcessBatch(context);

        public async ValueTask<Exception?> ProcessBatchExpectingFailure(BatchContext context) {
            try {
                await ProcessBatch(context);
                return null;
            }
            catch (Exception ex) {
                return ex;
            }
        }

        public ValueTask Checkpoint(RecordPosition[] positions) =>
            ((IProcessor)_projection).OnCheckpoint(new CheckpointContext {
                Positions         = positions,
                CheckpointId      = TestId.New(),
                Trigger           = CheckpointTrigger.Interval,
                Timestamp         = DateTimeOffset.UnixEpoch,
                CancellationToken = _cancellationToken
            });

        /// <summary>
        /// The committed log position, read on a separate connection so an uncommitted transaction
        /// cannot make it look durable.
        /// </summary>
        public long? StoredPosition() {
            using var connection = _fixture.GetConnection();
            using var command    = connection.CreateCommand();

            command.CommandText = $"""
                SELECT MAX(log_position) FROM "{DuckDBProjectionCheckpointStore.TableName}"
                WHERE projection_id = '{_projectionId}'
                """;

            var value = command.ExecuteScalar();

            return value is null or DBNull ? null : Convert.ToInt64(value);
        }

        public ValueTask DisposeAsync() => _projection.DisposeAsync();
    }

    #endregion

    #region ->> Projections <<-

    /// <summary>Writes straight through, and throws on <see cref="Rejected"/>.</summary>
    sealed class TotalsProjection : DuckDBUpsertProjection<Total> {
        readonly string _table;

        public TotalsProjection(string table) {
            _table = table;

            Setup(connection => {
                using var command = connection.CreateCommand();

                command.CommandText = $"""
                    CREATE TABLE IF NOT EXISTS "{table}" (id TEXT PRIMARY KEY, amount INTEGER NOT NULL)
                    """;

                command.ExecuteNonQuery();
            });

            Project<Total>((msg, _) => msg);

            // Spelled out because a throw expression fits both the sync and the async Project overload.
            Func<Rejected, ProjectionContext<DuckDBAdvancedConnection>, Total?> reject =
                (_, _) => throw new InvalidOperationException("Deliberate handler failure.");

            Project(reject);
        }

        protected override async ValueTask Upsert(
            DuckDBAdvancedConnection connection, Total document, CancellationToken cancellationToken
        ) {
            using var command = connection.CreateCommand();

            command.CommandText = $"""
                INSERT INTO "{_table}" (id, amount) VALUES ($id, $amount)
                ON CONFLICT (id) DO UPDATE SET amount = EXCLUDED.amount
                """;

            command.Parameters.Add(new DuckDBParameter("id", document.Id));
            command.Parameters.Add(new DuckDBParameter("amount", document.Amount));

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Folds by key, so a replay exposes whether the fold saw rolled-back state.</summary>
    sealed class CounterProjection : DocumentProjection<DuckDBAdvancedConnection, Counter, string> {
        public CounterProjection(CounterStore store, string table) : base(store, table) {
            Setup((_, cancellationToken) => store.EnsureCollection(table, cancellationToken));

            Project<Incremented>(
                msg => msg.CounterId,
                (msg, current, _) => current is { } counter
                    ? counter with { Total = counter.Total + msg.By }
                    : new Counter(msg.CounterId, msg.By));

            // Spelled out because a throw expression fits both the sync and the async Project overload.
            Func<Rejected, Counter?, DocumentProjectionContext, Counter?> reject =
                (_, _, _) => throw new InvalidOperationException("Deliberate handler failure.");

            Project(msg => msg.CounterId, reject);
        }
    }

    /// <summary>
    /// <see cref="CounterProjection"/>'s storage, bound to the epoch's transaction. Reading through
    /// <c>Begin</c> is what makes the rollback visible to a replay: a store on its own connection would
    /// still see the discarded epoch's rows.
    /// </summary>
    sealed class CounterStore(IProjectionTransaction<DuckDBAdvancedConnection> transaction) : IDocumentStore {
        public async ValueTask EnsureCollection(string collection, CancellationToken cancellationToken) {
            var connection = await transaction.Open(cancellationToken).ConfigureAwait(false);

            using var command = connection.CreateCommand();

            command.CommandText = $"""
                CREATE TABLE IF NOT EXISTS "{collection}" (id TEXT PRIMARY KEY, total INTEGER NOT NULL)
                """;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<TDocument?> Load<TDocument, TKey>(
            string collection, TKey key, CancellationToken cancellationToken
        ) where TDocument : class where TKey : notnull {
            var connection = await transaction.Open(cancellationToken).ConfigureAwait(false);

            using var command = connection.CreateCommand();

            command.CommandText = $"""SELECT id, total FROM "{collection}" WHERE id = $id""";
            command.Parameters.Add(new DuckDBParameter("id", key.ToString()));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            var counter = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? new Counter(reader.GetString(0), reader.GetInt32(1))
                : null;

            return (TDocument?)(object?)counter;
        }

        public async ValueTask Save<TDocument, TKey>(
            string collection, TKey key, TDocument document, CancellationToken cancellationToken
        ) where TDocument : class where TKey : notnull {
            var connection = await transaction.Open(cancellationToken).ConfigureAwait(false);
            var counter    = (Counter)(object)document;

            using var command = connection.CreateCommand();

            command.CommandText = $"""
                INSERT INTO "{collection}" (id, total) VALUES ($id, $total)
                ON CONFLICT (id) DO UPDATE SET total = EXCLUDED.total
                """;

            command.Parameters.Add(new DuckDBParameter("id", counter.Id));
            command.Parameters.Add(new DuckDBParameter("total", counter.Total));

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask Delete<TKey>(string collection, TKey key, CancellationToken cancellationToken)
            where TKey : notnull {
            var connection = await transaction.Open(cancellationToken).ConfigureAwait(false);

            using var command = connection.CreateCommand();

            command.CommandText = $"""DELETE FROM "{collection}" WHERE id = $id""";
            command.Parameters.Add(new DuckDBParameter("id", key.ToString()));

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    #endregion

    #region ->> Documents and messages <<-

    public sealed record Total(string Id, int Amount);

    public sealed record Counter(string Id, int Total);

    public sealed record Incremented(string CounterId, int By);

    /// <summary>Carries a key so a failing message can be aimed at one the epoch has already touched.</summary>
    public sealed record Rejected(string CounterId);

    #endregion

    #region ->> Arranging <<-

    long _position;

    long NextPosition() => ++_position;

    static RecordPosition[] PositionsAt(long logPosition) =>
        [RecordPosition.From(LogPosition.From(logPosition), Partition.From(0))];

    ValueTask<Epoch> Wire(ProjectionProcessor<DuckDBAdvancedConnection> projection, CancellationToken ct) =>
        Epoch.Start(_ => projection, DuckDB, ct);

    ValueTask<Epoch> Wire(
        Func<IProjectionTransaction<DuckDBAdvancedConnection>, ProjectionProcessor<DuckDBAdvancedConnection>> build,
        CancellationToken ct
    ) =>
        Epoch.Start(build, DuckDB, ct);

    static string NewTableName() => $"epoch_{TestId.New()}";

    string NewCounterId() => $"{Faker.Commerce.Department()}-{TestId.New()}";

    Total NewTotal() => new($"total-{TestId.New()}", Faker.Random.Int(1, 1_000));

    #endregion

    #region ->> Reading back <<-

    // Every read opens its own connection, so nothing an open transaction is holding can be mistaken for
    // committed state.
    Total[] ReadTotals(string table) {
        using var connection = DuckDB.GetConnection();
        using var command    = connection.CreateCommand();

        command.CommandText = $"""SELECT id, amount FROM "{table}" """;

        using var reader = command.ExecuteReader();

        var totals = new List<Total>();

        while (reader.Read())
            totals.Add(new Total(reader.GetString(0), reader.GetInt32(1)));

        return totals.ToArray();
    }

    int CountRows(string table) {
        using var connection = DuckDB.GetConnection();
        using var command    = connection.CreateCommand();

        command.CommandText = $"""SELECT COUNT(*) FROM "{table}" """;

        return Convert.ToInt32(command.ExecuteScalar());
    }

    // Reads on the caller's connection deliberately: the row under assertion lives in an open
    // transaction and would be invisible from anywhere else.
    static int? ReadTotal(DuckDBAdvancedConnection connection, string table, string counterId) {
        using var command = connection.CreateCommand();

        command.CommandText = $"""SELECT total FROM "{table}" WHERE id = $id""";
        command.Parameters.Add(new DuckDBParameter("id", counterId));

        var value = command.ExecuteScalar();

        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    #endregion
}
