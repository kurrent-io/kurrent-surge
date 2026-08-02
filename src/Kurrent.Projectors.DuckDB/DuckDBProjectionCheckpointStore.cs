using System.Text;
using Kurrent.Projectors.Transactional;
using Kurrent.Quack;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Models;

namespace Kurrent.Projectors.DuckDB;

/// <summary>
/// The DuckDB backend's checkpoint storage: one row per <c>(projection_id, partition)</c> in a control
/// table living in the same database as the projected state.
/// </summary>
/// <remarks>
/// <para>
/// Stateless — every method takes the client to run on, so a write lands in whichever transaction the
/// caller has open. <see cref="Store"/> is always handed the projection's open transaction, which is what
/// makes positions and projected rows one durable fact.
/// </para>
/// <para>
/// Every statement is a Quack prepared statement: parsed once per connection, bound positionally, no
/// per-call command objects or parameter allocations. Quack's API is synchronous throughout, so these
/// methods satisfy the async contract by completing synchronously — the interface is async because other
/// backends will genuinely need it, not because this one does.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class DuckDBProjectionCheckpointStore(string projectionId, TimeProvider time)
    : IProjectionCheckpointStore<DuckDBAdvancedConnection> {
    /// <summary>
    /// Quoted in every statement because the <c>$</c> prefix is not a legal bare identifier in DuckDB.
    /// </summary>
    public const string TableName = "$projection_checkpoints";

    /// <inheritdoc/>
    public ValueTask EnsureStoreExists(DuckDBAdvancedConnection client, CancellationToken cancellationToken) {
        client.ExecuteNonQuery<EnsureStoreExistsStatement>();
        return ValueTask.CompletedTask;
    }

    // The resume floor: the positions this store last read or wrote. Guards every Store — a row that
    // no longer holds its floor was advanced by another writer. One instance lives one run, so a
    // restart reads the floor back from the durable rows through Load.
    RecordPosition[] _floor = [];

    /// <inheritdoc/>
    public ValueTask<RecordPosition[]> Load(
        DuckDBAdvancedConnection client,
        Partition[] partitions,
        SubscriptionInitialPosition initialPosition,
        CancellationToken cancellationToken
    ) {
        var stored = new Dictionary<Partition, RecordPosition>(partitions.Length);

        foreach (ref readonly var position in client.ExecuteQuery<LoadArgs, RecordPosition, LoadStatement>(new(projectionId)))
            stored[position.Partition] = position;

        var fallback = initialPosition == SubscriptionInitialPosition.Earliest
            ? RecordPosition.Earliest
            : RecordPosition.Latest;

        var resumeFrom = partitions
            .Select(partition => stored.TryGetValue(partition, out var position)
                ? position
                : fallback with { Partition = partition })
            .ToArray();

        _floor = resumeFrom;

        return ValueTask.FromResult(resumeFrom);
    }

    /// <inheritdoc/>
    public ValueTask Store(
        DuckDBAdvancedConnection client,
        RecordPosition[] next,
        CancellationToken cancellationToken
    ) {
        var floors = _floor.ToDictionary(p => p.Partition, p => (long)p.LogPosition);

        // One statement for the whole batch. Each row whose guard held comes back through RETURNING, so the
        // partitions that do NOT come back are exactly the ones something else moved.
        var applied = new HashSet<Partition>(next.Length);

        foreach (ref readonly var partition in client.ExecuteQuery<StoreArgs, Partition, StoreStatement>(
            StoreArgs.From(projectionId, next, floors, time.GetUtcNow().UtcDateTime)))
            applied.Add(partition);

        if (applied.Count == next.Length) {
            _floor = next;
            return ValueTask.CompletedTask;
        }

        var stalled = next
            .Where(position => !applied.Contains(position.Partition))
            .Select(position => $"{position.Partition} (expected {floors.GetValueOrDefault(position.Partition, 0L)})");

        throw new InvalidOperationException(
            $"Checkpoints for '{projectionId}' moved underneath this projection — {string.Join(", ", stalled)}. "
          + "Another writer owns this projection's checkpoints.");
    }

    /// <inheritdoc/>
    public ValueTask Reset(
        DuckDBAdvancedConnection client,
        LogPosition position,
        Partition[] partitions,
        CancellationToken cancellationToken
    ) {
        // Partitions are bound as VARCHAR and cast back in SQL: Quack binds list parameters for strings and
        // blobs only, and the merge source needs the whole set in one statement.
        var owned = partitions.Select(partition => partition.Value.ToString()).ToArray();

        client.ExecuteNonQuery<ResetArgs, ResetStatement>(
            new(projectionId, owned, position, time.GetUtcNow().UtcDateTime));

        _floor = partitions
            .Select(partition => RecordPosition.From((long)position, partition))
            .ToArray();

        return ValueTask.CompletedTask;
    }

    #region ->> Arguments <<-

    readonly record struct LoadArgs(string ProjectionId);

    /// <summary>
    /// One batch of positions, transposed into parallel columns so the whole set travels as a single
    /// statement. Everything is bound as <c>VARCHAR</c> and cast back in SQL — Quack binds list parameters
    /// for strings and blobs only.
    /// </summary>
    readonly record struct StoreArgs(
        string   ProjectionId,
        string[] Partitions,
        string[] LogPositions,
        string?[] Streams,
        string?[] StreamRevisions,
        string[] ExpectedFloors,
        DateTime Timestamp
    ) {
        public static StoreArgs From(
            string projectionId,
            RecordPosition[] positions,
            Dictionary<Partition, long> floors,
            DateTime timestamp
        ) {
            var partitions = new string[positions.Length];
            var logs       = new string[positions.Length];
            var streams    = new string?[positions.Length];
            var revisions  = new string?[positions.Length];
            var expected   = new string[positions.Length];

            for (var index = 0; index < positions.Length; index++) {
                var position = positions[index];

                partitions[index] = position.Partition.Value.ToString();
                logs[index]       = ((long)position.LogPosition).ToString();
                streams[index]    = position.Stream.IsUnset ? null : (string)position.Stream;
                revisions[index]  = position.Revision.IsUnset ? null : ((int)position.Revision).ToString();
                expected[index]   = floors.GetValueOrDefault(position.Partition, 0L).ToString();
            }

            return new(projectionId, partitions, logs, streams, revisions, expected, timestamp);
        }
    }

    readonly record struct ResetArgs(string ProjectionId, string[] Partitions, long LogPosition, DateTime Timestamp);

    #endregion

    #region ->> Statements <<-

    /// <summary>
    /// The statement text, encoded once at type initialization. A <c>u8</c> literal cannot interpolate, so
    /// spelling the SQL out here is what keeps <see cref="TableName"/> declared in exactly one place.
    /// </summary>
    /// <remarks>
    /// Each <c>CommandText</c> hands back a span over the array it is given, so nothing is allocated per
    /// call and the statement is still parsed once per connection.
    /// </remarks>
    static class Sql {
        public static readonly byte[] EnsureStoreExists = Utf8($"""
            CREATE TABLE IF NOT EXISTS "{TableName}" (
                projection_id    VARCHAR   NOT NULL,
                partition       INTEGER   NOT NULL,
                log_position    BIGINT    NOT NULL,
                stream          VARCHAR,
                stream_revision INTEGER,
                timestamp       TIMESTAMP NOT NULL,
                PRIMARY KEY (projection_id, partition)
            );
            """);

        public static readonly byte[] Load = Utf8($"""
            SELECT partition, log_position, stream, stream_revision
            FROM "{TableName}"
            WHERE projection_id = $1
            ORDER BY partition;
            """);

        // The whole batch in one statement, guarded per row: a partition advances only if it still holds the
        // position this projection last committed. That guard is the entire concurrency and monotonicity
        // story — a row whose floor has moved matches no branch, so it silently does nothing and is absent
        // from RETURNING, which is how Store (below) detects it and throws.
        //
        // Every column arrives as VARCHAR and is cast back: Quack binds list parameters for strings and
        // blobs only, and parallel unnest zips the columns positionally.
        public static readonly byte[] Store = Utf8($"""
            MERGE INTO "{TableName}" AS t
            USING (
                SELECT $1                                   AS projection_id,
                       unnest($2::VARCHAR[])::INTEGER       AS partition,
                       unnest($3::VARCHAR[])::BIGINT        AS log_position,
                       unnest($4::VARCHAR[])                AS stream,
                       unnest($5::VARCHAR[])::INTEGER       AS stream_revision,
                       unnest($6::VARCHAR[])::BIGINT        AS expected_floor,
                       $7                                   AS timestamp
            ) AS s
               ON t.projection_id = s.projection_id AND t.partition = s.partition
             WHEN MATCHED AND t.log_position = s.expected_floor
                  THEN UPDATE SET log_position    = s.log_position,
                                  stream          = s.stream,
                                  stream_revision = s.stream_revision,
                                  timestamp       = s.timestamp
             WHEN NOT MATCHED
                  THEN INSERT (projection_id, partition, log_position, stream, stream_revision, timestamp)
                       VALUES (s.projection_id, s.partition, s.log_position, s.stream, s.stream_revision, s.timestamp)
             RETURNING partition;
            """);

        // The guard on NOT MATCHED BY SOURCE is load-bearing: that branch scans the whole table, not this
        // processor's slice, so without it a reset deletes every other projection's checkpoints — silently,
        // since they share this database by design.
        //
        // Rewinding rather than deleting is equally load-bearing: a partition left without a row falls back
        // to the configured initial position, which silently discards the explicit start.
        //
        // Partitions arrive as VARCHAR because Quack binds list parameters for strings and blobs only.
        public static readonly byte[] Reset = Utf8($"""
            MERGE INTO "{TableName}" AS t
            USING (
                SELECT $1                             AS projection_id,
                       unnest($2::VARCHAR[])::INTEGER AS partition,
                       $3                             AS log_position,
                       NULL::VARCHAR                  AS stream,
                       NULL::INTEGER                  AS stream_revision,
                       $4                             AS timestamp
            ) AS s
            USING (projection_id, partition)
            WHEN MATCHED     THEN UPDATE
            WHEN NOT MATCHED THEN INSERT BY NAME
            WHEN NOT MATCHED BY SOURCE AND t.projection_id = $1 THEN DELETE;
            """);

        static byte[] Utf8(string sql) => Encoding.UTF8.GetBytes(sql);
    }

    struct EnsureStoreExistsStatement : IParameterlessStatement {
        public static ReadOnlySpan<byte> CommandText => Sql.EnsureStoreExists;
    }

    struct LoadStatement : IQuery<LoadArgs, RecordPosition> {
        public static StatementBindingResult Bind(in LoadArgs args, PreparedStatement statement) =>
            new(statement) { args.ProjectionId };

        public static ReadOnlySpan<byte> CommandText => Sql.Load;

        public static RecordPosition Parse(ref DataChunk.Row row) {
            var partition   = Partition.From(row.ReadInt32());
            var logPosition = row.ReadInt64();
            var stream      = row.TryReadString();
            var revision    = row.TryReadInt32();

            return stream is not null && revision is not null
                ? RecordPosition.From(logPosition, stream, Revision.From(revision.Value), partition)
                : RecordPosition.From(logPosition, partition);
        }
    }

    struct StoreStatement : IQuery<StoreArgs, Partition> {
        public static StatementBindingResult Bind(in StoreArgs args, PreparedStatement statement) =>
            new(statement) {
                args.ProjectionId,
                args.Partitions,
                args.LogPositions,
                args.Streams,
                args.StreamRevisions,
                args.ExpectedFloors,
                args.Timestamp
            };

        public static ReadOnlySpan<byte> CommandText => Sql.Store;

        public static Partition Parse(ref DataChunk.Row row) => Partition.From(row.ReadInt32());
    }

    struct ResetStatement : IPreparedStatement<ResetArgs> {
        public static StatementBindingResult Bind(in ResetArgs args, PreparedStatement statement) =>
            new(statement) {
                args.ProjectionId,
                args.Partitions,
                args.LogPosition,
                args.Timestamp
            };

        public static ReadOnlySpan<byte> CommandText => Sql.Reset;
    }

    #endregion
}
