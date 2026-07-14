using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using DuckDB.NET.Data;
using DuckDB.NET.Native;
using Kurrent.Quack.ConnectionPool;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Polly;
using Polly.Retry;

namespace Kurrent.Surge.Mcp;

/// <summary>
/// JSON-document task store that keeps every field in a single <c>(task_id, json)</c> row and performs each
/// transition as a <b>server-side patch operation</b> — a guarded single-statement
/// <c>UPDATE … SET json = json_merge_patch(json, …)</c> fenced on the document's own <c>$.status</c> — with
/// no read-modify-write. Connections come from a Kurrent.Quack <see cref="DuckDBConnectionPool"/>, but the
/// query path is plain ADO.NET (<see cref="DbCommand"/>), not the Kurrent.Quack prepared-statement extensions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Exact payload fidelity through a patch.</b> <c>result</c>/<c>error</c> are stored as opaque JSON
/// <i>string</i> values — the caller's raw text is bound as a plain string (never cast to <c>JSON</c>), so
/// <c>json_merge_patch</c> cannot re-serialize it — and read back with <c>json_extract_string</c>, the exact
/// inverse of JSON string-escaping. The payload therefore round-trips byte-for-byte. <c>inputRequests</c> is
/// kept as a real embedded object (the resolve logic needs <c>json_keys</c>/merge); its round-trip is
/// semantic, which is all that field requires.
/// </para>
/// <para>
/// No lock and no read-modify-write: each op is one statement on a pooled connection, and DuckDB's optimistic
/// write-write conflicts are retried. Register as a singleton; file-backed only. The
/// <see cref="InputResponseReceived"/> event is in-process.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class JsonMcpTaskStoreV2 : IMcpTaskStore, IDisposable {
    readonly DuckDBConnectionPool   _pool;
    readonly DuckDBTaskStoreOptions _options;
    readonly TimeProvider           _time;
    readonly ResiliencePipeline     _pipeline;

    /// <summary>
    /// Creates a store over the given DuckDB connection string (use a file source for durability).
    /// An optional <paramref name="pipelineBuilder"/> lets callers supply the full resilience pipeline
    /// (pass an empty builder to disable retry); when omitted, a default write-conflict retry is used.
    /// </summary>
    public JsonMcpTaskStoreV2(
        string connectionString,
        DuckDBTaskStoreOptions? options = null,
        TimeProvider? timeProvider = null,
        ResiliencePipelineBuilder? pipelineBuilder = null
    ) {
        _pool    = new DuckDBConnectionPool(connectionString);
        _options = options ?? new DuckDBTaskStoreOptions();
        _time    = timeProvider ?? TimeProvider.System;

        // DuckDB uses optimistic MVCC, not locks: when two writers race the same task row the loser's
        // commit fails with DuckDBErrorType.Transaction. Retrying the whole single-statement op re-reads
        // the winner's committed row and re-applies the guard/patch. An empty builder disables retry.
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

    // The one I/O primitive: rent a pooled connection, run a single command through the resilience
    // pipeline, return its result.
    T Execute<T>(Func<DbCommand, T> execute) =>
        _pipeline.Execute(() => {
            using var connection = _pool.Open();
            using var command    = connection.CreateCommand();
            return execute(command);
        });

    void EnsureSchema() {
        const string sql = """
        CREATE TABLE IF NOT EXISTS mcp_tasks (
            task_id VARCHAR NOT NULL PRIMARY KEY,
            json    JSON    NOT NULL
        );
        """;

        Execute(command => {
            command.CommandText = sql;
            return command.ExecuteNonQuery();
        });
    }

    /// <inheritdoc/>
    public Task<McpTaskInfo> CreateTaskAsync(CancellationToken cancellationToken = default) {
        const string sql = "INSERT INTO mcp_tasks (task_id, json) VALUES ($task_id, $json);";

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
            Bind(command, "json", Serialize(info));
            return command.ExecuteNonQuery();
        });

        return Task.FromResult(info);
    }

    /// <inheritdoc/>
    public Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) {
        // Pull the document for the structural fields, plus result/error as decoded strings. result/error
        // were stored as opaque JSON strings, so json_extract_string returns the caller's exact raw text;
        // re-parsing it yields a JsonElement whose GetRawText is byte-identical to what was passed in.
        const string sql = """
        SELECT
            CAST(json AS VARCHAR),
            json_extract_string(json, '$.result'),
            json_extract_string(json, '$.error')
        FROM mcp_tasks
        WHERE task_id = $task_id;
        """;

        var info = Execute<McpTaskInfo?>(command => {
            command.CommandText = sql;
            Bind(command, "task_id", taskId);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return null;

            return Deserialize(reader.GetString(0)) with {
                Result = reader.IsDBNull(1) ? null : JsonElement.Parse(reader.GetString(1)),
                Error  = reader.IsDBNull(2) ? null : JsonElement.Parse(reader.GetString(2))
            };
        });

        return Task.FromResult(info);
    }

    /// <inheritdoc/>
    public Task SetCompletedAsync(string taskId, JsonElement result, CancellationToken cancellationToken = default) {
        var affected = Execute(command => {
            command.CommandText = SetBodySql;
            BindGuard(command, taskId);
            Bind(command, "status", StatusWire(McpTaskStatus.Completed));
            Bind(command, "body_key", "result");
            Bind(command, "body", result.GetRawText()); // plain string => opaque JSON string, preserved verbatim
            Bind(command, "now", Now());
            return command.ExecuteNonQuery();
        });

        // 0 rows => not found (throw) or already terminal (silent no-op).
        if (affected == 0)
            ThrowIfMissing(taskId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetFailedAsync(string taskId, JsonElement error, CancellationToken cancellationToken = default) {
        var affected = Execute(command => {
            command.CommandText = SetBodySql;
            BindGuard(command, taskId);
            Bind(command, "status", StatusWire(McpTaskStatus.Failed));
            Bind(command, "body_key", "error");
            Bind(command, "body", error.GetRawText());
            Bind(command, "now", Now());
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
        SET json = json_merge_patch(json, json_object('status', $status, 'lastUpdatedAt', $now))
        WHERE task_id = $task_id
          AND json_extract_string(json, '$.status') NOT IN ($terminal_completed, $terminal_failed, $terminal_cancelled);
        """;

        var affected = Execute(command => {
            command.CommandText = sql;
            BindGuard(command, taskId);
            Bind(command, "status", StatusWire(McpTaskStatus.Cancelled));
            Bind(command, "now", Now());
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
        // inputRequests is embedded as a real object (CAST … AS JSON) so json_merge_patch merges it
        // recursively — concurrent calls accumulate their keys.
        const string sql = """
        UPDATE mcp_tasks
        SET json = json_merge_patch(json, json_object('status', $status, 'inputRequests', CAST($input_requests AS JSON), 'lastUpdatedAt', $now))
        WHERE task_id = $task_id
          AND json_extract_string(json, '$.status') NOT IN ($terminal_completed, $terminal_failed, $terminal_cancelled);
        """;

        var affected = Execute(command => {
            command.CommandText = sql;
            BindGuard(command, taskId);
            Bind(command, "status", StatusWire(McpTaskStatus.InputRequired));
            Bind(command, "input_requests", SerializeInputRequests(inputRequests));
            Bind(command, "now", Now());
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
        // Delete the satisfied keys via a null-patch on $.inputRequests, and drop back to Working once none
        // remain — all in one statement (the inner patch is evaluated twice: once to apply, once to test).
        const string sql = """
        UPDATE mcp_tasks
        SET json = json_merge_patch(
                json_merge_patch(json, json_object('inputRequests', CAST($deletes AS JSON), 'lastUpdatedAt', $now)),
                CASE WHEN len(json_keys(json_merge_patch(json, json_object('inputRequests', CAST($deletes AS JSON))), '$.inputRequests')) = 0
                     THEN json_object('status', $working)
                     ELSE '{}'::JSON END)
        WHERE task_id = $task_id
          AND json_extract_string(json, '$.status') NOT IN ($terminal_completed, $terminal_failed, $terminal_cancelled);
        """;

        var affected = Execute(command => {
            command.CommandText = sql;
            BindGuard(command, taskId);
            Bind(command, "working", StatusWire(McpTaskStatus.Working));
            Bind(command, "deletes", NullPatch(inputResponses.Keys));
            Bind(command, "now", Now());
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

    public void Dispose() => _pool.Dispose();

    // Shared by complete + fail: merge {status, <body_key>: <body>, lastUpdatedAt}. body is bound as a
    // plain string, so json_object stores it as an opaque JSON string — preserved byte-for-byte.
    const string SetBodySql =
        """
        UPDATE mcp_tasks
        SET json = json_merge_patch(json, json_object('status', $status, $body_key, $body, 'lastUpdatedAt', $now))
        WHERE task_id = $task_id
          AND json_extract_string(json, '$.status') NOT IN ($terminal_completed, $terminal_failed, $terminal_cancelled);
        """;

    // Existence probe used only when a guarded UPDATE affected no rows, to tell "not found" (throw)
    // apart from "already terminal" (silent).
    void ThrowIfMissing(string taskId) {
        const string sql = "SELECT EXISTS (SELECT 1 FROM mcp_tasks WHERE task_id = $task_id);";

        var exists = Execute(command => {
            command.CommandText = sql;
            Bind(command, "task_id", taskId);
            return Convert.ToBoolean(command.ExecuteScalar());
        });

        if (!exists)
            throw new InvalidOperationException($"Task '{taskId}' not found.");
    }

    #region ->> Binding & helpers <<-

    // Binds the terminal-status guard shared by every transition, all from the enum so the WHERE clause
    // carries no status literals.
    void BindGuard(DbCommand command, string taskId) {
        Bind(command, "task_id", taskId);
        Bind(command, "terminal_completed", StatusWire(McpTaskStatus.Completed));
        Bind(command, "terminal_failed",    StatusWire(McpTaskStatus.Failed));
        Bind(command, "terminal_cancelled", StatusWire(McpTaskStatus.Cancelled));
    }

    static void Bind(DbCommand command, string name, object? value) =>
        command.Parameters.Add(new DuckDBParameter(name, value ?? DBNull.Value));

    // ISO-8601 round-trippable text; System.Text.Json parses it back to DateTimeOffset on read.
    string Now() => _time.GetUtcNow().ToString("O");

    // A merge-patch fragment that deletes each satisfied request key (RFC 7386: a null value removes the
    // member). Keys are request ids; embedded via CAST(… AS JSON) as a real object.
    static string NullPatch(IEnumerable<string> keys) {
        var deletes = new JsonObject();

        foreach (var key in keys)
            deletes[key] = null;

        return deletes.ToJsonString();
    }

    // The SEP-2663 wire names the enum declares via [JsonStringEnumMemberName]; these are exactly the
    // values the source-generated document carries in "$.status", so guard predicates match.
    static string StatusWire(McpTaskStatus status) =>
        status switch {
            McpTaskStatus.Working       => "working",
            McpTaskStatus.InputRequired => "input_required",
            McpTaskStatus.Completed     => "completed",
            McpTaskStatus.Cancelled     => "cancelled",
            McpTaskStatus.Failed        => "failed",
            _                           => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown task status.")
        };

    #endregion

    #region ->> Serialization <<-

    static string Serialize(McpTaskInfo info) =>
        JsonSerializer.Serialize(info, JsonMcpTaskContext.Default.McpTaskInfo);

    // Deserializes the structural fields; result/error are overridden by the caller from json_extract_string
    // (they are stored as opaque strings, so the value here is ignored).
    static McpTaskInfo Deserialize(string json) {
        var info = JsonSerializer.Deserialize(json, JsonMcpTaskContext.Default.McpTaskInfo)!;
        // A fully-resolved request set can serialize back as an empty object; surface "no pending
        // requests" as null, matching the other stores' read contract.
        return info.InputRequests is { Count: 0 } ? info with { InputRequests = null } : info;
    }

    // Reuse the MCP SDK's own source-generated contract for the request map (trim/AOT-safe); it emits the
    // identical InputRequest shape that JsonMcpTaskContext reads back when the whole document is deserialized.
    static readonly JsonTypeInfo<IDictionary<string, InputRequest>> InputRequestMapTypeInfo =
        (JsonTypeInfo<IDictionary<string, InputRequest>>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, InputRequest>));

    static string SerializeInputRequests(IDictionary<string, InputRequest> requests) =>
        JsonSerializer.Serialize(requests, InputRequestMapTypeInfo);

    #endregion
}
