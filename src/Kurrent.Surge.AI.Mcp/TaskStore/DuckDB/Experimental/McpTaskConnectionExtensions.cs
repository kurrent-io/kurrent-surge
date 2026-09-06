using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Kurrent.Quack;
using ModelContextProtocol;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;

namespace Kurrent.Surge.AI.Mcp.DuckDB.Experimental;

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
static partial class McpTaskConnectionExtensions {
    extension(DuckDBAdvancedConnection connection) {
        public void EnsureTaskSchema() =>
            connection.ExecuteNonQuery<EnsureTableStatement>();
    }
    
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
}

static partial class McpTaskConnectionExtensions {
    extension(DuckDBAdvancedConnection connection) {
        public long CreateTask(McpTaskInfo info) {
            var args = new CreateTaskArgs(
                info.TaskId,
                ToMcpTaskStatusArg(info.Status),
                info.CreatedAt.UtcDateTime,
                info.LastUpdatedAt.UtcDateTime,
                info.TimeToLive is { } ttl ? (long)ttl.TotalMilliseconds : null,
                info.PollIntervalMs);
            
            return connection.ExecuteNonQuery<CreateTaskArgs, CreateTaskStatement>(args);
            
            // Persist the SEP-2663 wire names the enum declares via [JsonStringEnumMemberName]
            // (working / input_required / completed / cancelled / failed).
            static string ToMcpTaskStatusArg(McpTaskStatus status) =>
                status switch {
                    McpTaskStatus.Working       => "working",
                    McpTaskStatus.InputRequired => "input_required",
                    McpTaskStatus.Completed     => "completed",
                    McpTaskStatus.Cancelled     => "cancelled",
                    McpTaskStatus.Failed        => "failed",
                    _                           => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown task status.")
                };
        }
    }
    
    struct CreateTaskStatement : IPreparedStatement<CreateTaskArgs> {
        public static StatementBindingResult Bind(in CreateTaskArgs args, PreparedStatement statement) =>
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
    
    readonly record struct CreateTaskArgs(
        string TaskId,
        string Status,
        DateTime CreatedAt,
        DateTime LastUpdatedAt,
        long? TtlMs,
        long? PollIntervalMs
    );
}

static partial class McpTaskConnectionExtensions {
    extension(DuckDBAdvancedConnection connection) {
        public McpTaskInfo? GetTask(string taskId) {
            var row = connection.QueryFirstOrDefault<TaskIdArgs, TaskRow, GetTaskStatement>(new(taskId));
            return row is { IsUndefined: false } ? row.Value.ToMcpTaskInfo(taskId) : null;
        }
    }
    
    struct GetTaskStatement : IQuery<TaskIdArgs, TaskRow> {
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
    
        public static TaskRow Parse(ref DataChunk.Row row) {
            
            
            return new(
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
    }
    
    readonly record struct TaskIdArgs(string TaskId);
}

static partial class McpTaskConnectionExtensions {
    extension(DuckDBAdvancedConnection connection) {
        public bool TaskExists(string taskId) {
            var row = connection.QueryFirstOrDefault<TaskIdArgs, bool, ExistsStatement>(new(taskId));
            return row is { IsUndefined: false, Value: true };
        }
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
}

static partial class McpTaskConnectionExtensions {
    extension(DuckDBAdvancedConnection connection) {
        public long SetCompleted(string taskId, JsonElement result, DateTimeOffset now) =>
            connection.ExecuteNonQuery<TransitionArgs, SetCompletedStatement>(
                TransitionArgs.CreateSetCompletedArgs(taskId, result, now));

        public long SetFailed(string taskId, JsonElement error, DateTimeOffset now) =>
            connection.ExecuteNonQuery<TransitionArgs, SetFailedStatement>(
                TransitionArgs.CreateSetFailedArgs(taskId, error, now));

        public long SetCancelled(string taskId, DateTimeOffset now) =>
            connection.ExecuteNonQuery<TransitionArgs, SetCancelledStatement>(
                TransitionArgs.CreateSetCancelledArgs(taskId, now));

        public long SetInputRequests(string taskId, IDictionary<string, InputRequest> inputRequests, DateTimeOffset now) =>
            connection.ExecuteNonQuery<TransitionArgs, SetInputRequestsStatement>(
                TransitionArgs.CreateSetInputRequestsArgs(taskId, inputRequests, now));

