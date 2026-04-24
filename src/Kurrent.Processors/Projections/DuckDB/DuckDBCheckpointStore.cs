


using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Models;
using Kurrent.Projections.DuckDB;

public class DuckDBCheckpointStore(DuckDBConnectionProvider connectionProvider, ILogger<DuckDBCheckpointStore> logger) : ICheckpointStore {
    public string ProcessorId { get; }

    public ValueTask<ProcessorCheckpoint> LoadLatestAsync(CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask<ProcessorCheckpoint> LoadLatestAsync(Partition[] partitions, SubscriptionInitialPosition initialPosition, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask StoreAsync(string droneId, string checkpointId, PartitionPosition[] positions, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask ResetAsync(string droneId, string checkpointId, PartitionPosition[] positions, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask ResetAsync(string droneId, string checkpointId, RecordPosition position, CancellationToken ct = default) =>
        throw new NotImplementedException();
}

// // ReSharper disable RedundantAnonymousTypePropertyName
// // ReSharper disable MethodHasAsyncOverload
//
// using Dapper;
// using Kurrent.Quack;
// using Kurrent.Surge.Projectors;
// // ReSharper disable UseAwaitUsing
//
// namespace Kurrent.Surge.DuckDB;
//
// public partial class DuckDBCheckpointStore(string checkpointKey, DuckDBAdvancedConnection connection, ILogger? logger = null) : IProjectionCheckpointStore {
//     string                   CheckpointKey { get; } = checkpointKey;
//     DuckDBAdvancedConnection Connection    { get; } = connection;
//     ILogger                  Logger        { get; } = logger ?? NullLogger.Instance;
//
//     DuckDBCheckpoint CurrentCheckpoint { get; set; } = DuckDBCheckpoint.None;
//
//     public ValueTask EnsureStoreExists(CancellationToken ct) {
//         ct.ThrowIfCancellationRequested();
//
//         LogTableCreation(Logger, CheckpointKey);
//
//         try {
//             using var tx = Connection.BeginTransaction();
//
//             Connection.Execute("CREATE SEQUENCE IF NOT EXISTS seq_checkpoints_id START 1;");
//
//             Connection.Execute(
//                 """
//                 CREATE TABLE IF NOT EXISTS checkpoints (
//                     id         BIGINT      PRIMARY KEY DEFAULT nextval('seq_checkpoints_id'),
//                     key        TEXT        UNIQUE DEFAULT '',
//                     partition  INT         DEFAULT NULL,
//                     position   UBIGINT     DEFAULT NULL,
//                     timestamp  TIMESTAMPTZ DEFAULT current_localtimestamp()
//                 );
//                 """
//             );
//             tx.CommitOnDispose();
//         }
//         catch (Exception ex) {
//             throw new FailedToCreateCheckpointsTableException(CheckpointKey, ex);
//         }
//
//         var tableExists = Connection.TableExists("checkpoints");
//
//         return !tableExists ? throw new FailedToCreateCheckpointsTableException(CheckpointKey) : ValueTask.CompletedTask;
//     }
//
//     public ValueTask<RecordPosition> LoadCheckpoint(CancellationToken ct) {
//         ct.ThrowIfCancellationRequested();
//
//         const string sql =
//             """
//             INSERT INTO checkpoints (key) VALUES ($key)
//             ON CONFLICT (key) DO NOTHING;
//             SELECT id, position FROM checkpoints WHERE key = $key;
//             """;
//
//         LogCheckpointLoading(Logger, CheckpointKey);
//
//         try {
//             using var tx = Connection.BeginTransaction();
//
//             var checkpoint = Connection.QuerySingle<DuckDBCheckpoint>(sql, new { key = CheckpointKey });
//
//             tx.CommitOnDispose();
//
//             LogCheckpointLoaded(Logger, CheckpointKey, checkpoint.Id, checkpoint.Position);
//
//             return ValueTask.FromResult<RecordPosition>(CurrentCheckpoint = checkpoint);
//         }
//         catch (Exception ex) {
//             throw new FailedToLoadCheckpointException(CheckpointKey, ex);
//         }
//     }
//
//     public ValueTask<RecordPosition> StoreCheckpoint(RecordPosition position, CancellationToken ct) {
//         ct.ThrowIfCancellationRequested();
//
//         LogCheckpointStoring(Logger, CheckpointKey, position);
//
//         const string storePositionSql =
//             """
//             UPDATE checkpoints
//             SET position  = $position,
//                 timestamp = current_localtimestamp()
//             WHERE id = $id AND position = $expected_position
//             """;
//
//         const string storeFirstPositionSql =
//             """
//             UPDATE checkpoints
//             SET position  = $position,
//                 timestamp = current_localtimestamp()
//             WHERE id = $id AND position IS NULL
//             """;
//
//         int affected;
//
//         try {
//             if (CurrentCheckpoint.Position is null) {
//                 affected = Connection
//                     .Execute(
//                         storeFirstPositionSql,
//                         new {
//                             id       = CurrentCheckpoint.Id,
//                             position = (ulong?)position
//                         }
//                     );
//             }
//             else {
//                 affected = Connection
//                     .Execute(
//                         storePositionSql,
//                         new {
//                             id                = CurrentCheckpoint.Id,
//                             position          = (ulong?)position,
//                             expected_position = CurrentCheckpoint.Position
//                         }
//                     );
//             }
//         }
//         catch (Exception ex) {
//             throw new FailedToStoreCheckpointException(CheckpointKey, position, ex);
//         }
//
//         if (affected > 0)
//             LogCheckpointStored(Logger, CheckpointKey, position);
//         else
//             throw new FailedToStoreCheckpointException(CheckpointKey, position);
//
//         return ValueTask.FromResult<RecordPosition>(CurrentCheckpoint = CurrentCheckpoint with {
//             Position = position
//         });
//     }
//
//     public ValueTask<RecordPosition> ResetCheckpoint(CancellationToken ct) {
//         ct.ThrowIfCancellationRequested();
//
//         const string sql =
//             """
//             UPDATE checkpoints
//             SET position  = NULL,
//                 timestamp = current_localtimestamp()
//             WHERE id = $id
//             """;
//
//         try {
//             Connection.Execute(sql, new { id = CurrentCheckpoint.Id });
//         }
//         catch (Exception ex) {
//             throw new FailedToResetCheckpointException(CheckpointKey, ex);
//         }
//
//         return ValueTask.FromResult<RecordPosition>(CurrentCheckpoint = CurrentCheckpoint with {
//             Position = null
//         });
//     }
//
//     record DuckDBCheckpoint {
//         public static readonly DuckDBCheckpoint None = new() { Id = 0 };
//
//         public int    Id       { get; init; }
//         public ulong? Position { get; init; }
//
//         public static implicit operator RecordPosition(DuckDBCheckpoint checkpoint) =>
//             checkpoint.Position is null ? RecordPosition.Unset : RecordPosition.ForLog(checkpoint.Position);
//     }
//
//     #region Logging
//
//     [LoggerMessage(Level = LogLevel.Trace, Message = "[{Key}] Creating checkpoints table")]
//     static partial void LogTableCreation(ILogger logger, string key);
//
//     [LoggerMessage(Level = LogLevel.Debug, Message = "[{Key}] Checkpoints table created successfully")]
//     static partial void LogTableCreated(ILogger logger, string key);
//
//     [LoggerMessage(Level = LogLevel.Debug, Message = "[{Key}] Checkpoints table already exists")]
//     static partial void LogTableAlreadyExists(ILogger logger, string key);
//
//     [LoggerMessage(Level = LogLevel.Trace, Message = "[{Key}] Loading checkpoint...")]
//     static partial void LogCheckpointLoading(ILogger logger, string key);
//
//     [LoggerMessage(Level = LogLevel.Debug, Message = "[{Key}] Loaded checkpoint {Position} (Id: {Id})")]
//     static partial void LogCheckpointLoaded(ILogger logger, string key, int id, ulong? position);
//
//     [LoggerMessage(Level = LogLevel.Trace, Message = "[{Key}] Storing checkpoint {Position}")]
//     static partial void LogCheckpointStoring(ILogger logger, string key, ulong? position);
//
//     [LoggerMessage(Level = LogLevel.Debug, Message = "[{Key}] Stored checkpoint {Position}")]
//     static partial void LogCheckpointStored(ILogger logger, string key, ulong? position);
//
//     #endregion
// }
