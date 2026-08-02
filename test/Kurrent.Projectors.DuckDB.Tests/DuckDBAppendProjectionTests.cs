using Bogus;
using DuckDB.NET.Data;
using Kurrent.Projectors.Transactional;
using Kurrent.Quack;
using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming;
using Kurrent.Surge.Streaming.Models;
using Kurrent.Testkit.TUnit.Assertions;

namespace Kurrent.Projectors.DuckDB.Tests;

/// <summary>
/// Covers <see cref="DuckDBAppendProjection{TRow,TBinder}"/> by driving <c>Apply</c> against a real
/// DuckDB file, without a stream or a processor. The pipeline suites only ever count rows; these assert
/// which column each bound value landed in, and which records produced a row at all — the two ways this
/// shape goes wrong while still looking healthy.
/// </summary>
[Category("Projections")]
[Category("DuckDB")]
[Category("Integration")]
[Timeout(30_000)]
public class DuckDBAppendProjectionTests {
    [ClassDataSource<DuckDBTestFixture>(Shared = SharedType.None)]
    public required DuckDBTestFixture DuckDB { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    #region ->> Binding <<-

    [Test]
    public async ValueTask appends_each_value_to_the_column_its_binder_position_names() {
        // Arrange — every column is a different type and no two carry the same value, so a binder that
        // transposed two positions fails on a value rather than passing on the row count.
        using var connection = DuckDB.GetConnection();

        var table = CreateWideTable(connection);
        var lap   = NewLap();

        var projection = new WideProjection(table);
        var expected   = new[] { WideRow.From(lap) };

        // Act
        await projection.OnProcessBatch(connection, BatchOf(Decoded(lap)));

        // Assert
        await Assert.That(ReadWideRows(connection, table)).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async ValueTask binds_null_for_a_row_whose_every_column_is_null() {
        // Arrange — the nullable overloads route null through DBNull. Selecting the non-nullable overload
        // instead binds the type's default, which stores a zero and reads back as a valid row.
        using var connection = DuckDB.GetConnection();

        var table = CreateNullableTable(connection);

        var projection = new NullableProjection(table);

        // Act
        await projection.OnProcessBatch(connection, BatchOf(Decoded(NewLap())));

        // Assert
        var nonNull = Scalar(connection, $"""
            SELECT COUNT(*) FROM "{table}"
            WHERE driver IS NOT NULL OR lap IS NOT NULL OR elapsed IS NOT NULL OR clean IS NOT NULL
            """);

        await Assert.That(CountRows(connection, table)).IsEqualTo(1);
        await Assert.That(Convert.ToInt32(nonNull)).IsEqualTo(0);
    }

    [Test]
    public async ValueTask appends_one_row_per_matching_record_in_the_batch() {
        // Arrange
        using var connection = DuckDB.GetConnection();

        var table = CreateWideTable(connection);
        var laps  = Enumerable.Range(0, 5).Select(_ => NewLap()).ToArray();

        var projection = new WideProjection(table);
        var expected   = laps.Select(WideRow.From).ToArray();

        // Act
        await projection.OnProcessBatch(connection, BatchOf(laps.Select(lap => Decoded(lap)).ToArray()));

        // Assert
        await Assert.That(ReadWideRows(connection, table)).IsDeepEquivalentTo(expected);
    }

    #endregion

    #region ->> Dispatch <<-

    [Test]
    public async ValueTask skips_a_record_whose_type_has_no_handler() {
        // Arrange — the projection registers LapCompleted only. There is no fallback handler, so an
        // unregistered type must contribute nothing rather than throw or bind a partial row.
        using var connection = DuckDB.GetConnection();

        var table = CreateWideTable(connection);
        var lap   = NewLap();

        var projection = new WideProjection(table);
        var expected   = new[] { WideRow.From(lap) };

        // Act
        await projection.OnProcessBatch(connection, BatchOf(Decoded(lap), Decoded(NewPitStop())));

        // Assert
        await Assert.That(ReadWideRows(connection, table)).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async ValueTask skips_a_record_that_was_never_decoded() {
        // Arrange — dispatch keys on the decoded CLR type, so an undecoded record has nothing to match.
        using var connection = DuckDB.GetConnection();

        var table = CreateWideTable(connection);

        var projection = new WideProjection(table);

        // Act
        await projection.OnProcessBatch(connection, BatchOf(Undecoded()));

        // Assert
        await Assert.That(CountRows(connection, table)).IsEqualTo(0);
    }

    [Test]
    public async ValueTask appends_nothing_for_a_record_whose_handler_maps_to_null() {
        // Arrange — returning null is the documented way to say "this message produces no row". Binding
        // a default-valued row instead would be indistinguishable downstream from a real one.
        using var connection = DuckDB.GetConnection();

        var table   = CreateWideTable(connection);
        var kept    = NewLap();
        var dropped = kept with { Driver = $"dropped-{TestId.New()}" };

        var projection = new SelectiveProjection(table, kept.Driver);
        var expected   = new[] { WideRow.From(kept) };

        // Act
        await projection.OnProcessBatch(connection, BatchOf(Decoded(kept), Decoded(dropped)));

        // Assert
        await Assert.That(ReadWideRows(connection, table)).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async ValueTask maps_more_than_one_message_type_into_the_same_table() {
        // Arrange — one table, two registrations, one row type. This is the shape an event log takes.
        using var connection = DuckDB.GetConnection();

        var table = CreateWideTable(connection);
        var lap   = NewLap();
        var pit   = NewPitStop();

        var projection = new TwoMessageProjection(table);
        var expected   = new[] { WideRow.From(lap), WideRow.From(pit) };

        // Act
        await projection.OnProcessBatch(connection, BatchOf(Decoded(lap), Decoded(pit)));

        // Assert
        await Assert.That(ReadWideRows(connection, table)).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async ValueTask hands_the_handler_the_position_of_the_record_being_projected() {
        // Arrange — handlers map from the message, but the context is what carries provenance. A
        // projection that stamps the log position depends on it being the projected record's own.
        using var connection = DuckDB.GetConnection();

        var table    = CreateWideTable(connection);
        var lap      = NewLap();
        var position = Faker.Random.Long(1, 500_000);

        var projection = new PositionStampingProjection(table);
        var expected   = new[] { WideRow.From(lap) with { Lap = position } };

        // Act
        await projection.OnProcessBatch(connection, BatchOf(Decoded(lap, position)));

        // Assert — the projection stamps the position into the lap column, so reading it back proves the
        // handler saw this record's position rather than a default.
        await Assert.That(ReadWideRows(connection, table)).IsDeepEquivalentTo(expected);
    }

    #endregion

    #region ->> Batches <<-

    [Test]
    public async ValueTask appends_nothing_and_does_not_fail_for_an_empty_batch() {
        // Arrange — the appender is opened before the loop, so an empty batch still opens and flushes
        // one. Flushing an appender that holds no rows must be a no-op, not an error.
        using var connection = DuckDB.GetConnection();

        var table = CreateWideTable(connection);

        var projection = new WideProjection(table);

        // Act
        await projection.OnProcessBatch(connection, BatchOf());

        // Assert
        await Assert.That(CountRows(connection, table)).IsEqualTo(0);
    }

    [Test]
    public async ValueTask opens_a_fresh_appender_for_every_batch() {
        // Arrange — the appender's lifetime is one Apply. Hoisting it would leave the second batch
        // appending onto a disposed handle; never flushing it would lose the first batch's rows.
        using var connection = DuckDB.GetConnection();

        var table = CreateWideTable(connection);
        var first = NewLap();
        var next  = NewLap();

        var projection = new WideProjection(table);
        var expected   = new[] { WideRow.From(first), WideRow.From(next) };

        // Act
        await projection.OnProcessBatch(connection, BatchOf(Decoded(first)));
        await projection.OnProcessBatch(connection, BatchOf(Decoded(next)));

        // Assert
        await Assert.That(ReadWideRows(connection, table)).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async ValueTask makes_rows_visible_only_once_the_batch_has_been_applied() {
        // Arrange — Quack's appender buffers, and rows stay invisible until it flushes. That flush lives
        // in DuckDBRowAppender.Dispose; without it a batch would silently land nothing. The observing
        // connection is a second one, because the appender holds the applying connection's transaction.
        using var connection = DuckDB.GetConnection();
        using var observer   = DuckDB.GetConnection();

        var table = CreateWideTable(connection);

        var projection = new ObservingProjection(table, () => CountRows(observer, table));

        // Act
        await projection.OnProcessBatch(connection, BatchOf(Decoded(NewLap())));

        // Assert — the handler ran with its own row still buffered, and the row landed once Apply returned.
        await Assert.That(projection.RowsSeenDuringApply).IsEqualTo(0);
        await Assert.That(CountRows(connection, table)).IsEqualTo(1);
    }

    #endregion

    #region ->> Failure <<-

    [Test]
    public async ValueTask surfaces_the_failure_when_a_handler_throws() {
        // Arrange — Apply owns no transaction; discarding the rows is the caller's job. What Apply must
        // do is let the failure out, so the projector rolls back instead of committing a partial batch.
        using var connection = DuckDB.GetConnection();

        var table = CreateWideTable(connection);

        var projection = new ThrowingProjection(table);

        // Act / Assert
        await Assert.That(async () => await projection.OnProcessBatch(connection, BatchOf(Decoded(NewLap()))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async ValueTask fails_when_the_target_table_does_not_exist() {
        // Arrange — Table names a table Setup is expected to have created. Opening an appender against a
        // missing one has to fail loudly here, not swallow every row for the life of the projection.
        using var connection = DuckDB.GetConnection();

        var projection = new WideProjection($"never_created_{TestId.New()}");

        // Act / Assert
        await Assert.That(async () => await projection.OnProcessBatch(connection, BatchOf(Decoded(NewLap()))))
            .Throws<DuckDBException>();
    }

    #endregion

    #region ->> Projections <<-

    /// <summary>
    /// Binds every column of the wide table. Derived projections pass <c>register: false</c> to replace
    /// the default <see cref="LapCompleted"/> registration with their own.
    /// </summary>
    class WideProjection : DuckDBAppendProjection<WideRow, WideRowBinder> {
        readonly string _table;

        public WideProjection(string table) : this(table, register: true) { }

        protected WideProjection(string table, bool register) {
            _table = table;

            if (register)
                Project<LapCompleted>((msg, _) => WideRow.From(msg));
        }

        protected override string Table => _table;
    }

    sealed class TwoMessageProjection : WideProjection {
        public TwoMessageProjection(string table) : base(table, register: false) {
            Project<LapCompleted>((msg, _) => WideRow.From(msg));
            Project<PitStopPerformed>((msg, _) => WideRow.From(msg));
        }
    }

    /// <summary>Maps to a row for one driver and to nothing for every other.</summary>
    sealed class SelectiveProjection : WideProjection {
        public SelectiveProjection(string table, string keep) : base(table, register: false) =>
            Project<LapCompleted>((msg, _) => msg.Driver == keep ? WideRow.From(msg) : null);
    }

    /// <summary>Stamps the projected record's log position into the lap column.</summary>
    sealed class PositionStampingProjection : WideProjection {
        public PositionStampingProjection(string table) : base(table, register: false) =>
            Project<LapCompleted>((msg, context) =>
                WideRow.From(msg) with { Lap = context.Record.Position.LogPosition.Value });
    }

    /// <summary>Counts the table from inside the handler, while its own row is still buffered.</summary>
    sealed class ObservingProjection : WideProjection {
        public ObservingProjection(string table, Func<int> countRows) : base(table, register: false) =>
            Project<LapCompleted>((msg, _) => {
                RowsSeenDuringApply = countRows();
                return WideRow.From(msg);
            });

        public int RowsSeenDuringApply { get; private set; } = -1;
    }

    sealed class ThrowingProjection : WideProjection {
        public ThrowingProjection(string table) : base(table, register: false) =>
            Project<LapCompleted>((_, _) =>
                throw new InvalidOperationException("Deliberate handler failure."));
    }

    sealed class NullableProjection : DuckDBAppendProjection<NullableRow, NullableRowBinder> {
        readonly string _table;

        public NullableProjection(string table) {
            _table = table;

            Project<LapCompleted>((_, _) => new NullableRow(null, null, null, null));
        }

        protected override string Table => _table;
    }

    #endregion

    #region ->> Rows <<-

    public readonly record struct WideRow(string Driver, long Lap, double Elapsed, bool Clean) {
        public static WideRow From(LapCompleted message) =>
            new(message.Driver, message.LapNumber, message.ElapsedSeconds, message.Clean);

        public static WideRow From(PitStopPerformed message) =>
            new(message.Driver, message.Lap, message.DurationSeconds, Clean: true);
    }

    public readonly struct WideRowBinder
        : IRowBinder<WideRow, Kurrent.Quack.Appender, Kurrent.Quack.Appender.Row> {
        public static Kurrent.Quack.Appender.Row Bind(in WideRow row, Kurrent.Quack.Appender source) =>
            new(source) {
                row.Driver,
                row.Lap,
                row.Elapsed,
                row.Clean,
            };
    }

    public readonly record struct NullableRow(string? Driver, long? Lap, double? Elapsed, bool? Clean);

    public readonly struct NullableRowBinder
        : IRowBinder<NullableRow, Kurrent.Quack.Appender, Kurrent.Quack.Appender.Row> {
        public static Kurrent.Quack.Appender.Row Bind(in NullableRow row, Kurrent.Quack.Appender source) =>
            new(source) {
                row.Driver,
                row.Lap,
                row.Elapsed,
                row.Clean,
            };
    }

    #endregion

    #region ->> Messages <<-

    public sealed record LapCompleted(string Driver, int LapNumber, double ElapsedSeconds, bool Clean);

    public sealed record PitStopPerformed(string Driver, int Lap, double DurationSeconds);

    LapCompleted NewLap() =>
        new(Faker.Name.FullName(), Faker.Random.Int(1, 60), Faker.Random.Double(60, 120), Faker.Random.Bool());

    PitStopPerformed NewPitStop() =>
        new(Faker.Name.FullName(), Faker.Random.Int(1, 60), Faker.Random.Double(2, 8));

    #endregion

    #region ->> Arranging <<-

    long _position;

    long NextPosition() => ++_position;

    Record Decoded<T>(T message) where T : class =>
        ProjectionTestRecords.Decoded(message, NextPosition());

    static Record Decoded<T>(T message, long logPosition) where T : class =>
        ProjectionTestRecords.Decoded(message, logPosition);

    Record Undecoded() => ProjectionTestRecords.Undecoded(NextPosition());

    static BatchContext BatchOf(params Record[] records) => ProjectionTestRecords.BatchOf(records);

    static string CreateWideTable(DuckDBAdvancedConnection connection) =>
        CreateTable(connection, """
            driver  VARCHAR NOT NULL,
            lap     BIGINT  NOT NULL,
            elapsed DOUBLE  NOT NULL,
            clean   BOOLEAN NOT NULL
            """);

    static string CreateNullableTable(DuckDBAdvancedConnection connection) =>
        CreateTable(connection, """
            driver  VARCHAR,
            lap     BIGINT,
            elapsed DOUBLE,
            clean   BOOLEAN
            """);

    static string CreateTable(DuckDBAdvancedConnection connection, string columns) {
        var table = $"appended_{TestId.New()}";

        using var command = connection.CreateCommand();

        command.CommandText = $"""CREATE TABLE "{table}" ({columns})""";
        command.ExecuteNonQuery();

        return table;
    }

    #endregion

    #region ->> Reading back <<-

    static WideRow[] ReadWideRows(DuckDBAdvancedConnection connection, string table) {
        using var command = connection.CreateCommand();

        command.CommandText = $"""SELECT driver, lap, elapsed, clean FROM "{table}" """;

        using var reader = command.ExecuteReader();

        var rows = new List<WideRow>();

        while (reader.Read())
            rows.Add(new WideRow(
                reader.GetString(0), reader.GetInt64(1), reader.GetDouble(2), reader.GetBoolean(3)));

        return rows.ToArray();
    }

    static int CountRows(DuckDBAdvancedConnection connection, string table) =>
        Convert.ToInt32(Scalar(connection, $"""SELECT COUNT(*) FROM "{table}" """));

    static object? Scalar(DuckDBAdvancedConnection connection, string sql) {
        using var command = connection.CreateCommand();

        command.CommandText = sql;

        return command.ExecuteScalar();
    }

    #endregion
}