        public long ResolveInputRequests(string taskId, IEnumerable<string> requestIds, DateTimeOffset now) =>
            connection.ExecuteNonQuery<TransitionArgs, ResolveInputRequestsStatement>(
                TransitionArgs.CreateResolveInputRequestsArgs(taskId, requestIds, now));
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
    ) {
        static TransitionArgs Create(string taskId, McpTaskStatus targetStatus, string? body, DateTimeOffset now) {
            return new(
                taskId,
                ToMcpTaskStatusArg(targetStatus),
                body,
                now.UtcDateTime,
                ToMcpTaskStatusArg(McpTaskStatus.Completed),
                ToMcpTaskStatusArg(McpTaskStatus.Failed),
                ToMcpTaskStatusArg(McpTaskStatus.Cancelled));
            
            // Persist the SEP-2663 wire names the enum declares via [JsonStringEnumMemberName]
            // (working / input_required / completed / cancelled / failed).
            static string ToMcpTaskStatusArg(McpTaskStatus status) =>
                status switch {
                    McpTaskStatus.Working       => "working",
                    McpTaskStatus.InputRequired => "input_required",
                    McpTaskStatus.Completed     => "completed",
                    McpTaskStatus.Cancelled     => "cancelled",
                    McpTaskStatus.Failed        => "failed",
                    _                           => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown task status.")
                };
        }

        public static TransitionArgs CreateSetCompletedArgs(string taskId, JsonElement result, DateTimeOffset now) =>
            Create(
                taskId, McpTaskStatus.Completed, result.GetRawText(),
                now);

        public static TransitionArgs CreateSetFailedArgs(string taskId, JsonElement error, DateTimeOffset now) =>
            Create(
                taskId, McpTaskStatus.Failed, error.GetRawText(),
                now);

        public static TransitionArgs CreateSetCancelledArgs(string taskId, DateTimeOffset now) =>
            Create(
                taskId, McpTaskStatus.Cancelled, null,
                now);

        public static TransitionArgs CreateSetInputRequestsArgs(string taskId, IDictionary<string, InputRequest> inputRequests, DateTimeOffset now) =>
            Create(
                taskId, McpTaskStatus.InputRequired, JsonSerializer.Serialize(inputRequests, Mappings.InputRequestMapTypeInfo),
                now);

        public static TransitionArgs CreateResolveInputRequestsArgs(string taskId, IEnumerable<string> requestIds, DateTimeOffset now) {
            return Create(
                taskId, McpTaskStatus.Working, BuildNullPatch(requestIds),
                now);

            // A JSON merge-patch that deletes each key (RFC 7386: a null value removes the member).
            static string BuildNullPatch(IEnumerable<string> keys) {
                var patch = new JsonObject();

                foreach (var key in keys)
                    patch[key] = null;

                return patch.ToJsonString(McpJsonUtilities.DefaultOptions);
            }
        }
    }
}

static class Mappings {
    // Reuse the MCP SDK's own source-generated contract (it registers IDictionary<string, InputRequest>),
    // keeping serialization trim/AOT-safe via the typed JsonTypeInfo overloads.
    public static readonly JsonTypeInfo<IDictionary<string, InputRequest>> InputRequestMapTypeInfo =
        (JsonTypeInfo<IDictionary<string, InputRequest>>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, InputRequest>));
    
    public static McpTaskInfo ToMcpTaskInfo(this in TaskRow row, string taskId) {
        return new(
            taskId,
            row.Status.ToMcpTaskStatus(),
            new(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)),
            new(DateTime.SpecifyKind(row.LastUpdatedAt, DateTimeKind.Utc)),
            row.TtlMs is { } ttl ? TimeSpan.FromMilliseconds(ttl) : null,
            row.PollIntervalMs,
            row.StatusMessage,
            row.ResultJson is null ? null : JsonElement.Parse(row.ResultJson),
            row.ErrorJson is null ? null : JsonElement.Parse(row.ErrorJson),
            ParseInputRequests(row.InputRequestsJson));

        static IReadOnlyDictionary<string, InputRequest>? ParseInputRequests(string? json) {
            if (json is null) return null;
            var map = JsonSerializer.Deserialize(json, InputRequestMapTypeInfo);
            // An empty map (all requests resolved) reads back as "no pending requests".
            return map is not { Count: > 0 } ? null : new Dictionary<string, InputRequest>(map);
        }
    }

    public static McpTaskStatus ToMcpTaskStatus(this string value) =>
        value switch {
            "working"        => McpTaskStatus.Working,
            "input_required" => McpTaskStatus.InputRequired,
            "completed"      => McpTaskStatus.Completed,
            "cancelled"      => McpTaskStatus.Cancelled,
            "failed"         => McpTaskStatus.Failed,
            _                => throw new InvalidOperationException($"Unknown persisted task status '{value}'.")
        };
}

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

