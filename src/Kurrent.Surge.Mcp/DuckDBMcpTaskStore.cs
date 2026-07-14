using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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
/// A DuckDB-backed <see cref="IMcpTaskStore"/> (ModelContextProtocol.Core 2.0) that persists each task as a
/// single JSON document — <c>(task_id, json)</c> — and applies every transition as a server-side patch: a
/// guarded single-statement <c>UPDATE … json_merge_patch(…)</c> fenced on the document's own
/// <c>$.status</c>, with no read-modify-write. Connections come from a Kurrent.Quack
/// <see cref="DuckDBConnectionPool"/> via the plain ADO.NET command API.
/// </summary>
/// <remarks>
/// <para>
/// Payloads round-trip byte-for-byte: <c>result</c> and <c>error</c> are stored as opaque JSON string
/// values — the caller's raw text is never re-parsed by DuckDB — and read back with
/// <c>json_extract_string</c>, the exact inverse of JSON string-escaping. <c>inputRequests</c> is a real
/// nested object so <c>json_merge_patch</c> merges concurrent additions.
/// </para>
/// <para>
/// No lock: each operation is one statement on a pooled connection, and DuckDB's optimistic write-write
/// conflicts are absorbed by the resilience pipeline's retry. Register as a singleton; file-backed only
/// (<see cref="DuckDBConnectionPool"/> rejects <c>:memory:</c> data sources). The
/// <see cref="InputResponseReceived"/> event is in-process.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class DuckDBMcpTaskStore : IMcpTaskStore, IDisposable {
    readonly DuckDBConnectionPool   _pool;
    readonly DuckDBTaskStoreOptions _options;
    readonly TimeProvider           _time;
    readonly ResiliencePipeline     _pipeline;

    public DuckDBMcpTaskStore(
        string connectionString,
        DuckDBTaskStoreOptions? options = null,
        TimeProvider? timeProvider = null,
        ResiliencePipelineBuilder? pipelineBuilder = null
    ) {
        _pool    = new DuckDBConnectionPool(connectionString);
        _options = options ?? new DuckDBTaskStoreOptions();
        _time    = timeProvider ?? TimeProvider.System;

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

        Execute(command => {
            command.CommandText = """
            CREATE TABLE IF NOT EXISTS mcp_tasks (
                task_id VARCHAR NOT NULL PRIMARY KEY,
                json    JSON    NOT NULL
            );
            """;
            return command.ExecuteNonQuery();
        });
    }

    // The one I/O primitive: rent a pooled connection, run a single command through the resilience
    // pipeline, return its result.
    T Execute<T>(Func<DbCommand, T> execute) =>
        _pipeline.Execute(() => {
            using var connection = _pool.Open();
            using var command    = connection.CreateCommand();
            return execute(command);
        });

    /// <inheritdoc/>
    public Task<McpTaskInfo> CreateTaskAsync(CancellationToken cancellationToken = default) {
        var now = _time.GetUtcNow();

        var info = new McpTaskInfo(
            Guid.CreateVersion7(now).ToString(),
            McpTaskStatus.Working,
            now, now,
            _options.DefaultTimeToLive,
            _options.DefaultPollIntervalMs);

        Execute(command => {
            command.CommandText = "INSERT INTO mcp_tasks (task_id, json) VALUES ($task_id, $json);";
            Bind(command, "task_id", info.TaskId);
            Bind(command, "json", Serialize(info));
            return command.ExecuteNonQuery();
        });

        return Task.FromResult(info);
    }

    /// <inheritdoc/>
    public Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) {
        // result/error were stored as opaque JSON strings, so json_extract_string returns the caller's
        // exact raw text; re-parsing it yields a JsonElement whose GetRawText is byte-identical.
        const string sql = """
        SELECT CAST(json AS VARCHAR), json_extract_string(json, '$.result'), json_extract_string(json, '$.error')
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
    public Task SetCompletedAsync(string taskId, JsonElement result, CancellationToken cancellationToken = default) =>
        ApplyOrThrow(taskId, McpTaskStatus.Completed, patch => patch["result"] = JsonValue.Create(result.GetRawText()));

    /// <inheritdoc/>
    public Task SetFailedAsync(string taskId, JsonElement error, CancellationToken cancellationToken = default) =>
        ApplyOrThrow(taskId, McpTaskStatus.Failed, patch => patch["error"] = JsonValue.Create(error.GetRawText()));

    /// <inheritdoc/>
    public Task<bool> SetCancelledAsync(string taskId, CancellationToken cancellationToken = default) {
        // Guarded in the WHERE (terminal-excluded): a row comes back only when a live task actually
        // transitions (true); terminal and missing both yield no row (false). No existence probe needed.
        const string sql = """
        UPDATE mcp_tasks
        SET json = json_merge_patch(json, $patch)
        WHERE task_id = $task_id
          AND json_extract_string(json, '$.status') NOT IN ($terminal_completed, $terminal_failed, $terminal_cancelled)
        RETURNING task_id;
        """;

        var transitioned = Execute(command => {
            command.CommandText = sql;
            BindGuard(command, taskId);
            Bind(command, "patch", BuildPatch(McpTaskStatus.Cancelled).ToJsonString());
            using var reader = command.ExecuteReader();
            return reader.Read();
        });

        return Task.FromResult(transitioned);
    }

    /// <inheritdoc/>
    public Task SetInputRequestsAsync(
        string taskId,
        IDictionary<string, InputRequest> inputRequests,
        CancellationToken cancellationToken = default
    ) =>
        // Add inputRequests as a real object so json_merge_patch merges it recursively — concurrent calls accumulate keys.
        ApplyOrThrow(taskId, McpTaskStatus.InputRequired, patch => patch["inputRequests"] = JsonNode.Parse(SerializeInputRequests(inputRequests)));

    /// <inheritdoc/>
    public Task ResolveInputRequestsAsync(
        string taskId,
        IDictionary<string, InputResponse> inputResponses,
        CancellationToken cancellationToken = default
    ) {
        // CASE-guarded (matched on existence) so one statement tells all three cases apart: 0 rows =>
        // missing (throw); RETURNING false => already terminal (silent); RETURNING true => a live task was
        // resolved (raise events). Deletes the satisfied keys and flips back to Working once none remain.
        const string sql = """
        UPDATE mcp_tasks
        SET json = CASE WHEN json_extract_string(json, '$.status') NOT IN ($terminal_completed, $terminal_failed, $terminal_cancelled)
                        THEN json_merge_patch(
                                 json_merge_patch(json, json_object('inputRequests', CAST($deletes AS JSON), 'lastUpdatedAt', $now)),
                                 CASE WHEN len(json_keys(json_merge_patch(json, json_object('inputRequests', CAST($deletes AS JSON))), '$.inputRequests')) = 0
                                      THEN json_object('status', $working)
                                      ELSE '{}'::JSON END)
                        ELSE json END
        WHERE task_id = $task_id
        RETURNING json_extract_string(json, '$.status');
        """;

        // RETURNING the post-patch status as a column expression — DuckDB can't prepare a bound parameter
        // inside a RETURNING clause, so the terminal check lives in C#. null => the task is missing.
        var status = Execute<string?>(command => {
            command.CommandText = sql;
            BindGuard(command, taskId);
            Bind(command, "working", StatusWire(McpTaskStatus.Working));
            Bind(command, "deletes", NullPatch(inputResponses.Keys));
            Bind(command, "now", Now());
            using var reader = command.ExecuteReader();
            return reader.Read() ? reader.GetString(0) : null;
        });

        if (status is null)
            throw NotFound(taskId);

        // A live task ends non-terminal after resolve; a terminal one was left untouched. Only the former
        // notifies listeners.
        var resolvedLiveTask = status != StatusWire(McpTaskStatus.Completed)
                            && status != StatusWire(McpTaskStatus.Failed)
                            && status != StatusWire(McpTaskStatus.Cancelled);

        if (resolvedLiveTask)
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

    // complete / fail / set-input-requests: patch the row iff it is still live, leaving terminal rows
    // untouched. The guard lives in the SET's CASE, not the WHERE, so the row still matches on existence:
    // 0 rows back means the task is missing (throw); any row back means it exists (silent success — whether
    // we just transitioned it or it was already terminal). One round-trip, no separate existence probe.
    Task ApplyOrThrow(string taskId, McpTaskStatus status, Action<JsonObject> body) {
        const string sql = """
        UPDATE mcp_tasks
        SET json = CASE WHEN json_extract_string(json, '$.status') NOT IN ($terminal_completed, $terminal_failed, $terminal_cancelled)
                        THEN json_merge_patch(json, $patch)
                        ELSE json END
        WHERE task_id = $task_id
        RETURNING task_id;
        """;

        var exists = Execute(command => {
            command.CommandText = sql;
            BindGuard(command, taskId);
            Bind(command, "patch", BuildPatch(status, body).ToJsonString());
            using var reader = command.ExecuteReader();
            return reader.Read();
        });

        if (!exists)
            throw NotFound(taskId);

        return Task.CompletedTask;
    }

    // The merge-patch for a transition: target status + update time, plus whatever the op adds. Built inside
    // the command lambda so a retried attempt re-reads the clock. result/error go in as JSON string values
    // (JsonValue.Create) — opaque, so DuckDB never re-serializes them and they round-trip byte-for-byte;
    // inputRequests goes in as a real object that json_merge_patch merges recursively.
    JsonObject BuildPatch(McpTaskStatus status, Action<JsonObject>? body = null) {
        var patch = new JsonObject { ["status"] = StatusWire(status), ["lastUpdatedAt"] = Now() };
        body?.Invoke(patch);
        return patch;
    }

    static InvalidOperationException NotFound(string taskId) =>
        new($"Task '{taskId}' not found.");

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
}

// Source-generated (AOT-safe) contract for the whole task document. The MCP SDK registers the input-request
// map but not McpTaskInfo itself, so this store owns the context. CamelCase + string enums reproduce the
// SDK's wire shape (status carries the SEP-2663 names).
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(McpTaskInfo))]
partial class JsonMcpTaskContext : JsonSerializerContext;
