using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using DuckDB.NET.Data;
using DuckDB.NET.Native;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Polly;
using Polly.Retry;

namespace Kurrent.Surge.Mcp;

/// <summary>
/// A self-contained DuckDB-backed <see cref="IMcpTaskStore"/> for ModelContextProtocol.Core 2.0.
/// Persists <see cref="McpTaskInfo"/> to a DuckDB file so tasks survive process restarts.
/// </summary>
/// <remarks>
/// <para>
/// Register as a singleton (see the DI extension): the 2.0 contract requires a single shared
/// instance so <c>tasks/get</c> polls on later requests observe tasks created on earlier ones.
/// </para>
/// <para>
/// Every operation is a single SQL statement run through <see cref="Execute{T}"/>. Status transitions
/// carry their guard in the <c>WHERE</c> clause (<c>status NOT IN (terminal)</c>) and input-request maps
/// are merged in the database with <c>json_merge_patch</c>, so there is no read-modify-write and no lock;
/// DuckDB's optimistic write-write conflicts are retried instead. The <see cref="InputResponseReceived"/>
/// event is in-process.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class DuckDBMcpTaskStore : IMcpTaskStore, IDisposable {
    readonly string                 _connectionString;
    readonly DuckDBTaskStoreOptions _options;
    readonly TimeProvider           _time;
    readonly ResiliencePipeline     _pipeline;

    /// <summary>
    /// Creates a store over the given DuckDB connection string (use a file source for durability).
    /// An optional <paramref name="pipelineBuilder"/> lets callers add their own resilience strategies
    /// (timeouts, telemetry); the conflict-retry strategy is always appended.
    /// </summary>
    public DuckDBMcpTaskStore(
        string connectionString,
        DuckDBTaskStoreOptions? options = null,
        TimeProvider? timeProvider = null,
        ResiliencePipelineBuilder? pipelineBuilder = null
    ) {
        _connectionString = connectionString;
        _options          = options ?? new DuckDBTaskStoreOptions();
        _time             = timeProvider ?? TimeProvider.System;

        // DuckDB uses optimistic MVCC, not locks: when two writers race the same task row the loser's
        // commit fails with DuckDBErrorType.Transaction ("Conflict on update"). Retrying the whole
        // single-statement op on a fresh connection re-reads the winner's committed row and re-applies
        // the guard/merge, so concurrent same-task writers converge without a lock. Different tasks are
        // different rows and never conflict. Only that error is retried; everything else propagates.
        // A caller-supplied builder is used as-is — it fully owns the pipeline, including whether to
        // retry (pass an empty builder to disable retry entirely). When none is supplied we default
        // to the write-conflict retry.
        _pipeline = pipelineBuilder is not null
            ? pipelineBuilder.Build()
            : new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions {
                    ShouldHandle     = args => ValueTask.FromResult(
                        args.Outcome.Exception is DuckDBException { ErrorType: DuckDBErrorType.Transaction }),
                    MaxRetryAttempts = 10,
                    Delay            = TimeSpan.FromMilliseconds(10),
                    MaxDelay         = TimeSpan.FromMilliseconds(250),
                    BackoffType      = DelayBackoffType.Exponential,
                    UseJitter        = true
                })
                .Build();

        EnsureSchema();
    }

    // The one I/O primitive: open a fresh connection, run a single command through the resilience
    // pipeline, return its result. Every operation goes through here, so connection handling and
    // conflict retries live in exactly one place.
    T Execute<T>(Func<DbCommand, T> execute) =>
        _pipeline.Execute(() => {
            using var connection = new DuckDBConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            return execute(command);
        });

    void EnsureSchema() {
        const string sql = """
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
        """;

        Execute(command => {
            command.CommandText = sql;
            return command.ExecuteNonQuery();
        });
    }

    /// <inheritdoc/>
    public Task<McpTaskInfo> CreateTaskAsync(CancellationToken cancellationToken = default) {
        const string sql = """
        INSERT INTO mcp_tasks (
            task_id,
            status,
            created_at,
            last_updated_at,
            ttl_ms,
            poll_interval_ms
        )
        VALUES (
            $task_id,
            $status,
            $created_at,
            $last_updated_at,
            $ttl_ms,
            $poll_interval_ms
        );
        """;

        var now = _time.GetUtcNow();

        var info = new McpTaskInfo(
            Guid.CreateVersion7(now).ToString(),
            McpTaskStatus.Working,
            now, now,
            _options.DefaultTimeToLive,
            _options.DefaultPollIntervalMs);

        Execute(command => {
            command.CommandText = sql;
            Bind(command, "task_id", info.TaskId);
            Bind(command, "status", StatusToDb(info.Status));
            Bind(command, "created_at", info.CreatedAt.UtcDateTime);
            Bind(command, "last_updated_at", info.LastUpdatedAt.UtcDateTime);
            Bind(command, "ttl_ms", info.TimeToLive is { } ttl ? (long)ttl.TotalMilliseconds : null);
            Bind(command, "poll_interval_ms", info.PollIntervalMs);
            return command.ExecuteNonQuery();
        });

        return Task.FromResult(info);
    }

    /// <inheritdoc/>
    public Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) {
        const string sql = """
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
        WHERE task_id = $task_id;
        """;

        var info = Execute<McpTaskInfo?>(command => {
            command.CommandText = sql;
            Bind(command, "task_id", taskId);

            using var reader = command.ExecuteReader();

            if (!reader.Read())
                return null;

            return new McpTaskInfo(
                taskId,
                StatusFromDb(reader.GetString(0)),
                ToUtc(reader.GetDateTime(2)),
                ToUtc(reader.GetDateTime(3)),
                reader.IsDBNull(4) ? null : TimeSpan.FromMilliseconds(reader.GetInt64(4)),
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                ParseJson(GetStringOrNull(reader, 6)),
                ParseJson(GetStringOrNull(reader, 7)),
                ParseInputRequests(GetStringOrNull(reader, 8)));
        });

        return Task.FromResult(info);
    }

    /// <inheritdoc/>
    public Task SetCompletedAsync(string taskId, JsonElement result, CancellationToken cancellationToken = default) {
        const string sql = """
        UPDATE mcp_tasks
        SET status = $status,
            result_json = $payload,
            last_updated_at = $now
        WHERE task_id = $task_id
          AND status NOT IN ($terminal_completed, $terminal_failed, $terminal_cancelled);
        """;

        var affected = Execute(command => {
            command.CommandText = sql;
            BindTransition(command, taskId, McpTaskStatus.Completed);
            Bind(command, "payload", result.GetRawText());
            return command.ExecuteNonQuery();
        });

        // 0 rows => not found (throw) or already terminal (silent no-op).
        if (affected == 0)
            ThrowIfMissing(taskId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetFailedAsync(string taskId, JsonElement error, CancellationToken cancellationToken = default) {
        const string sql = """
        UPDATE mcp_tasks
        SET status = $status,
            error_json = $payload,
            last_updated_at = $now
        WHERE task_id = $task_id
          AND status NOT IN ($terminal_completed, $terminal_failed, $terminal_cancelled);
        """;

        var affected = Execute(command => {
            command.CommandText = sql;
            BindTransition(command, taskId, McpTaskStatus.Failed);
            Bind(command, "payload", error.GetRawText());
            return command.ExecuteNonQuery();
        });

        if (affected == 0)
            ThrowIfMissing(taskId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> SetCancelledAsync(string taskId, CancellationToken cancellationToken = default) {
        const string sql = """
        UPDATE mcp_tasks
        SET status = $status,
            last_updated_at = $now
        WHERE task_id = $task_id
          AND status NOT IN ($terminal_completed, $terminal_failed, $terminal_cancelled);
        """;

        var affected = Execute(command => {
            command.CommandText = sql;
            BindTransition(command, taskId, McpTaskStatus.Cancelled);
            return command.ExecuteNonQuery();
        });

        // true only if a live task actually transitioned; false for not-found and already-terminal.
        return Task.FromResult(affected == 1);
    }

    /// <inheritdoc/>
    public Task SetInputRequestsAsync(
        string taskId,
        IDictionary<string, InputRequest> inputRequests,
        CancellationToken cancellationToken = default
    ) {
        const string sql = """
        UPDATE mcp_tasks
        SET input_requests_json = json_merge_patch(COALESCE(input_requests_json, '{}'), $patch),
            status = $status,
            last_updated_at = $now
        WHERE task_id = $task_id
          AND status NOT IN ($terminal_completed, $terminal_failed, $terminal_cancelled);
        """;

        var affected = Execute(command => {
            command.CommandText = sql;
            BindTransition(command, taskId, McpTaskStatus.InputRequired);
            Bind(command, "patch", SerializeInputRequests(inputRequests));
            return command.ExecuteNonQuery();
        });

        if (affected == 0)
            ThrowIfMissing(taskId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ResolveInputRequestsAsync(
        string taskId,
        IDictionary<string, InputResponse> inputResponses,
        CancellationToken cancellationToken = default
    ) {
        // Delete the satisfied keys via a null-patch, and drop back to Working once none remain —
        // all in one statement.
        const string sql = """
        UPDATE mcp_tasks
        SET input_requests_json = json_merge_patch(COALESCE(input_requests_json, '{}'), $patch),
            status = CASE
                WHEN len(json_keys(json_merge_patch(COALESCE(input_requests_json, '{}'), $patch))) = 0 THEN $status
                ELSE status
            END,
            last_updated_at = $now
        WHERE task_id = $task_id
          AND status NOT IN ($terminal_completed, $terminal_failed, $terminal_cancelled);
        """;

        var affected = Execute(command => {
            command.CommandText = sql;
            BindTransition(command, taskId, McpTaskStatus.Working);
            Bind(command, "patch", BuildNullPatch(inputResponses.Keys));
            return command.ExecuteNonQuery();
        });

        if (affected == 0) {
            ThrowIfMissing(taskId); // throws if absent; returns silently if terminal
            return Task.CompletedTask;
        }

        // A live task was updated — notify listeners of each response.
        foreach (var kvp in inputResponses)
            InputResponseReceived?.Invoke(
                new InputResponseReceivedEventArgs {
                    TaskId    = taskId,
                    RequestId = kvp.Key,
                    Response  = kvp.Value
                });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public event Action<InputResponseReceivedEventArgs>? InputResponseReceived;

    // Connections are opened and closed per operation; nothing store-wide to release.
    public void Dispose() { }

    // Existence probe used only when a guarded UPDATE affected no rows, to tell "not found" (throw)
    // apart from "already terminal" (silent). DuckDB's RETURNING cannot expose the pre-update row
    // (no SQL-Server-style OUTPUT deleted.*), so a targeted EXISTS on the rare 0-row path is the
    // pragmatic distinction; the happy path stays a single statement.
    void ThrowIfMissing(string taskId) {
        const string sql = """
        SELECT EXISTS (
            SELECT 1
            FROM mcp_tasks
            WHERE task_id = $task_id
        );
        """;

        var exists = Execute(command => {
            command.CommandText = sql;
            Bind(command, "task_id", taskId);
            return Convert.ToBoolean(command.ExecuteScalar());
        });

        if (!exists)
            throw new InvalidOperationException($"Task '{taskId}' not found.");
    }

    // Binds the parameters common to every transition: the target status, the timestamp, and the
    // terminal-status guard — all from the McpTaskStatus enum, so the SQL carries no status literals.
    void BindTransition(DbCommand command, string taskId, McpTaskStatus targetStatus) {
        Bind(command, "task_id", taskId);
        Bind(command, "now", _time.GetUtcNow().UtcDateTime);
        Bind(command, "status", StatusToDb(targetStatus));
        Bind(command, "terminal_completed", StatusToDb(McpTaskStatus.Completed));
        Bind(command, "terminal_failed",    StatusToDb(McpTaskStatus.Failed));
        Bind(command, "terminal_cancelled", StatusToDb(McpTaskStatus.Cancelled));
    }

    static void Bind(DbCommand command, string name, object? value) =>
        command.Parameters.Add(new DuckDBParameter(name, value ?? DBNull.Value));

    static DateTimeOffset ToUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    static string? GetStringOrNull(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

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

    static McpTaskStatus StatusFromDb(string value) =>
        value switch {
            "working"        => McpTaskStatus.Working,
            "input_required" => McpTaskStatus.InputRequired,
            "completed"      => McpTaskStatus.Completed,
            "cancelled"      => McpTaskStatus.Cancelled,
            "failed"         => McpTaskStatus.Failed,
            _                => throw new InvalidOperationException($"Unknown persisted task status '{value}'.")
        };

    static JsonElement? ParseJson(string? json) =>
        json is null ? null : JsonElement.Parse(json);

    // A JSON merge-patch that deletes each key (RFC 7386: a null value removes the member).
    static string BuildNullPatch(IEnumerable<string> keys) {
        var patch = new JsonObject();

        foreach (var key in keys)
            patch[key] = null;

        return patch.ToJsonString();
    }

    // Reuse the MCP SDK's own source-generated contract (it registers IDictionary<string, InputRequest>),
    // keeping serialization trim/AOT-safe via the typed JsonTypeInfo overloads.
    static readonly JsonTypeInfo<IDictionary<string, InputRequest>> InputRequestMapTypeInfo =
        (JsonTypeInfo<IDictionary<string, InputRequest>>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, InputRequest>));

    static string SerializeInputRequests(IDictionary<string, InputRequest> requests) =>
        JsonSerializer.Serialize(requests, InputRequestMapTypeInfo);

    static IReadOnlyDictionary<string, InputRequest>? ParseInputRequests(string? json) {
        if (json is null)
            return null;

        var map = JsonSerializer.Deserialize(json, InputRequestMapTypeInfo);
        // An empty map (all requests resolved) reads back as "no pending requests".
        return map is not { Count: > 0 } ? null : new Dictionary<string, InputRequest>(map);
    }
}
