using Kurrent.Quack;

namespace Kurrent.Surge.Mcp;

public sealed partial class DuckDBTaskStore {
    #region ->> Mapping <<-

    static TaskRecord FromRow(in TaskRow r) => new() {
        TaskId            = r.TaskId,
        SessionId         = r.SessionId,
        Status            = StatusFromDb(r.Status),
        StatusMessage     = r.StatusMessage,
        CreatedAt         = ToUtc(r.CreatedAt),
        LastUpdatedAt     = ToUtc(r.LastUpdatedAt),
        TimeToLive        = FromMs(r.TtlMs),
        ExpiresAt         = r.ExpiresAtTicks is { } ticks ? new DateTimeOffset(ticks, TimeSpan.Zero) : null,
        PollInterval      = FromMs(r.PollIntervalMs),
        RequestJson       = r.RequestJson,
        RequestIdJson     = r.RequestIdJson,
        ResultJson        = r.ResultJson,
        ErrorJson         = r.ErrorJson,
        InputRequestsJson = r.InputRequestsJson
    };

    static InsertArgs ToInsertArgs(TaskRecord r) => new(
        r.TaskId, r.SessionId, StatusToDb(r.Status), r.StatusMessage,
        r.CreatedAt.UtcDateTime, r.LastUpdatedAt.UtcDateTime, ToMs(r.TimeToLive), r.ExpiresAt?.UtcTicks,
        ToMs(r.PollInterval), r.RequestJson, r.RequestIdJson, r.ResultJson, r.ErrorJson, r.InputRequestsJson);

    static UpdateArgs ToUpdateArgs(TaskRecord r) => new(
        r.TaskId, r.SessionId, StatusToDb(r.Status), r.StatusMessage,
        r.LastUpdatedAt.UtcDateTime, ToMs(r.TimeToLive), r.ExpiresAt?.UtcTicks, ToMs(r.PollInterval),
        r.RequestJson, r.RequestIdJson, r.ResultJson, r.ErrorJson, r.InputRequestsJson);

    // Status is persisted by canonical name — never by ordinal — so the two SDK versions
    // (which disagree on the Failed/Cancelled ordinals) can share the same table.
    static string StatusToDb(McpTaskState state) => state switch {
        McpTaskState.Working       => "Working",
        McpTaskState.InputRequired => "InputRequired",
        McpTaskState.Completed     => "Completed",
        McpTaskState.Failed        => "Failed",
        McpTaskState.Cancelled     => "Cancelled",
        _                          => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown task state.")
    };

    static McpTaskState StatusFromDb(string value) => value switch {
        "Working"       => McpTaskState.Working,
        "InputRequired" => McpTaskState.InputRequired,
        "Completed"     => McpTaskState.Completed,
        "Failed"        => McpTaskState.Failed,
        "Cancelled"     => McpTaskState.Cancelled,
        _               => throw new InvalidOperationException($"Unknown persisted task state '{value}'.")
    };

