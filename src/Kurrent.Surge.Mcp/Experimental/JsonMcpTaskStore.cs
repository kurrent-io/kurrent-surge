using System.Text.Json;
using System.Text.Json.Serialization;
using DuckDB.NET.Data;
using DuckDB.NET.Native;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Polly;
using Polly.Retry;

namespace Kurrent.Surge.Mcp;

/// <summary>
/// A DuckDB-backed <see cref="IMcpTaskStore"/> (ModelContextProtocol.Core 2.0) that persists each task as a
/// single JSON document — <c>(task_id, json)</c> — rather than a column per field. The whole
/// <see cref="McpTaskInfo"/> is (de)serialized through a source-generated context, and every transition is a
/// read-modify-write: load the document, mutate the record, store it back. Storing the serializer's exact
/// text by direct assignment (never rebuilt with DuckDB JSON functions) keeps <c>result</c>/<c>error</c>
/// payloads byte-identical across the round-trip.
/// </summary>
/// <remarks>
/// <para>
/// Concurrency is identical to <see cref="QuackMcpTaskStore"/>: one dedicated write connection serialized by
/// a <see cref="SemaphoreSlim"/>, pooled connections for reads. Because writes are serialized, the
/// read-modify-write cycle sees a stable document and never loses a concurrent merge — no SQL-level guard is
/// needed; the terminal-status fence and the found / terminal / applied decision are evaluated in memory.
/// Storage shape is the only variable versus the columnar stores.
/// </para>
/// <para>
/// Register as a singleton. File-backed only — <see cref="DuckDBConnectionPool"/> rejects <c>:memory:</c>
/// data sources. The <see cref="InputResponseReceived"/> event is in-process.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class JsonMcpTaskStore : IMcpTaskStore, IDisposable {
    readonly DuckDBConnectionPool     _pool;   // concurrent reads
    readonly DuckDBAdvancedConnection _writer; // dedicated, serialized writer (not from the pool)
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
    public JsonMcpTaskStore(
        DuckDBConnectionPool connectionPool,
        DuckDBTaskStoreOptions? options = null,
        TimeProvider? timeProvider = null,
        ResiliencePipelineBuilder? pipelineBuilder = null
    ) {
        _pool    = connectionPool;
        _writer  = _pool.Open(); // a dedicated connection, deliberately NOT returned to the pool
        _options = options      ?? new DuckDBTaskStoreOptions();
        _time    = timeProvider ?? TimeProvider.System;

        _pipeline = pipelineBuilder is not null
            ? pipelineBuilder.Build()
            : new ResiliencePipelineBuilder()
                .AddRetry(
                    new RetryStrategyOptions {
                        ShouldHandle     = args => ValueTask.FromResult(args.Outcome.Exception is DuckDBException { ErrorType: DuckDBErrorType.Transaction }),
                        MaxRetryAttempts = 10,
                        Delay            = TimeSpan.FromMilliseconds(10),
                        MaxDelay         = TimeSpan.FromMilliseconds(250),
                        BackoffType      = DelayBackoffType.Exponential,
                        UseJitter        = true
                    })
                .Build();

        _writer.ExecuteNonQuery<EnsureTableStatement>();
    }

    public JsonMcpTaskStore(
        string connectionString,
        DuckDBTaskStoreOptions? options = null,
        TimeProvider? timeProvider = null,
        ResiliencePipelineBuilder? pipelineBuilder = null
    ) : this(
        new DuckDBConnectionPool(connectionString), options, timeProvider,
        pipelineBuilder) { }

    /// <inheritdoc/>
    public Task<McpTaskInfo> CreateTaskAsync(CancellationToken cancellationToken = default) {
        var now = _time.GetUtcNow();

        var info = new McpTaskInfo(
            Guid.CreateVersion7(now).ToString(),
            McpTaskStatus.Working,
            now, now,
            _options.DefaultTimeToLive,
            _options.DefaultPollIntervalMs);

        var json = Serialize(info);
        WriteLocked(conn => conn.ExecuteNonQuery<TaskDocArgs, InsertStatement>(new(info.TaskId, json)));

        return Task.FromResult(info);
    }

    /// <inheritdoc/>
    public Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) {
        var info = ExecuteRead(conn => {
            var row = conn.QueryFirstOrDefault<TaskIdArgs, string, SelectByIdStatement>(new(taskId));
            return row is { IsUndefined: false } ? Deserialize(row.Value) : null;
        });

        return Task.FromResult(info);
    }

    /// <inheritdoc/>
    public Task SetCompletedAsync(string taskId, JsonElement result, CancellationToken cancellationToken = default) {
        // NotFound throws; Terminal is a silent no-op; Applied succeeded.
        if (Transition(taskId, info => info with { Status = McpTaskStatus.Completed, Result = result, LastUpdatedAt = _time.GetUtcNow() }) == TransitionOutcome.NotFound)
            throw NotFound(taskId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetFailedAsync(string taskId, JsonElement error, CancellationToken cancellationToken = default) {
        if (Transition(taskId, info => info with { Status = McpTaskStatus.Failed, Error = error, LastUpdatedAt = _time.GetUtcNow() }) == TransitionOutcome.NotFound)
            throw NotFound(taskId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> SetCancelledAsync(string taskId, CancellationToken cancellationToken = default) {
        // true only if a live task actually transitioned; false for not-found and already-terminal.
        var applied = Transition(taskId, info => info with { Status = McpTaskStatus.Cancelled, LastUpdatedAt = _time.GetUtcNow() }) == TransitionOutcome.Applied;

        return Task.FromResult(applied);
    }

    /// <inheritdoc/>
    public Task SetInputRequestsAsync(
        string taskId,
        IDictionary<string, InputRequest> inputRequests,
        CancellationToken cancellationToken = default
    ) {
        if (Transition(taskId, info => info with {
                Status         = McpTaskStatus.InputRequired,
                InputRequests  = Merge(info.InputRequests, inputRequests),
                LastUpdatedAt  = _time.GetUtcNow()
            }) == TransitionOutcome.NotFound)
            throw NotFound(taskId);

        return Task.CompletedTask;

        // Add the new requests to any already pending — concurrent SetInputRequests calls accumulate.
        static IReadOnlyDictionary<string, InputRequest> Merge(IReadOnlyDictionary<string, InputRequest>? pending, IDictionary<string, InputRequest> add) {
            var merged = pending is null ? new Dictionary<string, InputRequest>() : new Dictionary<string, InputRequest>(pending);

            foreach (var kvp in add)
                merged[kvp.Key] = kvp.Value;

            return merged;
        }
    }

    /// <inheritdoc/>
    public Task ResolveInputRequestsAsync(
        string taskId,
        IDictionary<string, InputResponse> inputResponses,
        CancellationToken cancellationToken = default
    ) {
        var outcome = Transition(taskId, info => Resolve(info, inputResponses.Keys, _time.GetUtcNow()));

        // NotFound throws; Terminal drops silently (no listener left); Applied notifies each response.
        if (outcome == TransitionOutcome.NotFound)
            throw NotFound(taskId);

        if (outcome == TransitionOutcome.Applied) {
            foreach (var kvp in inputResponses)
                InputResponseReceived?.Invoke(
                    new InputResponseReceivedEventArgs {
                        TaskId    = taskId,
                        RequestId = kvp.Key,
                        Response  = kvp.Value
                    });
        }

        return Task.CompletedTask;

        // Drop each satisfied request; return to working once none remain (empty map surfaces as null).
        static McpTaskInfo Resolve(McpTaskInfo info, IEnumerable<string> resolvedKeys, DateTimeOffset now) {
            var remaining = info.InputRequests is null
                ? new Dictionary<string, InputRequest>()
                : new Dictionary<string, InputRequest>(info.InputRequests);

            foreach (var key in resolvedKeys)
                remaining.Remove(key);

            return remaining.Count == 0
                ? info with { Status = McpTaskStatus.Working, InputRequests = null, LastUpdatedAt = now }
                : info with { InputRequests = remaining, LastUpdatedAt = now };
        }
    }

    /// <inheritdoc/>
    public event Action<InputResponseReceivedEventArgs>? InputResponseReceived;

    public void Dispose() {
        _writer.Dispose();
        _pool.Dispose();
        _writeLock.Dispose();
    }

    #region ->> Read-modify-write <<-

    enum TransitionOutcome { NotFound, Terminal, Applied }

    // Load → check terminal → mutate → store, all on the dedicated writer under the write lock, so the
    // cycle is atomic against other writers with no SQL guard. The read runs on the writer (not the pool)
    // to observe this store's own uncommitted-to-others writes in program order.
    TransitionOutcome Transition(string taskId, Func<McpTaskInfo, McpTaskInfo> mutate) =>
        WriteLocked(conn => {
            var row = conn.QueryFirstOrDefault<TaskIdArgs, string, SelectByIdStatement>(new(taskId));
            if (row is not { IsUndefined: false })
                return TransitionOutcome.NotFound;

            var info = Deserialize(row.Value);
            if (info.Status is McpTaskStatus.Completed or McpTaskStatus.Failed or McpTaskStatus.Cancelled)
                return TransitionOutcome.Terminal;

            conn.ExecuteNonQuery<TaskDocArgs, UpdateStatement>(new(taskId, Serialize(mutate(info))));
            return TransitionOutcome.Applied;
        });

    // Writes: serialized on the dedicated connection (one writer at a time), through the pipeline.
    T WriteLocked<T>(Func<DuckDBAdvancedConnection, T> execute) =>
        _pipeline.Execute(() => {
            _writeLock.Wait();

            try {
                return execute(_writer);
            } finally {
                _writeLock.Release();
            }
        });

    // Reads: concurrent, each on its own pooled connection (returned to the pool on dispose).
    T ExecuteRead<T>(Func<DuckDBAdvancedConnection, T> execute) =>
        _pipeline.Execute(() => {
            using var _ = _pool.Rent(out var connection);
            return execute(connection);
        });

    static InvalidOperationException NotFound(string taskId) =>
        new($"Task '{taskId}' not found.");

    #endregion

    #region ->> Serialization <<-

    static string Serialize(McpTaskInfo info) =>
        JsonSerializer.Serialize(info, JsonMcpTaskContext.Default.McpTaskInfo);

    static McpTaskInfo Deserialize(string json) {
        var info = JsonSerializer.Deserialize(json, JsonMcpTaskContext.Default.McpTaskInfo)!;
        // A fully-resolved request set can serialize back as an empty object; surface "no pending
        // requests" as null, matching the columnar store's read contract.
        return info.InputRequests is { Count: 0 } ? info with { InputRequests = null } : info;
    }

    #endregion

    #region ->> Args & Statements <<-

    readonly record struct TaskIdArgs(string TaskId);

    readonly record struct TaskDocArgs(string TaskId, string Json);

    struct EnsureTableStatement : IParameterlessStatement {
        public static ReadOnlySpan<byte> CommandText =>
            """
            CREATE TABLE IF NOT EXISTS mcp_tasks (
                task_id VARCHAR NOT NULL PRIMARY KEY,
                json    JSON    NOT NULL
            );
            """u8;
    }

    struct InsertStatement : IPreparedStatement<TaskDocArgs> {
        public static StatementBindingResult Bind(in TaskDocArgs args, PreparedStatement statement) =>
            new(statement) {
                args.TaskId,
                args.Json
            };

        public static ReadOnlySpan<byte> CommandText =>
            """
            INSERT INTO mcp_tasks (task_id, json) VALUES ($1, $2);
            """u8;
    }

    struct UpdateStatement : IPreparedStatement<TaskDocArgs> {
        public static StatementBindingResult Bind(in TaskDocArgs args, PreparedStatement statement) =>
            new(statement) {
                args.TaskId,
                args.Json
            };

        // Direct assignment of the serializer's exact text — never json_merge_patch — so payload escaping
        // survives the round-trip verbatim.
        public static ReadOnlySpan<byte> CommandText =>
            """
            UPDATE mcp_tasks SET json = $2 WHERE task_id = $1;
            """u8;
    }

    struct SelectByIdStatement : IQuery<TaskIdArgs, string> {
        public static StatementBindingResult Bind(in TaskIdArgs args, PreparedStatement statement) =>
            new(statement) { args.TaskId };

        public static ReadOnlySpan<byte> CommandText =>
            """
            SELECT CAST(json AS VARCHAR) FROM mcp_tasks WHERE task_id = $1;
            """u8;

        public static string Parse(ref DataChunk.Row row) =>
            row.ReadString();
    }

    #endregion
}

// Source-generated (AOT-safe) contract for the whole task document. The MCP SDK registers the input-request
// map but not McpTaskInfo itself, so the store owns this context. CamelCase + string enums reproduce the
// SDK's wire shape (status carries the SEP-2663 names).
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(McpTaskInfo))]
partial class JsonMcpTaskContext : JsonSerializerContext;
