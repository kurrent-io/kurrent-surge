using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Kurrent.Quack;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Kurrent.Surge.Mcp;

/// <summary>
/// Command extensions over a <see cref="DuckDBAdvancedConnection"/> that encapsulate the Kurrent.Quack
/// prepared-statement plumbing and the domain-to-storage mapping for the MCP task table, so
/// <see cref="QuackMcpTaskStore"/> reads as one-liners (<c>conn.CreateTask(info)</c>, <c>conn.GetTask(id)</c>).
/// </summary>
/// <remarks>
/// These members carry no connection lifecycle, write serialization, resilience, or affected-row policy —
/// that orchestration stays in <see cref="QuackMcpTaskStore"/>. Every transition takes the caller's
/// <c>now</c> so the clock stays owned by the store's <see cref="TimeProvider"/>.
/// </remarks>
static class McpTaskConnectionExtensions {
    #region ->> Commands <<-

    extension(DuckDBAdvancedConnection connection) {
        public void EnsureTaskSchema() =>
            connection.ExecuteNonQuery<EnsureTableStatement>();

        public long CreateTask(McpTaskInfo info) {
            return connection.ExecuteNonQuery<InsertArgs, InsertStatement>(ToInsertArgs(info));

            static InsertArgs ToInsertArgs(McpTaskInfo info) =>
                new(
                    info.TaskId,
                    StatusToDb(info.Status),
                    info.CreatedAt.UtcDateTime,
                    info.LastUpdatedAt.UtcDateTime,
                    info.TimeToLive is { } ttl ? (long)ttl.TotalMilliseconds : null,
                    info.PollIntervalMs);
        }

        public McpTaskInfo? GetTask(string taskId) {
            var row = connection.QueryFirstOrDefault<TaskIdArgs, TaskRow, SelectByIdStatement>(new(taskId));
            return row is { IsUndefined: false } ? FromRow(taskId, row.Value) : null;

            static McpTaskInfo FromRow(string taskId, in TaskRow r) =>
                new(
                    taskId,
                    StatusFromDb(r.Status),
                    ToUtc(r.CreatedAt),
                    ToUtc(r.LastUpdatedAt),
                    r.TtlMs is { } ttl ? TimeSpan.FromMilliseconds(ttl) : null,
                    r.PollIntervalMs,
                    r.StatusMessage,
                    ParseJson(r.ResultJson),
                    ParseJson(r.ErrorJson),
                    ParseInputRequests(r.InputRequestsJson));

            static McpTaskStatus StatusFromDb(string value) =>
                value switch {
                    "working"        => McpTaskStatus.Working,
                    "input_required" => McpTaskStatus.InputRequired,
                    "completed"      => McpTaskStatus.Completed,
                    "cancelled"      => McpTaskStatus.Cancelled,
                    "failed"         => McpTaskStatus.Failed,
                    _                => throw new InvalidOperationException($"Unknown persisted task status '{value}'.")
                };

            static DateTimeOffset ToUtc(DateTime value) =>
                new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

            static JsonElement? ParseJson(string? json) =>
                json is null ? null : JsonElement.Parse(json);

            static IReadOnlyDictionary<string, InputRequest>? ParseInputRequests(string? json) {
                if (json is null)
                    return null;

                var map = JsonSerializer.Deserialize(json, InputRequestMapTypeInfo);
                // An empty map (all requests resolved) reads back as "no pending requests".
                return map is not { Count: > 0 } ? null : new Dictionary<string, InputRequest>(map);
            }
        }

        public bool TaskExists(string taskId) {
            var row = connection.QueryFirstOrDefault<TaskIdArgs, bool, ExistsStatement>(new(taskId));
            return row is { IsUndefined: false, Value: true };
        }

        public long SetCompleted(string taskId, JsonElement result, DateTimeOffset now) =>
            connection.ExecuteNonQuery<TransitionArgs, SetCompletedStatement>(
                Transition(taskId, McpTaskStatus.Completed, result.GetRawText(), now));

        public long SetFailed(string taskId, JsonElement error, DateTimeOffset now) =>
            connection.ExecuteNonQuery<TransitionArgs, SetFailedStatement>(
                Transition(taskId, McpTaskStatus.Failed, error.GetRawText(), now));

        public long SetCancelled(string taskId, DateTimeOffset now) =>
            connection.ExecuteNonQuery<TransitionArgs, SetCancelledStatement>(
                Transition(taskId, McpTaskStatus.Cancelled, null, now));

        public long SetInputRequests(string taskId, IDictionary<string, InputRequest> inputRequests, DateTimeOffset now) {
            return connection.ExecuteNonQuery<TransitionArgs, SetInputRequestsStatement>(
                Transition(taskId, McpTaskStatus.InputRequired, SerializeInputRequests(inputRequests), now));

            static string SerializeInputRequests(IDictionary<string, InputRequest> requests) =>
                JsonSerializer.Serialize(requests, InputRequestMapTypeInfo);
        }

