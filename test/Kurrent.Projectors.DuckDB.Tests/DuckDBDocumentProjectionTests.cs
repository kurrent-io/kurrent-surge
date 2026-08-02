using Bogus;
using DuckDB.NET.Data;
using Kurrent.Projectors.Transactional;
using Kurrent.Quack;
using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors.DuckDB.Tests;

using static ProjectionTestRecords;

/// <summary>
/// Covers <see cref="DocumentProjection{TClient,TDocument,TKey}"/> — the shape that loads, folds in memory,
/// and writes once per key at the end of the batch. Its failure modes are all silent: a second load
/// resurrects a document a handler deleted, and a missed fold turns one write into many.
/// </summary>
/// <remarks>
/// Over a store of its own, so the loads are countable. What
/// <see cref="DuckDBDocumentProjection{TDocument,TKey}"/>'s own hidden store does with them is
/// <see cref="DuckDBDocumentStoreTests"/>.
/// </remarks>
[Category("Projections")]
[Category("DuckDB")]
[Category("Integration")]
[Timeout(30_000)]
public class DuckDBDocumentProjectionTests {
    [ClassDataSource<DuckDBTestFixture>(Shared = SharedType.None)]
    public required DuckDBTestFixture DuckDB { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    #region ->> Writing <<-

    [Test]
    public async ValueTask writes_one_row_per_key_touched_in_the_batch() {
        // Arrange
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);
        var first      = NewCounterId();
        var second     = NewCounterId();

        var expected = new[] {
            new Counter(first, 3, ""),
            new Counter(second, 7, "")
        };

        // Act
        await projection.OnProcessBatch(connection, BatchOf(
            Decoded(new Incremented(first, 3), NextPosition()),
            Decoded(new Incremented(second, 7), NextPosition())));

        // Assert
        await Assert.That(ReadCounters(connection, projection.Table)).IsEquivalentTo(expected);
    }

    #endregion

    #region ->> Folding <<-

    [Test]
    public async ValueTask loads_a_key_once_however_many_messages_touch_it() {
        // Arrange — the load is the expensive half of this shape. Reloading per message would also be
        // wrong, not just slow: the second load would not see what the first message folded in.
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);
        var counterId  = NewCounterId();

        var expected = new[] { new Counter(counterId, 6, "") };

        // Act
        await projection.OnProcessBatch(connection, BatchOf(
            Decoded(new Incremented(counterId, 1), NextPosition()),
            Decoded(new Incremented(counterId, 2), NextPosition()),
            Decoded(new Incremented(counterId, 3), NextPosition())));

