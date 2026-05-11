using Kurrent.Client.Streams;
using Kurrent.Streaming.Checkpointing;
using Kurrent.Quack;

namespace Kurrent.Projections.DuckDB;

/// <summary>
/// DuckDB-backed <see cref="CheckpointStoreBase"/>. One row per <c>(processor_id, partition)</c>,
/// updated under optimistic concurrency control — each update is fenced on the previously
/// observed <c>log_position</c> for that partition.
/// </summary>
/// <remarks>
/// Stream/revision values are persisted exactly as supplied by the caller. No log-position
/// resolution. Full wipes delete every row for the processor; selective resets overwrite
/// the supplied partitions without fencing (reset is a deliberate override).
/// </remarks>
[PublicAPI]
public sealed partial class DuckDBCheckpointStore(
    string processorId,
    IDuckDBConnectionProvider connectionProvider,
    TimeProvider time,
    ILogger<DuckDBCheckpointStore> logger
)
    : CheckpointStoreBase(processorId, time, logger) {
    
    /// <summary>
    /// Checkpoint table name. Quoted in every SQL statement below because the <c>$</c>
    /// prefix is not a legal bare identifier in DuckDB.
    /// </summary>
    internal const string TableName = "$processors_checkpoints";

    /// <summary>
    /// Fencing state: partition → last-known <c>log_position</c> persisted from this
    /// instance. Populated on load; updated after successful store / selective reset;
    /// cleared on full wipe. Store paths consult this to decide UPDATE-with-expected vs
    /// INSERT-if-absent and to detect concurrent modifications.
    /// </summary>
    readonly Dictionary<int, long> _lastStored = new();

    protected override string ReadyTarget => TableName;

    #region ->> Hooks <<-

    protected override ValueTask OnInitializeAsync(CancellationToken ct) {
        using var connection = connectionProvider.GetConnection();

        var existed = connection.TableExists(TableName);
        if (existed)
            LogTableAlreadyExists(ProcessorId);
        else {
            LogTableCreation(ProcessorId);
            
            connection.ExecuteNonQuery<EnsureTableStatement>();

            if (!connection.TableExists(TableName))
                throw new InvalidOperationException($"Failed to create checkpoints table '{TableName}'.");

            LogTableCreated(ProcessorId);
        }

        return ValueTask.CompletedTask;
    }

    protected override ValueTask<ProcessorCheckpoint> OnLoadLatestAsync(CancellationToken ct) {
        var positions       = new List<RecordPosition>();
        var latestTimestamp = DateTimeOffset.MinValue;

        _lastStored.Clear();

        using var connection = connectionProvider.GetConnection();
        
        foreach (ref readonly var row in connection
            .ExecuteQuery<ProcessorIdArgs, CheckpointRow, LoadForProcessorStatement>(new(ProcessorId))) {
            positions.Add(MapRow(in row));
            _lastStored[row.Partition] = row.LogPosition;

            var ts = new DateTimeOffset(DateTime.SpecifyKind(row.Timestamp, DateTimeKind.Utc));
            if (ts > latestTimestamp)
                latestTimestamp = ts;
        }
        

        if (positions.Count == 0)
            return ValueTask.FromResult(ProcessorCheckpoint.Empty);

        return ValueTask.FromResult(new ProcessorCheckpoint {
            Positions = positions.ToArray(),
            Timestamp = latestTimestamp
        });
    }

    protected override ValueTask OnStoreAsync(
        string droneId,
        string checkpointId,
        RecordPosition[] positions,
        CancellationToken ct
    ) {
        var timestamp = Time.GetUtcNow().UtcDateTime;
        var pending   = new Dictionary<int, long>(positions.Length);

        using var connection = connectionProvider.GetConnection();

        using var tx = connection.BeginTransaction();

        foreach (var p in positions) {
            var partition      = p.Partition.Value;
            var newLogPosition = (long)p.LogPosition;

            if (_lastStored.TryGetValue(partition, out var expected)) {
                var affected = connection.ExecuteNonQuery<UpdateWithExpectedArgs, UpdateWithExpectedStatement>(
                    BuildUpdateArgs(
                        p, droneId, checkpointId,
                        timestamp, expected));

                if (affected == 0)
                    throw new InvalidOperationException($"Concurrent modification detected for partition {partition}: expected log_position {expected}. Reload and retry.");
            }
            else {
                var affected = connection.ExecuteNonQuery<UpsertArgs, InsertIfAbsentStatement>(
                    BuildUpsertArgs(p, droneId, checkpointId, timestamp));

                if (affected == 0)
                    throw new InvalidOperationException($"Row for partition {partition} already exists; cannot first-write. Reload and retry.");
            }

            pending[partition] = newLogPosition;
        }

        tx.CommitOnDispose();
        
        foreach (var (partition, logPosition) in pending)
            _lastStored[partition] = logPosition;

        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnResetAsync(
        string droneId,
        string checkpointId,
        RecordPosition[] positions,
        CancellationToken ct
    ) {
        var timestamp = Time.GetUtcNow().UtcDateTime;

        using var connection = connectionProvider.GetConnection();
        using var tx         = connection.BeginTransaction();

        if (positions.Length == 0)
            connection.ExecuteNonQuery<ProcessorIdArgs, DeleteAllStatement>(new(ProcessorId));
        else {
            foreach (var p in positions)
                connection.ExecuteNonQuery<UpsertArgs, UpsertStatement>(
                    BuildUpsertArgs(
                        p, droneId, checkpointId,
                        timestamp));
        }

        tx.CommitOnDispose();


        if (positions.Length == 0)
            _lastStored.Clear();
        else {
            foreach (var p in positions)
                _lastStored[p.Partition.Value] = p.LogPosition;
        }

        return ValueTask.CompletedTask;
    }

    #endregion

    #region ->> Mapping <<-

    UpsertArgs BuildUpsertArgs(RecordPosition p, string droneId, string checkpointId, DateTime timestamp) =>
        new(
            ProcessorId,
            p.Partition.Value,
            p.LogPosition,
            p.Stream.IsUnset ? null : (string)p.Stream,
            p.Revision == Revision.Unset ? null : (int?)p.Revision,
            droneId,
            checkpointId,
            timestamp
        );

    UpdateWithExpectedArgs BuildUpdateArgs(
        RecordPosition p,
        string droneId,
        string checkpointId,
        DateTime timestamp,
        long expectedLogPosition
    ) =>
        new(
            ProcessorId,
            p.Partition.Value,
            p.LogPosition,
            p.Stream.IsUnset ? null : (string)p.Stream,
            p.Revision == Revision.Unset ? null : (int?)p.Revision,
            droneId,
            checkpointId,
            timestamp,
            expectedLogPosition
        );

    static RecordPosition MapRow(in CheckpointRow row) {
        var partition = Partition.From(row.Partition);

        return row.Stream is not null && row.StreamRevision is not null
            ? RecordPosition.From(row.LogPosition, row.Stream, Revision.From(row.StreamRevision.Value), partition)
            : RecordPosition.From(row.LogPosition, partition);
    }

    #endregion

    #region ->> Statements <<-

    readonly record struct ProcessorIdArgs(string ProcessorId);

    readonly record struct UpsertArgs(
        string   ProcessorId,
        int      Partition,
        long     LogPosition,
        string?  Stream,
        int?     StreamRevision,
        string   DroneId,
        string   CheckpointId,
        DateTime Timestamp
    );

    readonly record struct UpdateWithExpectedArgs(
        string   ProcessorId,
        int      Partition,
        long     LogPosition,
        string?  Stream,
        int?     StreamRevision,
        string   DroneId,
        string   CheckpointId,
        DateTime Timestamp,
        long     ExpectedLogPosition
    );

    readonly record struct CheckpointRow(
        int      Partition,
        long     LogPosition,
        string?  Stream,
        int?     StreamRevision,
        DateTime Timestamp
    );

    struct EnsureTableStatement : IParameterlessStatement {
        public static ReadOnlySpan<byte> CommandText => """
            CREATE TABLE IF NOT EXISTS "$processors_checkpoints" (
                processor_id    VARCHAR   NOT NULL,
                partition       INTEGER   NOT NULL,
                log_position    BIGINT    NOT NULL,
                stream          VARCHAR,
                stream_revision INTEGER,
                drone_id        VARCHAR   NOT NULL,
                checkpoint_id   VARCHAR   NOT NULL,
                timestamp       TIMESTAMP NOT NULL,
                PRIMARY KEY (processor_id, partition)
            );
            """u8;
    }

    struct LoadForProcessorStatement : IQuery<ProcessorIdArgs, CheckpointRow> {
        public static StatementBindingResult Bind(in ProcessorIdArgs args, PreparedStatement statement) =>
            new(statement) { args.ProcessorId };

        public static ReadOnlySpan<byte> CommandText => """
            SELECT partition, log_position, stream, stream_revision, timestamp
            FROM "$processors_checkpoints"
            WHERE processor_id = $1
            ORDER BY partition;
            """u8;

        public static CheckpointRow Parse(ref DataChunk.Row row) =>
            new(
                row.ReadInt32(),
                row.ReadInt64(),
                row.TryReadString(),
                row.TryReadInt32(),
                row.ReadDateTime()
            );
    }

    struct InsertIfAbsentStatement : IPreparedStatement<UpsertArgs> {
        public static StatementBindingResult Bind(in UpsertArgs args, PreparedStatement statement) =>
            new(statement) {
                args.ProcessorId,
                args.Partition,
                args.LogPosition,
                args.Stream,
                args.StreamRevision,
                args.DroneId,
                args.CheckpointId,
                args.Timestamp
            };

        public static ReadOnlySpan<byte> CommandText => """
            INSERT INTO "$processors_checkpoints"
                (processor_id, partition, log_position, stream, stream_revision, drone_id, checkpoint_id, timestamp)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            ON CONFLICT (processor_id, partition) DO NOTHING;
            """u8;
    }

    struct UpdateWithExpectedStatement : IPreparedStatement<UpdateWithExpectedArgs> {
        public static StatementBindingResult Bind(in UpdateWithExpectedArgs args, PreparedStatement statement) =>
            new(statement) {
                args.ProcessorId,
                args.Partition,
                args.LogPosition,
                args.Stream,
                args.StreamRevision,
                args.DroneId,
                args.CheckpointId,
                args.Timestamp,
                args.ExpectedLogPosition
            };

        public static ReadOnlySpan<byte> CommandText => """
            UPDATE "$processors_checkpoints"
            SET log_position    = $3,
                stream          = $4,
                stream_revision = $5,
                drone_id        = $6,
                checkpoint_id   = $7,
                timestamp       = $8
            WHERE processor_id = $1
              AND partition    = $2
              AND log_position = $9;
            """u8;
    }

    struct UpsertStatement : IPreparedStatement<UpsertArgs> {
        public static StatementBindingResult Bind(in UpsertArgs args, PreparedStatement statement) =>
            new(statement) {
                args.ProcessorId,
                args.Partition,
                args.LogPosition,
                args.Stream,
                args.StreamRevision,
                args.DroneId,
                args.CheckpointId,
                args.Timestamp
            };

        public static ReadOnlySpan<byte> CommandText => """
            INSERT INTO "$processors_checkpoints"
                (processor_id, partition, log_position, stream, stream_revision, drone_id, checkpoint_id, timestamp)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            ON CONFLICT (processor_id, partition) DO UPDATE SET
                log_position    = EXCLUDED.log_position,
                stream          = EXCLUDED.stream,
                stream_revision = EXCLUDED.stream_revision,
                drone_id        = EXCLUDED.drone_id,
                checkpoint_id   = EXCLUDED.checkpoint_id,
                timestamp       = EXCLUDED.timestamp;
            """u8;
    }

    struct DeleteAllStatement : IPreparedStatement<ProcessorIdArgs> {
        public static StatementBindingResult Bind(in ProcessorIdArgs args, PreparedStatement statement) =>
            new(statement) { args.ProcessorId };

        public static ReadOnlySpan<byte> CommandText => """
            DELETE FROM "$processors_checkpoints" WHERE processor_id = $1;
            """u8;
    }

    #endregion

    #region ->> Logging <<-

    [LoggerMessage(Level = LogLevel.Trace, Message = "[{ProcessorId}] Creating checkpoints table")]
    partial void LogTableCreation(string processorId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{ProcessorId}] Checkpoints table created")]
    partial void LogTableCreated(string processorId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}] Checkpoints table already exists")]
    partial void LogTableAlreadyExists(string processorId);

    #endregion
}