        public long ResolveInputRequests(string taskId, IEnumerable<string> requestIds, DateTimeOffset now) {
            return connection.ExecuteNonQuery<TransitionArgs, ResolveInputRequestsStatement>(
                Transition(taskId, McpTaskStatus.Working, BuildNullPatch(requestIds), now));

            // A JSON merge-patch that deletes each key (RFC 7386: a null value removes the member).
            static string BuildNullPatch(IEnumerable<string> keys) {
                var patch = new JsonObject();

                foreach (var key in keys)
                    patch[key] = null;

                return patch.ToJsonString();
            }
        }
    }

    #endregion

    #region ->> Mapping <<-

    // Builds the arguments shared by every status transition: the target status, an optional JSON body
    // (result / error / merge-patch — null for the bodyless cancel), the timestamp, and the terminal-status
    // guard, all from the McpTaskStatus enum so the SQL carries no status literals.
    static TransitionArgs Transition(string taskId, McpTaskStatus targetStatus, string? body, DateTimeOffset now) =>
        new(
            taskId, StatusToDb(targetStatus), body,
            now.UtcDateTime,
            StatusToDb(McpTaskStatus.Completed), StatusToDb(McpTaskStatus.Failed), StatusToDb(McpTaskStatus.Cancelled));

    // Persist the SEP-2663 wire names the enum declares via [JsonStringEnumMemberName]
    // (working / input_required / completed / cancelled / failed).
    static string StatusToDb(McpTaskStatus status) =>
        status switch {
            McpTaskStatus.Working       => "working",
            McpTaskStatus.InputRequired => "input_required",
            McpTaskStatus.Completed     => "completed",
            McpTaskStatus.Cancelled     => "cancelled",
            McpTaskStatus.Failed        => "failed",
            _                           => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown task status.")
        };

