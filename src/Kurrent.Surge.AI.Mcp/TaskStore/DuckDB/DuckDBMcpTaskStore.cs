using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using DuckDB.NET.Data;
using DuckDB.NET.Native;
using Kurrent.Quack.ConnectionPool;
using ModelContextProtocol;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using Polly;
using Polly.Retry;

namespace Kurrent.Surge.AI.Mcp.DuckDB;

/// <summary>
/// A DuckDB-backed <see cref="IMcpTaskStore"/> (ModelContextProtocol.Core 2.0) that persists each task as a
/// single JSON document — <c>(task_id, json)</c>, with a denormalized <c>expires_at</c> column for the sweep —
/// and applies every transition as a server-side patch: a guarded single-statement
/// <c>UPDATE … json_merge_patch(…)</c> fenced on the document's own <c>$.status</c>, with no
/// read-modify-write. Connections come from a Kurrent.Quack <see cref="DuckDBConnectionPool"/> via the plain
/// ADO.NET command API.
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
/// <para>
/// <b>TTL sweep (SEP-2663).</b> A durable store — unlike the reference in-memory store, which drops
/// expired tasks on process restart — must expire and delete them itself, or rows accumulate forever. A
/// background timer built from the injected <see cref="TimeProvider"/> runs a two-phase sweep: expired live
/// tasks are marked <c>failed</c> (so a poll within <see cref="DuckDBMcpTaskStoreOptions.GracePeriod"/>
/// observes the failure), then deleted once the grace period also elapses. The spec sanctions both — a
/// server MAY mark a task failed after its TTL and delete it thereafter, and returning not-found for a
/// purged task is compliant. See <see cref="DuckDBMcpTaskStoreOptions.SweepInterval"/>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class DuckDBMcpTaskStore : IMcpTaskStore, IDisposable {
    readonly DuckDBConnectionPool      _pool;
    readonly DuckDBMcpTaskStoreOptions _options;
    readonly TimeProvider              _time;
    readonly ResiliencePipeline        _pipeline;
    readonly ITimer                    _sweepTimer;

    // Reentrancy gate for the sweep timer: a sweep slower than the interval must not pile up ticks.
    int _sweeping;

    /// <summary>
    /// Creates a store over the given DuckDB connection string (use a file data source for durability).
    /// </summary>
    /// <param name="connectionString">DuckDB connection string. File-backed only — <c>:memory:</c> is rejected.</param>
    /// <param name="options">Store options (poll interval, default TTL); defaults are used when <see langword="null"/>.</param>
    /// <param name="timeProvider">Clock for task timestamps; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <param name="pipelineBuilder">
    /// Resilience pipeline for the store's operations. When <see langword="null"/> a default write-conflict
    /// retry is used, so concurrent writers to the same task converge; pass an empty builder to disable retry.
    /// </param>
    public DuckDBMcpTaskStore(
        string connectionString,
        DuckDBMcpTaskStoreOptions? options = null,
        TimeProvider? timeProvider = null,
        ResiliencePipelineBuilder? pipelineBuilder = null
    ) {
        _options = options ?? new DuckDBMcpTaskStoreOptions();

        // Config is validated before any resource is allocated. These two invariants make the store
        // self-bounding: every task must have a positive lifetime, and the sweep that reclaims it cannot be
        // switched off (a non-positive interval would also spin up a runaway 0 ms timer).
        if (_options.DefaultTimeToLive <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                $"{nameof(options)}.{nameof(DuckDBMcpTaskStoreOptions.DefaultTimeToLive)}",
                _options.DefaultTimeToLive,
                "DefaultTimeToLive must be positive; a task born already expired would be failed by the next sweep.");

        if (_options.SweepInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                $"{nameof(options)}.{nameof(DuckDBMcpTaskStoreOptions.SweepInterval)}",
                _options.SweepInterval,
                "SweepInterval must be positive; the sweep cannot be disabled.");

        _pool = new(connectionString);
        _time = timeProvider ?? TimeProvider.System;

        _pipeline = pipelineBuilder is not null
            ? pipelineBuilder.Build()
            : Resilience.GetDefaultPipelineBuilder().Build();

        // expires_at (createdAt + ttl) is denormalized out of the JSON document so the sweep can compare it
        // directly: the ttl inside the JSON is a .NET TimeSpan string (e.g. "1.02:00:00") that DuckDB cannot
        // cast to an INTERVAL, whereas a TIMESTAMP column compares cleanly.
        Execute(command => {
            command.CommandText = """
            CREATE TABLE IF NOT EXISTS mcp_tasks (
                task_id    VARCHAR   NOT NULL PRIMARY KEY,
                json       JSON      NOT NULL,
                expires_at TIMESTAMP
            );
            """;
            return command.ExecuteNonQuery();
        });

        // Expire and delete tasks on a background timer built from the injected TimeProvider (so tests drive it
        // deterministically via a fake clock). The first tick is one interval out, after the schema above exists.
        _sweepTimer = _time.CreateTimer(OnSweepTick, null, _options.SweepInterval, _options.SweepInterval);
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

        // Every task gets a bounded expiry (the store never advertises unlimited TTL), so the sweep can
        // always reclaim it. This is the same instant the task advertises as its TTL: createdAt + ttl.
        var expiresAt = now.Add(_options.DefaultTimeToLive);

        const string sql = """
        INSERT INTO mcp_tasks (task_id, json, expires_at) VALUES ($task_id, $json, $expires_at);
        """;

        Execute(command => {
            command.CommandText = sql;
            Bind(command, "task_id", info.TaskId);
            Bind(command, "json", Serialize(info));
            Bind(command, "expires_at", expiresAt.UtcDateTime);
            return command.ExecuteNonQuery();
        });

        return Task.FromResult(info);
    }

    /// <inheritdoc/>
    public Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) {
        // result/error were stored as opaque JSON strings, so json_extract_string returns the caller's
        // exact raw text; re-parsing it yields a JsonElement whose GetRawText is byte-identical.
        const string sql = """
        SELECT 
            CAST(json AS VARCHAR), 
            json_extract_string(json, '$.result'), 
            json_extract_string(json, '$.error')
        FROM  mcp_tasks
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
        // transitions (true); terminal and missing both yield no row (false).
        const string sql = """
        UPDATE mcp_tasks
        SET json = json_merge_patch(json, $patch)
        WHERE task_id = $task_id
          AND json_extract_string(json, '$.status') NOT IN ('completed', 'failed', 'cancelled')
        RETURNING task_id;
        """;

        var transitioned = Execute(command => {
            command.CommandText = sql;
            Bind(command, "task_id", taskId);
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
        // Add inputRequests as a real object so json_merge_patch merge
        // s it recursively — concurrent calls accumulate keys.
        ApplyOrThrow(taskId, McpTaskStatus.InputRequired,
            patch => patch["inputRequests"] = JsonNode.Parse(SerializeInputRequests(inputRequests)));

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
        SET json = CASE WHEN json_extract_string(json, '$.status') NOT IN ('completed', 'failed', 'cancelled')
                        THEN json_merge_patch(
                                 json_merge_patch(json, json_object('inputRequests', CAST($deletes AS JSON), 'lastUpdatedAt', $now)),
                                 CASE WHEN len(json_keys(json_merge_patch(json, json_object('inputRequests', CAST($deletes AS JSON))), '$.inputRequests')) = 0
                                      THEN json_object('status', $working)
                                      ELSE '{}'::JSON END)
                        ELSE json END
        WHERE task_id = $task_id
        RETURNING json_extract_string(json, '$.status');
        """;

        // Two statements on one connection: snapshot the currently-outstanding request keys (the pre-image),
        // then apply the resolve. The pre-image only decides which advisory events to raise — SEP-2663: a
        // server SHOULD ignore responses whose key is not currently outstanding — while the durable state
        // change stays the single atomic UPDATE below. On a write-write conflict the pipeline re-runs the whole
        // lambda, re-reading a fresh pre-image, so read and update never drift apart on the committed attempt.
        // RETURNING carries the post-patch status (DuckDB can't bind a parameter inside RETURNING, so the
        // terminal check lives in C#); a null status => the task is missing.
        var (status, outstanding) = Execute<(string? Status, HashSet<string> Outstanding)>(command => {
            command.CommandText = "SELECT CAST(json_extract(json, '$.inputRequests') AS VARCHAR) FROM mcp_tasks WHERE task_id = $task_id;";
            Bind(command, "task_id", taskId);

            HashSet<string> pending;
            using (var reader = command.ExecuteReader())
                pending = reader.Read() && !reader.IsDBNull(0) ? OutstandingKeys(reader.GetString(0)) : [];

            command.Parameters.Clear();
            command.CommandText = sql;
            Bind(command, "task_id", taskId);
            Bind(command, "working", TaskStatusWire(McpTaskStatus.Working));
            Bind(command, "deletes", NullPatch(inputResponses.Keys));
            Bind(command, "now", NowWire());

            using (var reader = command.ExecuteReader())
                return (reader.Read() ? reader.GetString(0) : null, pending);
        });

        if (status is null)
            throw NotFound(taskId);

        // A live task ends non-terminal after resolve; a terminal one was left untouched. Only the former
        // notifies listeners.
        var resolvedLiveTask = status != TaskStatusWire(McpTaskStatus.Completed)
                            && status != TaskStatusWire(McpTaskStatus.Failed)
                            && status != TaskStatusWire(McpTaskStatus.Cancelled);

        // The resolve is already durably committed. Snapshot the delegate once (a subscriber may unsubscribe
        // mid-dispatch) and invoke each handler in isolation: a throwing subscriber must neither fail this
        // call — the caller would treat resolve as failed and retry, double-notifying — nor block the
        // remaining subscribers. A handler's failure is its own concern, so it is swallowed here.
        if (resolvedLiveTask && InputResponseReceived is { } handlers) {
            var subscribers = handlers.GetInvocationList();

            foreach (var kvp in inputResponses) {
                // SEP-2663: ignore a response whose key was not actually outstanding on this task.
                if (!outstanding.Contains(kvp.Key))
                    continue;

                var args = new InputResponseReceivedEventArgs {
                    TaskId    = taskId,
                    RequestId = kvp.Key,
                    Response  = kvp.Value
                };

                foreach (var subscriber in subscribers) {
                    try {
                        ((Action<InputResponseReceivedEventArgs>)subscriber)(args);
                    } catch {
                        // Intentionally isolated — see the note above. This type has no logger; surfacing
                        // handler failures (trace/log) would be a follow-up if observability is wanted.
                    }
                }
            }
        }

        return Task.CompletedTask;

        // The request keys currently outstanding on the task, parsed from the pre-image inputRequests object
        // (an absent or non-object value => no outstanding keys).
        static HashSet<string> OutstandingKeys(string inputRequestsJson) =>
            JsonNode.Parse(inputRequestsJson) is JsonObject requests
                ? requests.Select(entry => entry.Key).ToHashSet()
                : [];

        // A merge-patch fragment that deletes each satisfied request key (RFC 7386: a null value removes the
        // member). Keys are request ids; embedded via CAST(… AS JSON) as a real object.
        static string NullPatch(IEnumerable<string> keys) {
            var deletes = new JsonObject();

            foreach (var key in keys)
                deletes[key] = null;

            return deletes.ToJsonString();
        }
    }

    /// <inheritdoc/>
    public event Action<InputResponseReceivedEventArgs>? InputResponseReceived;

    // Stop the timer first so no new sweep starts, then release the pool. A sweep already in flight may
    // observe the disposed pool and throw; OnSweepTick swallows it.
    public void Dispose() {
        _sweepTimer.Dispose();
        _pool.Dispose();
    }

    // SEP-2663 permits marking a task failed once its TTL elapses. A JSON-RPC-shaped error with a
    // server-defined code (-32001) records why; stored opaquely (like SetFailedAsync) so GetTaskAsync
    // surfaces it verbatim.
    const string ExpiryErrorJson =
        """{"code":-32001,"message":"Task expired: its time-to-live elapsed before completion."}""";

    // Timer callback: skip if a prior sweep is still running, and never let a failure escape the timer.
    void OnSweepTick(object? state) {
        if (Interlocked.CompareExchange(ref _sweeping, 1, 0) != 0)
            return;

        try {
            SweepExpiredTasks();
        } catch {
            // Best-effort background cleanup: a failed sweep (pool disposed mid-tick at shutdown, or a
            // transient DuckDB error the pipeline exhausted) is swallowed and the next tick retries. This
            // type has no logger; surfacing sweep failures would be a follow-up if observability is wanted.
        } finally {
            Volatile.Write(ref _sweeping, 0);
        }
    }

    // Two-phase TTL sweep against one snapshot of the clock:
    //   1. mark expired live tasks failed, so a poll inside the grace window observes the failure;
    //   2. delete every task whose expiry is older than the grace period (terminal ones too).
    // Both statements are idempotent, so the resilience pipeline may safely retry the whole batch on a
    // write-write conflict. One pooled connection carries both phases.
    void SweepExpiredTasks() {
        var now         = _time.GetUtcNow();
        var purgeCutoff = now - _options.GracePeriod;

        // status + lastUpdatedAt like BuildPatch, plus the error stored as an opaque JSON string so
        // GetTaskAsync's json_extract_string('$.error') reads it back exactly.
        var failedPatch = new JsonObject {
            ["status"]        = TaskStatusWire(McpTaskStatus.Failed),
            ["lastUpdatedAt"] = now.ToString("O"),
            ["error"]         = JsonValue.Create(ExpiryErrorJson)
        }.ToJsonString();

        Execute(command => {
            command.CommandText = """
            UPDATE mcp_tasks
            SET json = json_merge_patch(json, $patch)
            WHERE expires_at IS NOT NULL
              AND expires_at <= $now
              AND json_extract_string(json, '$.status') NOT IN ('completed', 'failed', 'cancelled');
            """;
            Bind(command, "patch", failedPatch);
            Bind(command, "now", now.UtcDateTime);
            var marked = command.ExecuteNonQuery();

            command.Parameters.Clear();
            command.CommandText = """
            DELETE FROM mcp_tasks
            WHERE expires_at IS NOT NULL
              AND expires_at <= $cutoff;
            """;
            Bind(command, "cutoff", purgeCutoff.UtcDateTime);
            var deleted = command.ExecuteNonQuery();

            return marked + deleted;
        });
    }

    // complete / fail / set-input-requests: patch the row iff it is still live, leaving terminal rows
    // untouched. The guard lives in the SET's CASE, not the WHERE, so the row still matches on existence:
    // 0 rows back means the task is missing (throw); any row back means it exists (silent success — whether
    // we just transitioned it or it was already terminal). One round-trip, no separate existence probe.
    Task ApplyOrThrow(string taskId, McpTaskStatus status, Action<JsonObject> body) {
        const string sql = """
        UPDATE mcp_tasks
        SET json = CASE WHEN json_extract_string(json, '$.status') NOT IN ('completed', 'failed', 'cancelled')
                        THEN json_merge_patch(json, $patch)
                        ELSE json END
        WHERE task_id = $task_id
        RETURNING task_id;
        """;

        var exists = Execute(command => {
            command.CommandText = sql;
            Bind(command, "task_id", taskId);
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
        var patch = new JsonObject {
            ["status"] = TaskStatusWire(status), 
            ["lastUpdatedAt"] = NowWire()
        };
        body?.Invoke(patch);
        return patch;
    }

    static InvalidOperationException NotFound(string taskId) =>
        new($"Task '{taskId}' not found.");

    static void Bind(DbCommand command, string name, object? value) =>
        command.Parameters.Add(new DuckDBParameter(name, value ?? DBNull.Value));

    // ISO-8601 round-trippable text; System.Text.Json parses it back to DateTimeOffset on read.
    string NowWire() => _time.GetUtcNow().ToString("O");

    // The SEP-2663 wire names the enum declares via [JsonStringEnumMemberName]; these are exactly the
    // values the source-generated document carries in "$.status", so guard predicates match.
    static string TaskStatusWire(McpTaskStatus status) =>
        status switch {
            McpTaskStatus.Working       => "working",
            McpTaskStatus.InputRequired => "input_required",
            McpTaskStatus.Completed     => "completed",
            McpTaskStatus.Cancelled     => "cancelled",
            McpTaskStatus.Failed        => "failed",
            _                           => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown task status.")
        };

    static string Serialize(McpTaskInfo info) {
        return JsonSerializer.Serialize(info, Mcp.McpTaskStoreSerializerContext.Default.McpTaskInfo);
    }

    // Deserializes the structural fields; result/error are overridden by the caller from json_extract_string
    // (they are stored as opaque strings, so the value here is ignored).
    static McpTaskInfo Deserialize(string json) {
        var info = JsonSerializer.Deserialize(json, Mcp.McpTaskStoreSerializerContext.Default.McpTaskInfo)!;
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

static class Resilience {
    public static RetryStrategyOptions DefaultRetryOptions => new() {
        ShouldHandle     = args => ValueTask.FromResult(args.Outcome.Exception is DuckDBException { ErrorType: DuckDBErrorType.Transaction }),
        MaxRetryAttempts = 10,
        Delay            = TimeSpan.FromMilliseconds(10),
        MaxDelay         = TimeSpan.FromMilliseconds(250),
        BackoffType      = DelayBackoffType.Exponential,
        UseJitter        = true
    };
    
    public static ResiliencePipelineBuilder GetDefaultPipelineBuilder() =>
        new ResiliencePipelineBuilder()
            .AddRetry(DefaultRetryOptions);
}
