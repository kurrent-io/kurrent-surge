using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DuckDB.NET.Data;
using DuckDB.NET.Native;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Polly;
using Polly.Retry;

namespace Kurrent.Surge.Mcp;

/// <summary>
/// A self-contained DuckDB-backed <see cref="IMcpTaskStore"/> (ModelContextProtocol.Core 2.0) built on
/// <b>Kurrent.Quack</b>, following its prescribed concurrency model: a single <b>dedicated write
/// connection</b> (not taken from the pool) with writes serialized by a <see cref="SemaphoreSlim"/>,
/// and <b>pooled connections for concurrent reads</b>.
/// </summary>
/// <remarks>
/// <para>
/// Kurrent.Quack's guidance is explicit: "every component that performs writes needs its own dedicated
/// connection created solely for that component (not taken from the pool)"; the pool is "for potentially
/// concurrent reads". DuckDB is single-writer/optimistic-MVCC, so concurrent writes to the same row
/// conflict; serializing writes through one connection means there is never more than one write
/// transaction at a time, so same-task writers converge with no conflict and no reliance on retry.
/// </para>
/// <para>
/// Register as a singleton. File-backed only — <see cref="DuckDBConnectionPool"/> rejects <c>:memory:</c>
/// data sources. The <see cref="InputResponseReceived"/> event is in-process.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class QuackMcpTaskStore : IMcpTaskStore, IDisposable {
    readonly DuckDBConnectionPool     _pool;       // concurrent reads
    readonly DuckDBAdvancedConnection _writer;     // dedicated, serialized writer (not from the pool)
    readonly SemaphoreSlim            _writeLock = new(1, 1);
    readonly DuckDBTaskStoreOptions   _options;
    readonly TimeProvider             _time;
    readonly ResiliencePipeline       _pipeline;

    /// <summary>
    /// Creates a store over the given DuckDB connection string (use a file source for durability).
    /// An optional <paramref name="pipelineBuilder"/> lets callers supply the full resilience pipeline
    /// (pass an empty builder to disable retry); when omitted, a default write-conflict retry is used —
    /// though with serialized writes it is only a belt-and-suspenders safety net.
    /// </summary>
    public QuackMcpTaskStore(
        string connectionString,
        DuckDBTaskStoreOptions? options = null,
        TimeProvider? timeProvider = null,
        ResiliencePipelineBuilder? pipelineBuilder = null
    ) {
        _pool    = new DuckDBConnectionPool(connectionString);
        _writer  = _pool.Open(); // a dedicated connection, deliberately NOT returned to the pool
        _options = options ?? new DuckDBTaskStoreOptions();
        _time    = timeProvider ?? TimeProvider.System;

        // A caller-supplied builder is used as-is — it fully owns the pipeline, including whether to
        // retry (pass an empty builder to disable retry entirely). When none is supplied we default
        // to the write-conflict retry (dormant while writes are serialized, but kept for safety).
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

    void EnsureSchema() =>
        _writer.ExecuteNonQuery<EnsureTableStatement>();

    // Writes: serialized on the dedicated connection (one writer at a time), through the pipeline.
    T ExecuteWrite<T>(Func<DuckDBAdvancedConnection, T> execute) =>
        _pipeline.Execute(() => {
            _writeLock.Wait();
            try {
                return execute(_writer);
            }
            finally {
                _writeLock.Release();
            }
        });

    // Reads: concurrent, each on its own pooled connection (returned to the pool on dispose).
    T ExecuteRead<T>(Func<DuckDBAdvancedConnection, T> execute) =>
        _pipeline.Execute(() => {
            using var scope = _pool.Rent(out var connection);
            return execute(connection);
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

        ExecuteWrite(connection => connection.ExecuteNonQuery<InsertArgs, InsertStatement>(ToInsertArgs(info)));
        return Task.FromResult(info);
    }

    /// <inheritdoc/>
    public Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) {
        var info = ExecuteRead(connection => {
            var row = connection.QueryFirstOrDefault<TaskIdArgs, TaskRow, SelectByIdStatement>(new(taskId));
            return row is { IsUndefined: false } ? FromRow(taskId, row.Value) : null;
        });

        return Task.FromResult(info);
    }

    /// <inheritdoc/>
    public Task SetCompletedAsync(string taskId, JsonElement result, CancellationToken cancellationToken = default) {
        var affected = ExecuteWrite(connection =>
            connection.ExecuteNonQuery<PayloadTransitionArgs, SetCompletedStatement>(
                PayloadArgs(taskId, McpTaskStatus.Completed, result.GetRawText())));

        // 0 rows => not found (throw) or already terminal (silent no-op).
        if (affected == 0)
            ThrowIfMissing(taskId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetFailedAsync(string taskId, JsonElement error, CancellationToken cancellationToken = default) {
        var affected = ExecuteWrite(connection =>
            connection.ExecuteNonQuery<PayloadTransitionArgs, SetFailedStatement>(
                PayloadArgs(taskId, McpTaskStatus.Failed, error.GetRawText())));

        if (affected == 0)
            ThrowIfMissing(taskId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> SetCancelledAsync(string taskId, CancellationToken cancellationToken = default) {
        var affected = ExecuteWrite(connection =>
            connection.ExecuteNonQuery<CancelTransitionArgs, SetCancelledStatement>(CancelArgs(taskId)));

        // true only if a live task actually transitioned; false for not-found and already-terminal.
        return Task.FromResult(affected == 1);
    }

    /// <inheritdoc/>
    public Task SetInputRequestsAsync(
        string taskId,
        IDictionary<string, InputRequest> inputRequests,
        CancellationToken cancellationToken = default
    ) {
        var affected = ExecuteWrite(connection =>
            connection.ExecuteNonQuery<PatchTransitionArgs, SetInputRequestsStatement>(
                PatchArgs(taskId, McpTaskStatus.InputRequired, SerializeInputRequests(inputRequests))));

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
        var affected = ExecuteWrite(connection =>
            connection.ExecuteNonQuery<PatchTransitionArgs, ResolveInputRequestsStatement>(
                PatchArgs(taskId, McpTaskStatus.Working, BuildNullPatch(inputResponses.Keys))));

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

    public void Dispose() {
        _writer.Dispose();
        _pool.Dispose();
        _writeLock.Dispose();
    }

    // Existence probe used only when a guarded UPDATE affected no rows, to tell "not found" (throw)
    // apart from "already terminal" (silent). A read, so it runs on a pooled connection.
    void ThrowIfMissing(string taskId) {
        var exists = ExecuteRead(connection => {
            var row = connection.QueryFirstOrDefault<TaskIdArgs, bool, ExistsStatement>(new(taskId));
            return row is { IsUndefined: false, Value: true };
        });

        if (!exists)
            throw new InvalidOperationException($"Task '{taskId}' not found.");
    }

    // Builds the arguments common to every transition: the target status, the timestamp, and the
    // terminal-status guard — all from the McpTaskStatus enum, so the SQL carries no status literals.
    PayloadTransitionArgs PayloadArgs(string taskId, McpTaskStatus targetStatus, string payload) => new(
        taskId, StatusToDb(targetStatus), payload, _time.GetUtcNow().UtcDateTime,
        StatusToDb(McpTaskStatus.Completed), StatusToDb(McpTaskStatus.Failed), StatusToDb(McpTaskStatus.Cancelled));

    CancelTransitionArgs CancelArgs(string taskId) => new(
        taskId, StatusToDb(McpTaskStatus.Cancelled), _time.GetUtcNow().UtcDateTime,
        StatusToDb(McpTaskStatus.Completed), StatusToDb(McpTaskStatus.Failed), StatusToDb(McpTaskStatus.Cancelled));

    PatchTransitionArgs PatchArgs(string taskId, McpTaskStatus targetStatus, string patch) => new(
        taskId, StatusToDb(targetStatus), patch, _time.GetUtcNow().UtcDateTime,
        StatusToDb(McpTaskStatus.Completed), StatusToDb(McpTaskStatus.Failed), StatusToDb(McpTaskStatus.Cancelled));

    #region ->> Mapping <<-

    static McpTaskInfo FromRow(string taskId, in TaskRow r) => new(
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

    static InsertArgs ToInsertArgs(McpTaskInfo info) => new(
        info.TaskId,
        StatusToDb(info.Status),
        info.CreatedAt.UtcDateTime,
        info.LastUpdatedAt.UtcDateTime,
        info.TimeToLive is { } ttl ? (long)ttl.TotalMilliseconds : null,
        info.PollIntervalMs);

    static DateTimeOffset ToUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

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
        var patch = new System.Text.Json.Nodes.JsonObject();

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

    #region ->> Rows, Args & Statements <<-

    // Raw column projection, read positionally in the SELECT-list order below (task_id is the lookup key).
    readonly record struct TaskRow(
        string   Status,
        string?  StatusMessage,
        DateTime CreatedAt,
        DateTime LastUpdatedAt,
        long?    TtlMs,
        long?    PollIntervalMs,
        string?  ResultJson,
        string?  ErrorJson,
        string?  InputRequestsJson
    );

    readonly record struct TaskIdArgs(string TaskId);

    readonly record struct InsertArgs(
        string   TaskId,
        string   Status,
        DateTime CreatedAt,
        DateTime LastUpdatedAt,
        long?    TtlMs,
        long?    PollIntervalMs
    );

    readonly record struct PayloadTransitionArgs(
        string   TaskId,
        string   Status,
        string   Payload,
        DateTime Now,
        string   TerminalCompleted,
        string   TerminalFailed,
        string   TerminalCancelled
    );

    readonly record struct CancelTransitionArgs(
        string   TaskId,
        string   Status,
        DateTime Now,
        string   TerminalCompleted,
        string   TerminalFailed,
        string   TerminalCancelled
    );

    readonly record struct PatchTransitionArgs(
        string   TaskId,
        string   Status,
        string   Patch,
        DateTime Now,
        string   TerminalCompleted,
        string   TerminalFailed,
        string   TerminalCancelled
    );

    struct EnsureTableStatement : IParameterlessStatement {
        public static ReadOnlySpan<byte> CommandText => """
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

        public static ReadOnlySpan<byte> CommandText => """
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

        public static TaskRow Parse(ref DataChunk.Row row) => new(
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

        public static ReadOnlySpan<byte> CommandText => """
            SELECT EXISTS (
                SELECT 1
                FROM mcp_tasks
                WHERE task_id = $1
            );
            """u8;

        public static bool Parse(ref DataChunk.Row row) => row.ReadBoolean();
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

        public static ReadOnlySpan<byte> CommandText => """
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

    struct SetCompletedStatement : IPreparedStatement<PayloadTransitionArgs> {
        public static StatementBindingResult Bind(in PayloadTransitionArgs args, PreparedStatement statement) =>
            new(statement) {
                args.TaskId, args.Status, args.Payload, args.Now,
                args.TerminalCompleted, args.TerminalFailed, args.TerminalCancelled
            };

        public static ReadOnlySpan<byte> CommandText => """
            UPDATE mcp_tasks
            SET status = $2,
                result_json = $3,
                last_updated_at = $4
            WHERE task_id = $1
              AND status NOT IN ($5, $6, $7);
            """u8;
    }

    struct SetFailedStatement : IPreparedStatement<PayloadTransitionArgs> {
        public static StatementBindingResult Bind(in PayloadTransitionArgs args, PreparedStatement statement) =>
            new(statement) {
                args.TaskId, args.Status, args.Payload, args.Now,
                args.TerminalCompleted, args.TerminalFailed, args.TerminalCancelled
            };

        public static ReadOnlySpan<byte> CommandText => """
            UPDATE mcp_tasks
            SET status = $2,
                error_json = $3,
                last_updated_at = $4
            WHERE task_id = $1
              AND status NOT IN ($5, $6, $7);
            """u8;
    }

    struct SetCancelledStatement : IPreparedStatement<CancelTransitionArgs> {
        public static StatementBindingResult Bind(in CancelTransitionArgs args, PreparedStatement statement) =>
            new(statement) {
                args.TaskId, args.Status, args.Now,
                args.TerminalCompleted, args.TerminalFailed, args.TerminalCancelled
            };

        public static ReadOnlySpan<byte> CommandText => """
            UPDATE mcp_tasks
            SET status = $2,
                last_updated_at = $3
            WHERE task_id = $1
              AND status NOT IN ($4, $5, $6);
            """u8;
    }

    struct SetInputRequestsStatement : IPreparedStatement<PatchTransitionArgs> {
        public static StatementBindingResult Bind(in PatchTransitionArgs args, PreparedStatement statement) =>
            new(statement) {
                args.TaskId, args.Status, args.Patch, args.Now,
                args.TerminalCompleted, args.TerminalFailed, args.TerminalCancelled
            };

        public static ReadOnlySpan<byte> CommandText => """
            UPDATE mcp_tasks
            SET input_requests_json = json_merge_patch(COALESCE(input_requests_json, '{}'), $3),
                status = $2,
                last_updated_at = $4
            WHERE task_id = $1
              AND status NOT IN ($5, $6, $7);
            """u8;
    }

    struct ResolveInputRequestsStatement : IPreparedStatement<PatchTransitionArgs> {
        public static StatementBindingResult Bind(in PatchTransitionArgs args, PreparedStatement statement) =>
            new(statement) {
                args.TaskId, args.Status, args.Patch, args.Now,
                args.TerminalCompleted, args.TerminalFailed, args.TerminalCancelled
            };

        public static ReadOnlySpan<byte> CommandText => """
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