        // Assert
        await Assert.That(projection.Loads).IsEqualTo(1);
        await Assert.That(ReadCounters(connection, projection.Table)).IsEquivalentTo(expected);
    }

    [Test]
    public async ValueTask folds_a_key_across_batches_through_storage() {
        // Arrange — the fold window is the batch. A later batch starts clean and loads what the earlier
        // one wrote, so the totals still accumulate — through the table, not through memory.
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);
        var counterId  = NewCounterId();

        var expected = new[] { new Counter(counterId, 5, "") };

        // Act
        await projection.OnProcessBatch(connection, BatchOf(Decoded(new Incremented(counterId, 2), NextPosition())));
        await projection.OnProcessBatch(connection, BatchOf(Decoded(new Incremented(counterId, 3), NextPosition())));

        // Assert — one load per batch; the second returns the first batch's row.
        await Assert.That(projection.Loads).IsEqualTo(2);
        await Assert.That(ReadCounters(connection, projection.Table)).IsEquivalentTo(expected);
    }

    [Test]
    public async ValueTask hands_the_handler_the_document_already_in_storage() {
        // Arrange — a row written by an earlier batch. The handler keeps the stored total and changes
        // only the label, so a load that returned null would show up as a reset total.
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);
        var stored     = new Counter(NewCounterId(), 41, "before");
        var label      = Faker.Commerce.ProductName();

        Seed(connection, projection.Table, stored);

        var expected = new[] { stored with { Label = label } };

        // Act
        await projection.OnProcessBatch(connection, BatchOf(
            Decoded(new Labelled(stored.Id, label), NextPosition())));

        // Assert
        await Assert.That(projection.Loads).IsEqualTo(1);
        await Assert.That(ReadCounters(connection, projection.Table)).IsEquivalentTo(expected);
    }

    [Test]
    public async ValueTask runs_an_async_handler() {
        // Arrange — Project has a sync and an async overload; only the sync one is exercised elsewhere.
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);
        var counterId  = NewCounterId();
        var label      = Faker.Commerce.ProductName();

        var expected = new[] { new Counter(counterId, 0, label) };

        // Act
        await projection.OnProcessBatch(connection, BatchOf(
            Decoded(new CreatedAsync(counterId, label), NextPosition())));

        // Assert
        await Assert.That(ReadCounters(connection, projection.Table)).IsEquivalentTo(expected);
    }

    #endregion

    #region ->> Deleting <<-

    [Test]
    public async ValueTask deletes_the_row_when_a_handler_returns_null() {
        // Arrange
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);
        var stored     = new Counter(NewCounterId(), 12, "doomed");

        Seed(connection, projection.Table, stored);

        // Act
        await projection.OnProcessBatch(connection, BatchOf(Decoded(new Removed(stored.Id), NextPosition())));

        // Assert
        await Assert.That(CountRows(connection, projection.Table)).IsEqualTo(0);
    }

    [Test]
    public async ValueTask does_not_reload_a_key_a_handler_deleted_earlier_in_the_batch() {
        // Arrange — a delete is authoritative. Reloading the key for the next message would hand the
        // handler the row that was just deleted and resurrect it when the batch drains.
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);
        var stored     = new Counter(NewCounterId(), 12, "doomed");

        Seed(connection, projection.Table, stored);

        // Act — Labelled maps a null document back to null, so the key must stay deleted.
        await projection.OnProcessBatch(connection, BatchOf(
            Decoded(new Removed(stored.Id), NextPosition()),
            Decoded(new Labelled(stored.Id, Faker.Commerce.ProductName()), NextPosition())));

        // Assert
        await Assert.That(projection.Loads).IsEqualTo(1);
        await Assert.That(CountRows(connection, projection.Table)).IsEqualTo(0);
    }

    #endregion

    #region ->> Dispatch <<-

    [Test]
    public async ValueTask skips_a_record_whose_type_has_no_handler() {
        // Arrange
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);

        // Act
        await projection.OnProcessBatch(connection, BatchOf(
            Decoded(new Unregistered(NewCounterId()), NextPosition())));

        // Assert
        await Assert.That(projection.Loads).IsEqualTo(0);
        await Assert.That(CountRows(connection, projection.Table)).IsEqualTo(0);
    }

    [Test]
    public async ValueTask skips_a_record_that_was_never_decoded() {
        // Arrange
        using var connection = DuckDB.GetConnection();

        var projection = NewProjection(connection);

        // Act
        await projection.OnProcessBatch(connection, BatchOf(Undecoded(NextPosition())));

        // Assert
        await Assert.That(projection.Loads).IsEqualTo(0);
        await Assert.That(CountRows(connection, projection.Table)).IsEqualTo(0);
    }

    #endregion

    #region ->> Projection <<-

    /// <summary>
    /// Counts by key. The store's <c>Loads</c> records how often storage was consulted, which is the only
    /// way to see the fold from outside.
    /// </summary>
    sealed class CounterProjection : DocumentProjection<DuckDBAdvancedConnection, Counter, string> {
        public CounterProjection(CounterStore store, string table) : base(store, table) {
            Store = store;
            Table = table;

            Project<Incremented>(
                msg => msg.CounterId,
                (msg, current, _) => current is { } counter
                    ? counter with { Total = counter.Total + msg.By }
                    : new Counter(msg.CounterId, msg.By, ""));

            Project<Labelled>(
                msg => msg.CounterId,
                (msg, current, _) => current is { } counter
                    ? counter with { Label = msg.Label }
                    : null);

            Project<CreatedAsync>(
                msg => msg.CounterId,
                (msg, _, _) => ValueTask.FromResult<Counter?>(new Counter(msg.CounterId, 0, msg.Label)));

            Project<Removed>(msg => msg.CounterId, (_, _, _) => null);
        }

        public CounterStore Store { get; }

        public string Table { get; }

        public int Loads => Store.Loads;
    }

    /// <summary>
    /// Reads and writes real columns on the connection it is given. These tests drive <c>Apply</c> directly
    /// rather than through an epoch, so the fixture's connection stands in for the transaction's.
    /// </summary>
    /// <remarks>
    /// The document type travels on the call, not on the interface, so a store written for one type casts to
    /// it — and would fail loudly if pointed at a projection over another.
    /// </remarks>
    sealed class CounterStore(DuckDBAdvancedConnection connection) : IDocumentStore {
        public int Loads { get; private set; }

        public async ValueTask<TDocument?> Load<TDocument, TKey>(
            string collection, TKey key, CancellationToken cancellationToken
        ) where TDocument : class where TKey : notnull {
            Loads++;

            using var command = connection.CreateCommand();

            command.CommandText = $"""SELECT id, total, label FROM "{collection}" WHERE id = $id""";
            command.Parameters.Add(new DuckDBParameter("id", key.ToString()));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            var counter = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? new Counter(reader.GetString(0), reader.GetInt32(1), reader.GetString(2))
                : null;

            return (TDocument?)(object?)counter;
        }

        public async ValueTask Save<TDocument, TKey>(
            string collection, TKey key, TDocument document, CancellationToken cancellationToken
        ) where TDocument : class where TKey : notnull {
            var counter = (Counter)(object)document;

            using var command = connection.CreateCommand();

            command.CommandText = $"""
                INSERT INTO "{collection}" (id, total, label) VALUES ($id, $total, $label)
                ON CONFLICT (id) DO UPDATE SET total = EXCLUDED.total, label = EXCLUDED.label
                """;

            command.Parameters.Add(new DuckDBParameter("id", counter.Id));
            command.Parameters.Add(new DuckDBParameter("total", counter.Total));
            command.Parameters.Add(new DuckDBParameter("label", counter.Label));

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask Delete<TKey>(string collection, TKey key, CancellationToken cancellationToken)
            where TKey : notnull {
            using var command = connection.CreateCommand();

            command.CommandText = $"""DELETE FROM "{collection}" WHERE id = $id""";
            command.Parameters.Add(new DuckDBParameter("id", key.ToString()));

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    #endregion

    #region ->> Documents and messages <<-

    public sealed record Counter(string Id, int Total, string Label);

    public sealed record Incremented(string CounterId, int By);

    public sealed record Labelled(string CounterId, string Label);

    public sealed record CreatedAsync(string CounterId, string Label);

    public sealed record Removed(string CounterId);

    public sealed record Unregistered(string CounterId);

    #endregion

    #region ->> Arranging <<-

    long _position;

    long NextPosition() => ++_position;

    string NewCounterId() => $"{Faker.Commerce.Department()}-{TestId.New()}";

    CounterProjection NewProjection(DuckDBAdvancedConnection connection) {
        var table = $"counters_{TestId.New()}";

        using var command = connection.CreateCommand();

        command.CommandText = $"""
            CREATE TABLE "{table}" (id TEXT PRIMARY KEY, total INTEGER NOT NULL, label TEXT NOT NULL)
            """;

        command.ExecuteNonQuery();

        return new CounterProjection(new CounterStore(connection), table);
    }

    static void Seed(DuckDBAdvancedConnection connection, string table, Counter counter) {
        using var command = connection.CreateCommand();

        command.CommandText = $"""INSERT INTO "{table}" (id, total, label) VALUES ($id, $total, $label)""";

        command.Parameters.Add(new DuckDBParameter("id", counter.Id));
        command.Parameters.Add(new DuckDBParameter("total", counter.Total));
        command.Parameters.Add(new DuckDBParameter("label", counter.Label));

        command.ExecuteNonQuery();
    }

    #endregion

    #region ->> Reading back <<-

    static Counter[] ReadCounters(DuckDBAdvancedConnection connection, string table) {
        using var command = connection.CreateCommand();

        command.CommandText = $"""SELECT id, total, label FROM "{table}" """;

        using var reader = command.ExecuteReader();

        var counters = new List<Counter>();

        while (reader.Read())
            counters.Add(new Counter(reader.GetString(0), reader.GetInt32(1), reader.GetString(2)));

        return counters.ToArray();
    }

    static int CountRows(DuckDBAdvancedConnection connection, string table) {
        using var command = connection.CreateCommand();

        command.CommandText = $"""SELECT COUNT(*) FROM "{table}" """;

        return Convert.ToInt32(command.ExecuteScalar());
    }

    #endregion
}