    // Reuse the MCP SDK's own source-generated contract (it registers IDictionary<string, InputRequest>),
    // keeping serialization trim/AOT-safe via the typed JsonTypeInfo overloads.
    static readonly JsonTypeInfo<IDictionary<string, InputRequest>> InputRequestMapTypeInfo =
        (JsonTypeInfo<IDictionary<string, InputRequest>>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, InputRequest>));

    #endregion

    #region ->> Rows, Args & Statements <<-

    // Raw column projection, read positionally in the SELECT-list order below (task_id is the lookup key).
    readonly record struct TaskRow(
        string Status,
        string? StatusMessage,
        DateTime CreatedAt,
        DateTime LastUpdatedAt,
        long? TtlMs,
        long? PollIntervalMs,
        string? ResultJson,
        string? ErrorJson,
        string? InputRequestsJson
    );

    readonly record struct TaskIdArgs(string TaskId);

    readonly record struct InsertArgs(
        string TaskId,
        string Status,
        DateTime CreatedAt,
        DateTime LastUpdatedAt,
        long? TtlMs,
        long? PollIntervalMs
    );

    // One shape for every status transition. Body carries the result / error / merge-patch JSON, or is
    // null for the bodyless cancel — whose statement simply does not bind it. The three terminal guards
    // feed the "status NOT IN (...)" clause that fences transitions off already-finished tasks.
    readonly record struct TransitionArgs(
        string TaskId,
        string Status,
        string? Body,
        DateTime Now,
        string TerminalCompleted,
        string TerminalFailed,
        string TerminalCancelled
    );

    struct EnsureTableStatement : IParameterlessStatement {
        public static ReadOnlySpan<byte> CommandText =>
            """
            CREATE TABLE IF NOT EXISTS mcp_tasks (
                task_id             VARCHAR   NOT NULL PRIMARY KEY,
                status              VARCHAR   NOT NULL,
                status_message      VARCHAR,
                created_at          TIMESTAMP NOT NULL,
                last_updated_at     TIMESTAMP NOT NULL,
                ttl_ms              BIGINT,
                poll_interval_ms    BIGINT,
                result_json         JSON,
                error_json          JSON,
                input_requests_json JSON
            );
            """u8;
    }

    struct SelectByIdStatement : IQuery<TaskIdArgs, TaskRow> {
        public static StatementBindingResult Bind(in TaskIdArgs args, PreparedStatement statement) =>
            new(statement) { args.TaskId };

        public static ReadOnlySpan<byte> CommandText =>
            """
            SELECT
                status,
                status_message,
                created_at,
                last_updated_at,
                ttl_ms,
                poll_interval_ms,
                CAST(result_json AS VARCHAR),
                CAST(error_json AS VARCHAR),
                CAST(input_requests_json AS VARCHAR)
            FROM mcp_tasks
            WHERE task_id = $1;
            """u8;

        public static TaskRow Parse(ref DataChunk.Row row) =>
            new(
                row.ReadString(),
                row.TryReadString(),
                row.ReadDateTime(),
                row.ReadDateTime(),
                row.TryReadInt64(),
                row.TryReadInt64(),
                row.TryReadString(),
                row.TryReadString(),
                row.TryReadString());
    }

    struct ExistsStatement : IQuery<TaskIdArgs, bool> {
        public static StatementBindingResult Bind(in TaskIdArgs args, PreparedStatement statement) =>
            new(statement) { args.TaskId };

        public static ReadOnlySpan<byte> CommandText =>
            """
            SELECT EXISTS (
                SELECT 1
                FROM mcp_tasks
                WHERE task_id = $1
            );
            """u8;

        public static bool Parse(ref DataChunk.Row row) =>
            row.ReadBoolean();
    }

    struct InsertStatement : IPreparedStatement<InsertArgs> {
        public static StatementBindingResult Bind(in InsertArgs args, PreparedStatement statement) =>
            new(statement) {
                args.TaskId,
                args.Status,
                args.CreatedAt,
                args.LastUpdatedAt,
                args.TtlMs,
                args.PollIntervalMs
            };

        public static ReadOnlySpan<byte> CommandText =>
            """
            INSERT INTO mcp_tasks (
                task_id,
                status,
                created_at,
                last_updated_at,
                ttl_ms,
                poll_interval_ms
            )
            VALUES ($1, $2, $3, $4, $5, $6);
            """u8;
    }

    struct SetCompletedStatement : IPreparedStatement<TransitionArgs> {
        public static StatementBindingResult Bind(in TransitionArgs args, PreparedStatement statement) =>
            new(statement) {
                args.TaskId,
                args.Status,
                args.Body,
                args.Now,
                args.TerminalCompleted,
                args.TerminalFailed,
                args.TerminalCancelled
            };

        public static ReadOnlySpan<byte> CommandText =>
            """
            UPDATE mcp_tasks
            SET status = $2,
                result_json = $3,
                last_updated_at = $4
            WHERE task_id = $1
              AND status NOT IN ($5, $6, $7);
            """u8;
    }

    struct SetFailedStatement : IPreparedStatement<TransitionArgs> {
        public static StatementBindingResult Bind(in TransitionArgs args, PreparedStatement statement) =>
            new(statement) {
                args.TaskId,
                args.Status,
                args.Body,
                args.Now,
                args.TerminalCompleted,
                args.TerminalFailed,
                args.TerminalCancelled
            };

        public static ReadOnlySpan<byte> CommandText =>
            """
            UPDATE mcp_tasks
            SET status = $2,
                error_json = $3,
                last_updated_at = $4
            WHERE task_id = $1
              AND status NOT IN ($5, $6, $7);
            """u8;
    }

    struct SetCancelledStatement : IPreparedStatement<TransitionArgs> {
        // The bodyless transition: Body is left unbound — the statement has no $3 payload, so Now is $3
        // and the terminal guards are $4..$6.
        public static StatementBindingResult Bind(in TransitionArgs args, PreparedStatement statement) =>
            new(statement) {
                args.TaskId,
                args.Status,
                args.Now,
                args.TerminalCompleted,
                args.TerminalFailed,
                args.TerminalCancelled
            };

        public static ReadOnlySpan<byte> CommandText =>
            """
            UPDATE mcp_tasks
            SET status = $2,
                last_updated_at = $3
            WHERE task_id = $1
              AND status NOT IN ($4, $5, $6);
            """u8;
    }

    struct SetInputRequestsStatement : IPreparedStatement<TransitionArgs> {
        public static StatementBindingResult Bind(in TransitionArgs args, PreparedStatement statement) =>
            new(statement) {
                args.TaskId,
                args.Status,
                args.Body,
                args.Now,
                args.TerminalCompleted,
                args.TerminalFailed,
                args.TerminalCancelled
            };

        public static ReadOnlySpan<byte> CommandText =>
            """
            UPDATE mcp_tasks
            SET input_requests_json = json_merge_patch(COALESCE(input_requests_json, '{}'), $3),
                status = $2,
                last_updated_at = $4
            WHERE task_id = $1
              AND status NOT IN ($5, $6, $7);
            """u8;
    }

    struct ResolveInputRequestsStatement : IPreparedStatement<TransitionArgs> {
        public static StatementBindingResult Bind(in TransitionArgs args, PreparedStatement statement) =>
            new(statement) {
                args.TaskId,
                args.Status,
                args.Body,
                args.Now,
                args.TerminalCompleted,
                args.TerminalFailed,
                args.TerminalCancelled
            };

        public static ReadOnlySpan<byte> CommandText =>
            """
            UPDATE mcp_tasks
            SET input_requests_json = json_merge_patch(COALESCE(input_requests_json, '{}'), $3),
                status = CASE
                    WHEN len(json_keys(json_merge_patch(COALESCE(input_requests_json, '{}'), $3))) = 0 THEN $2
                    ELSE status
                END,
                last_updated_at = $4
            WHERE task_id = $1
              AND status NOT IN ($5, $6, $7);
            """u8;
    }

    #endregion
}