    static DateTimeOffset ToUtc(DateTime dt) => new(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
    static long?          ToMs(TimeSpan? ts) => ts is { } v ? (long)v.TotalMilliseconds : null;
    static TimeSpan?      FromMs(long? ms)   => ms is { } v ? TimeSpan.FromMilliseconds(v) : null;

    #endregion

    #region ->> Rows & Args <<-

    // Raw column projection, read positionally in the SELECT-list order below.
    readonly record struct TaskRow(
        string   TaskId,
        string?  SessionId,
        string   Status,
        string?  StatusMessage,
        DateTime CreatedAt,
        DateTime LastUpdatedAt,
        long?    TtlMs,
        long?    ExpiresAtTicks,
        long?    PollIntervalMs,
        string?  RequestJson,
        string?  RequestIdJson,
        string?  ResultJson,
        string?  ErrorJson,
        string?  InputRequestsJson
    );

    readonly record struct EmptyArgs;
    readonly record struct TaskIdArgs(string TaskId);
    readonly record struct SessionCountArgs(string SessionId, long NowTicks);
    readonly record struct DeleteExpiredArgs(long NowTicks);

    readonly record struct ListArgs(
        string?  SessionId,
        int      SessionIsNull,
        long     NowTicks,
        DateTime CursorCreatedAt,
        string   CursorTaskId,
        int      HasCursor
    );

    readonly record struct InsertArgs(
        string   TaskId,
        string?  SessionId,
        string   Status,
        string?  StatusMessage,
        DateTime CreatedAt,
        DateTime LastUpdatedAt,
        long?    TtlMs,
        long?    ExpiresAtTicks,
        long?    PollIntervalMs,
        string?  RequestJson,
        string?  RequestIdJson,
        string?  ResultJson,
        string?  ErrorJson,
        string?  InputRequestsJson
    );

    readonly record struct UpdateArgs(
        string   TaskId,
        string?  SessionId,
        string   Status,
        string?  StatusMessage,
        DateTime LastUpdatedAt,
        long?    TtlMs,
        long?    ExpiresAtTicks,
        long?    PollIntervalMs,
        string?  RequestJson,
        string?  RequestIdJson,
        string?  ResultJson,
        string?  ErrorJson,
        string?  InputRequestsJson
    );

    #endregion

    #region ->> Statements <<-

    struct EnsureTableStatement : IParameterlessStatement {
        public static ReadOnlySpan<byte> CommandText => """
            CREATE TABLE IF NOT EXISTS mcp_tasks (
                task_id             VARCHAR   NOT NULL,
                session_id          VARCHAR,
                status              VARCHAR   NOT NULL,
                status_message      VARCHAR,
                created_at          TIMESTAMP NOT NULL,
                last_updated_at     TIMESTAMP NOT NULL,
                ttl_ms              BIGINT,
                expires_at_ticks    BIGINT,
                poll_interval_ms    BIGINT,
                request_json        VARCHAR,
                request_id_json     VARCHAR,
                result_json         VARCHAR,
                error_json          VARCHAR,
                input_requests_json VARCHAR,
                PRIMARY KEY (task_id)
            );
            """u8;
    }

    struct CountAllStatement : IQuery<EmptyArgs, long> {
        public static StatementBindingResult Bind(in EmptyArgs args, PreparedStatement statement) =>
            new(statement);

        public static ReadOnlySpan<byte> CommandText => """
            SELECT COUNT(*) FROM mcp_tasks;
            """u8;

        public static long Parse(ref DataChunk.Row row) => row.ReadInt64();
    }

    struct CountActiveInSessionStatement : IQuery<SessionCountArgs, long> {
        public static StatementBindingResult Bind(in SessionCountArgs args, PreparedStatement statement) =>
            new(statement) { args.SessionId, args.NowTicks };

        public static ReadOnlySpan<byte> CommandText => """
            SELECT COUNT(*)
            FROM mcp_tasks
            WHERE session_id = $1
              AND (expires_at_ticks IS NULL OR expires_at_ticks > $2);
            """u8;

        public static long Parse(ref DataChunk.Row row) => row.ReadInt64();
    }

    struct SelectByIdStatement : IQuery<TaskIdArgs, TaskRow> {
        public static StatementBindingResult Bind(in TaskIdArgs args, PreparedStatement statement) =>
            new(statement) { args.TaskId };

        public static ReadOnlySpan<byte> CommandText => """
            SELECT task_id, session_id, status, status_message, created_at, last_updated_at,
                   ttl_ms, expires_at_ticks, poll_interval_ms,
                   request_json, request_id_json, result_json, error_json, input_requests_json
            FROM mcp_tasks
            WHERE task_id = $1;
            """u8;

        public static TaskRow Parse(ref DataChunk.Row row) => ReadRow(ref row);
    }

    struct ListStatement : IQuery<ListArgs, TaskRow> {
        public static StatementBindingResult Bind(in ListArgs args, PreparedStatement statement) =>
            new(statement) {
                args.SessionId,
                args.SessionIsNull,
                args.NowTicks,
                args.CursorCreatedAt,
                args.CursorTaskId,
                args.HasCursor
            };

        // $2 (SessionIsNull) short-circuits the session filter for "list all"; $6 (HasCursor)
        // short-circuits the keyset filter for the first page. Ordering matches the cursor tuple.
        public static ReadOnlySpan<byte> CommandText => """
            SELECT task_id, session_id, status, status_message, created_at, last_updated_at,
                   ttl_ms, expires_at_ticks, poll_interval_ms,
                   request_json, request_id_json, result_json, error_json, input_requests_json
            FROM mcp_tasks
            WHERE ($2 <> 0 OR session_id = $1)
              AND (expires_at_ticks IS NULL OR expires_at_ticks > $3)
              AND ($6 = 0 OR created_at > $4 OR (created_at = $4 AND task_id > $5))
            ORDER BY created_at, task_id;
            """u8;

        public static TaskRow Parse(ref DataChunk.Row row) => ReadRow(ref row);
    }

    struct InsertStatement : IPreparedStatement<InsertArgs> {
        public static StatementBindingResult Bind(in InsertArgs args, PreparedStatement statement) =>
            new(statement) {
                args.TaskId, args.SessionId, args.Status, args.StatusMessage,
                args.CreatedAt, args.LastUpdatedAt, args.TtlMs, args.ExpiresAtTicks,
                args.PollIntervalMs, args.RequestJson, args.RequestIdJson,
                args.ResultJson, args.ErrorJson, args.InputRequestsJson
            };

        public static ReadOnlySpan<byte> CommandText => """
            INSERT INTO mcp_tasks
                (task_id, session_id, status, status_message, created_at, last_updated_at,
                 ttl_ms, expires_at_ticks, poll_interval_ms,
                 request_json, request_id_json, result_json, error_json, input_requests_json)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14);
            """u8;
    }

    struct UpdateStatement : IPreparedStatement<UpdateArgs> {
        public static StatementBindingResult Bind(in UpdateArgs args, PreparedStatement statement) =>
            new(statement) {
                args.TaskId, args.SessionId, args.Status, args.StatusMessage,
                args.LastUpdatedAt, args.TtlMs, args.ExpiresAtTicks, args.PollIntervalMs,
                args.RequestJson, args.RequestIdJson, args.ResultJson, args.ErrorJson,
                args.InputRequestsJson
            };

        public static ReadOnlySpan<byte> CommandText => """
            UPDATE mcp_tasks SET
                session_id          = $2,
                status              = $3,
                status_message      = $4,
                last_updated_at     = $5,
                ttl_ms              = $6,
                expires_at_ticks    = $7,
                poll_interval_ms    = $8,
                request_json        = $9,
                request_id_json     = $10,
                result_json         = $11,
                error_json          = $12,
                input_requests_json = $13
            WHERE task_id = $1;
            """u8;
    }

    struct DeleteExpiredStatement : IPreparedStatement<DeleteExpiredArgs> {
        public static StatementBindingResult Bind(in DeleteExpiredArgs args, PreparedStatement statement) =>
            new(statement) { args.NowTicks };

        public static ReadOnlySpan<byte> CommandText => """
            DELETE FROM mcp_tasks
            WHERE expires_at_ticks IS NOT NULL AND expires_at_ticks <= $1;
            """u8;
    }

    static TaskRow ReadRow(ref DataChunk.Row row) => new(
        row.ReadString(),
        row.TryReadString(),
        row.ReadString(),
        row.TryReadString(),
        row.ReadDateTime(),
        row.ReadDateTime(),
        row.TryReadInt64(),
        row.TryReadInt64(),
        row.TryReadInt64(),
        row.TryReadString(),
        row.TryReadString(),
        row.TryReadString(),
        row.TryReadString(),
        row.TryReadString()
    );

    #endregion
}
