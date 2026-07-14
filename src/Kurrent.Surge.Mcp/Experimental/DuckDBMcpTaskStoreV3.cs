using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
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
/// A DuckDB-backed <see cref="IMcpTaskStore"/> (ModelContextProtocol.Core 2.0) that speaks the DuckDB
/// <b>Quack remote protocol</b> instead of touching the file directly. The store starts an embedded
/// Quack server over the given database in its constructor and stops it on disposal; every operation
/// opens a fresh client connection and forwards its SQL to that server via <c>quack_query</c>.
/// </summary>
/// <remarks>
/// <para>
/// Functionally identical to <see cref="DuckDBMcpTaskStore"/> — same guarded single-statement SQL,
/// status wire names, affected-row semantics, EXISTS probe, <c>json_merge_patch</c>, and Polly
/// conflict-retry. The difference is purely transport: statements run <b>server-side</b> through the
/// Quack protocol. That is also why writes work at all — <c>UPDATE</c>/<c>DELETE</c> against a
/// quack-<c>ATTACH</c>ed proxy fail ("can only update base table"), but <c>quack_query</c> executes
/// the statement on the server where the table is a real base table.
/// </para>
/// <para>
/// <c>quack_query</c> takes SQL as a string with no parameter binding, so values are inlined and
/// escaped. Quack is a beta DuckDB extension (stable at DuckDB v2.0), so this store is experimental.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class DuckDBMcpTaskStoreV3 : IMcpTaskStore, IDisposable {
    readonly DuckDBTaskStoreOptions _options;
    readonly TimeProvider           _time;
    readonly ResiliencePipeline     _pipeline;
    readonly DuckDBConnection       _server; // long-lived: owns the file and hosts the quack server
    readonly string                 _uri;    // quack:localhost:<port>
    readonly string                 _token;

    /// <summary>
    /// Creates a store over the given DuckDB connection string (use a file source for durability),
    /// starting an embedded Quack server over it. An optional <paramref name="pipelineBuilder"/> lets
    /// callers add their own resilience strategies; the conflict-retry strategy is always appended.
    /// </summary>
    public DuckDBMcpTaskStoreV3(
        string connectionString,
        DuckDBTaskStoreOptions? options = null,
        TimeProvider? timeProvider = null,
        ResiliencePipelineBuilder? pipelineBuilder = null
    ) {
        _options  = options ?? new DuckDBTaskStoreOptions();
        _time     = timeProvider ?? TimeProvider.System;
        _pipeline = BuildPipeline(pipelineBuilder);
        _uri      = $"quack:localhost:{FreeLoopbackPort()}";
        _token    = Guid.NewGuid().ToString("N");

        // The server connection owns the file. Create the schema on it directly (a real base table),
        // then start the quack server so client connections can forward statements to it.
        _server = new DuckDBConnection(connectionString);
        _server.Open();
        Exec(_server, "INSTALL quack;");
        Exec(_server, "LOAD quack;");
        Exec(_server, SchemaSql);
        Exec(_server, $"CALL quack_serve('{_uri}', token := '{_token}');");
    }

    const string SchemaSql = """
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
    
    const string SchemaSql2 = """
        CREATE TABLE IF NOT EXISTS mcp_tasks (
            task_id VARCHAR   NOT NULL PRIMARY KEY,
            json         JSON
        );
        """;

    /// <inheritdoc/>
    public Task<McpTaskInfo> CreateTaskAsync(CancellationToken cancellationToken = default) {
        var now = _time.GetUtcNow();

        var info = new McpTaskInfo(
            Guid.CreateVersion7(now).ToString(),
            McpTaskStatus.Working,
            now, now,
            _options.DefaultTimeToLive,
            _options.DefaultPollIntervalMs);

        var sql = $"""
            INSERT INTO mcp_tasks (task_id, status, created_at, last_updated_at, ttl_ms, poll_interval_ms)
            VALUES ({Lit(info.TaskId)}, {Lit(StatusToDb(info.Status))}, {Ts(info.CreatedAt)}, {Ts(info.LastUpdatedAt)}, {Num(ToMs(info.TimeToLive))}, {Num(info.PollIntervalMs)})
            RETURNING 1;
            """;

        ForwardCount(sql); // executes server-side; the inserted-row count is not needed
        return Task.FromResult(info);
    }

    /// <inheritdoc/>
    public Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) {
        var sql = $"""
            SELECT status, status_message, created_at, last_updated_at, ttl_ms, poll_interval_ms,
                   CAST(result_json AS VARCHAR), CAST(error_json AS VARCHAR), CAST(input_requests_json AS VARCHAR)
            FROM mcp_tasks
            WHERE task_id = {Lit(taskId)};
            """;

        return Task.FromResult(ForwardRead(sql, taskId));
    }

    /// <inheritdoc/>
    public Task SetCompletedAsync(string taskId, JsonElement result, CancellationToken cancellationToken = default) {
        var sql = $"""
            UPDATE mcp_tasks SET status = {Lit(StatusToDb(McpTaskStatus.Completed))}, result_json = {Lit(result.GetRawText())}, last_updated_at = {Ts(_time.GetUtcNow())}
            WHERE task_id = {Lit(taskId)} AND status NOT IN {TerminalGuard}
            RETURNING 1;
            """;

        // 0 rows => not found (throw) or already terminal (silent no-op).
        if (ForwardCount(sql) == 0)
            ThrowIfMissing(taskId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetFailedAsync(string taskId, JsonElement error, CancellationToken cancellationToken = default) {
        var sql = $"""
            UPDATE mcp_tasks SET status = {Lit(StatusToDb(McpTaskStatus.Failed))}, error_json = {Lit(error.GetRawText())}, last_updated_at = {Ts(_time.GetUtcNow())}
            WHERE task_id = {Lit(taskId)} AND status NOT IN {TerminalGuard}
            RETURNING 1;
            """;

        if (ForwardCount(sql) == 0)
            ThrowIfMissing(taskId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> SetCancelledAsync(string taskId, CancellationToken cancellationToken = default) {
        var sql = $"""
            UPDATE mcp_tasks SET status = {Lit(StatusToDb(McpTaskStatus.Cancelled))}, last_updated_at = {Ts(_time.GetUtcNow())}
            WHERE task_id = {Lit(taskId)} AND status NOT IN {TerminalGuard}
            RETURNING 1;
            """;

        // true only if a live task actually transitioned; false for not-found and already-terminal.
        return Task.FromResult(ForwardCount(sql) == 1);
    }

    /// <inheritdoc/>
    public Task SetInputRequestsAsync(
        string taskId,
        IDictionary<string, InputRequest> inputRequests,
        CancellationToken cancellationToken = default
    ) {
        var patch = Lit(SerializeInputRequests(inputRequests));
        var sql = $"""
            UPDATE mcp_tasks
            SET input_requests_json = json_merge_patch(COALESCE(input_requests_json, {EmptyObject}), {patch}),
                status = {Lit(StatusToDb(McpTaskStatus.InputRequired))},
                last_updated_at = {Ts(_time.GetUtcNow())}
            WHERE task_id = {Lit(taskId)} AND status NOT IN {TerminalGuard}
            RETURNING 1;
            """;

        if (ForwardCount(sql) == 0)
            ThrowIfMissing(taskId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ResolveInputRequestsAsync(
        string taskId,
        IDictionary<string, InputResponse> inputResponses,
        CancellationToken cancellationToken = default
    ) {
        // Delete the satisfied keys via a null-patch, and drop back to Working once none remain.
        var patch = Lit(BuildNullPatch(inputResponses.Keys));
        var sql = $"""
            UPDATE mcp_tasks
            SET input_requests_json = json_merge_patch(COALESCE(input_requests_json, {EmptyObject}), {patch}),
                status = CASE
                    WHEN len(json_keys(json_merge_patch(COALESCE(input_requests_json, {EmptyObject}), {patch}))) = 0 THEN {Lit(StatusToDb(McpTaskStatus.Working))}
                    ELSE status
                END,
                last_updated_at = {Ts(_time.GetUtcNow())}
            WHERE task_id = {Lit(taskId)} AND status NOT IN {TerminalGuard}
            RETURNING 1;
            """;

        if (ForwardCount(sql) == 0) {
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

    public void Dispose() {
        try {
            Exec(_server, $"CALL quack_stop('{_uri}');");
        }
        catch {
            // Best-effort shutdown — the server dies with the connection anyway.
        }

        _server.Dispose();
    }

    // Existence probe used only when a guarded UPDATE affected no rows, to tell "not found" (throw)
    // apart from "already terminal" (silent).
    void ThrowIfMissing(string taskId) {
        var sql = $"SELECT EXISTS(SELECT 1 FROM mcp_tasks WHERE task_id = {Lit(taskId)});";

        var exists = ForwardScalar(sql, static reader => Convert.ToBoolean(reader.GetValue(0)));
        if (!exists)
            throw new InvalidOperationException($"Task '{taskId}' not found.");
    }

    #region ->> Quack forwarding <<-

    // Terminal-status guard, sourced from the enum so the SQL carries no status literals.
    static readonly string TerminalGuard =
        $"({Lit(StatusToDb(McpTaskStatus.Completed))}, {Lit(StatusToDb(McpTaskStatus.Failed))}, {Lit(StatusToDb(McpTaskStatus.Cancelled))})";

    // Runs one statement server-side via quack_query and returns the number of result rows. For a
    // guarded UPDATE with RETURNING that is the affected-row count.
    long ForwardCount(string innerSql) =>
        OnClient(command => {
            command.CommandText = Wrap($"SELECT count(*) FROM quack_query('{_uri}', '{Escape(innerSql)}', token => '{_token}')");
            return Convert.ToInt64(command.ExecuteScalar());
        });

    // Runs a scalar-returning statement server-side and projects the first cell.
    T ForwardScalar<T>(string innerSql, Func<DbDataReader, T> project) =>
        OnClient(command => {
            command.CommandText = Wrap($"SELECT * FROM quack_query('{_uri}', '{Escape(innerSql)}', token => '{_token}')");
            using var reader = command.ExecuteReader();
            reader.Read();
            return project(reader);
        });

    // Runs a task SELECT server-side and materializes the first row (or null).
    McpTaskInfo? ForwardRead(string innerSql, string taskId) =>
        OnClient(command => {
            command.CommandText = Wrap($"SELECT * FROM quack_query('{_uri}', '{Escape(innerSql)}', token => '{_token}')");
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadInfo(reader, taskId) : null;
        });

    // Each call opens a fresh client connection to the quack server, through the resilience pipeline.
    T OnClient<T>(Func<DbCommand, T> execute) =>
        _pipeline.Execute(() => {
            using var connection = new DuckDBConnection("Data Source=:memory:");
            connection.Open();
            Exec(connection, "LOAD quack;");
            using var command = connection.CreateCommand();
            return execute(command);
        });

    static string Wrap(string sql) => sql + ";";

    // Interpolated (not written as a brace literal) so the SQL templates can stay single-'$' raw strings.
    const string EmptyObject = "'{}'";

    static McpTaskInfo ReadInfo(DbDataReader reader, string taskId) => new(
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

    #endregion

    #region ->> Helpers <<-

    static void Exec(DuckDBConnection connection, string sql) {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static int FreeLoopbackPort() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally {
            listener.Stop();
        }
    }

    // A SQL string literal: wrap in single quotes and double any embedded quote. quack_query has no
    // parameter binding, so every value is inlined; the whole statement is escaped once more by
    // Escape() when it becomes the string argument to quack_query.
    static string Lit(string value) => $"'{value.Replace("'", "''")}'";

    static string Escape(string sql) => sql.Replace("'", "''");

    static string Ts(DateTimeOffset value) =>
        $"TIMESTAMP '{value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture)}'";

    static string Num(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "NULL";

    static long? ToMs(TimeSpan? value) => value is { } v ? (long)v.TotalMilliseconds : null;

    static DateTimeOffset ToUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    static string? GetStringOrNull(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    // A caller-supplied builder is used as-is — it fully owns the pipeline, including whether to retry
    // (pass an empty builder to disable retry entirely). When none is supplied we default to the
    // write-conflict retry.
    static ResiliencePipeline BuildPipeline(ResiliencePipelineBuilder? builder) =>
        builder is not null
            ? builder.Build()
            : new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions {
                // A write-write conflict raised on the server is forwarded to the client. It may surface
                // as DuckDBErrorType.Transaction or as a wrapped error whose message carries the DuckDB
                // "Conflict on update" text, so match both.
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is DuckDBException dex &&
                    (dex.ErrorType == DuckDBErrorType.Transaction ||
                     dex.Message.Contains("Conflict", StringComparison.OrdinalIgnoreCase))),
                MaxRetryAttempts = 10,
                Delay            = TimeSpan.FromMilliseconds(10),
                MaxDelay         = TimeSpan.FromMilliseconds(250),
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true
            })
            .Build();

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

    #endregion
}
