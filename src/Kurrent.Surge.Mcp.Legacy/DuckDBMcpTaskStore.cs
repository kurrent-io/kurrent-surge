using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Kurrent.Surge.Mcp.Legacy;

/// <summary>
/// DuckDB-backed <see cref="IMcpTaskStore"/> for the stable ModelContextProtocol.Core 1.4.1
/// contract. Maps the 1.4.1 <see cref="McpTask"/> model — sessions, listing, and separate
/// result storage/retrieval — onto the version-neutral <see cref="DuckDBTaskStore"/> engine.
/// The originating <see cref="JsonRpcRequest"/> and <see cref="RequestId"/> are persisted as JSON
/// (via <see cref="McpJsonUtilities.DefaultOptions"/>) for durability; the interface never reads
/// them back, so they are written but not deserialized.
/// </summary>
/// <remarks>
/// Exception and session semantics mirror the reference in-memory store exactly, including the
/// asymmetry where a null caller session bypasses isolation on every method except
/// <see cref="GetTaskResultAsync"/> (which compares sessions unconditionally).
/// </remarks>
[PublicAPI]
public sealed class DuckDBMcpTaskStore : IMcpTaskStore, IDisposable {
    readonly DuckDBTaskStore _store;
    readonly bool            _ownsStore;

    /// <summary>Wraps an existing engine (caller owns its lifetime).</summary>
    public DuckDBMcpTaskStore(DuckDBTaskStore store) {
        _store     = store;
        _ownsStore = false;
    }

    /// <summary>Builds and owns an engine over the given connection string.</summary>
    public DuckDBMcpTaskStore(
        DuckDBConnectionString connectionString,
        DuckDBTaskStoreOptions? options = null,
        TimeProvider? timeProvider = null,
        ILogger<DuckDBTaskStore>? logger = null
    ) {
        _store     = new DuckDBTaskStore(connectionString, options, timeProvider, logger);
        _ownsStore = true;
    }

    /// <inheritdoc/>
    public async Task<McpTask> CreateTaskAsync(
        McpTaskMetadata taskParams,
        RequestId requestId,
        JsonRpcRequest request,
        string? sessionId = null,
        CancellationToken cancellationToken = default
    ) {
        var input = new CreateTaskInput {
            SessionId           = sessionId,
            RequestedTimeToLive = taskParams.TimeToLive,
            RequestJson         = JsonSerializer.Serialize(request, McpJsonUtilities.DefaultOptions),
            RequestIdJson       = JsonSerializer.Serialize(requestId, McpJsonUtilities.DefaultOptions)
        };

        var record = await _store.CreateAsync(input, cancellationToken).ConfigureAwait(false);
        return ToMcpTask(record);
    }

    /// <inheritdoc/>
    public async Task<McpTask?> GetTaskAsync(
        string taskId,
        string? sessionId = null,
        CancellationToken cancellationToken = default
    ) {
        var record = await _store.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (record is null)
            return null;

        // Null caller session bypasses isolation; a mismatch is indistinguishable from not-found.
        if (sessionId is not null && record.SessionId != sessionId)
            return null;

        return ToMcpTask(record);
    }

    /// <inheritdoc/>
    public async Task<McpTask> StoreTaskResultAsync(
        string taskId,
        McpTaskStatus status,
        JsonElement result,
        string? sessionId = null,
        CancellationToken cancellationToken = default
    ) {
        // Validated up front, before touching the store — matches the reference ordering.
        if (status is not (McpTaskStatus.Completed or McpTaskStatus.Failed))
            throw new ArgumentException(
                $"Status must be {nameof(McpTaskStatus.Completed)} or {nameof(McpTaskStatus.Failed)}.",
                nameof(status));

        var raw = result.GetRawText();
        var updated = await _store.UpdateAsync(
            taskId,
            entry => {
                ThrowIfSessionMismatch(entry, sessionId, taskId);

                if (entry.Status.IsTerminal())
                    throw new InvalidOperationException($"Cannot store result for task in terminal state: {entry.Status}");

                return entry with { Status = ToState(status), ResultJson = raw };
            },
            cancellationToken).ConfigureAwait(false);

        return ToMcpTask(RequireFound(updated, taskId));
    }

    /// <inheritdoc/>
    public async Task<JsonElement> GetTaskResultAsync(
        string taskId,
        string? sessionId = null,
        CancellationToken cancellationToken = default
    ) {
        var record = await _store.GetAsync(taskId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Task not found: {taskId}");

        // Unconditional equality — unlike every other method, a null caller session does NOT bypass.
        if (sessionId != record.SessionId)
            throw new InvalidOperationException($"Invalid sessionId: {sessionId} provided for {taskId}");

        if (record.ResultJson is null)
            throw new InvalidOperationException($"No result stored for task: {taskId}");

        return ParseJson(record.ResultJson);
    }

    /// <inheritdoc/>
    public async Task<McpTask> UpdateTaskStatusAsync(
        string taskId,
        McpTaskStatus status,
        string? statusMessage,
        string? sessionId = null,
        CancellationToken cancellationToken = default
    ) {
        var updated = await _store.UpdateAsync(
            taskId,
            entry => {
                ThrowIfSessionMismatch(entry, sessionId, taskId);
                // No transition guard — any status is accepted, including terminal → non-terminal.
                return entry with { Status = ToState(status), StatusMessage = statusMessage };
            },
            cancellationToken).ConfigureAwait(false);

        return ToMcpTask(RequireFound(updated, taskId));
    }

    /// <inheritdoc/>
    public async Task<ListTasksResult> ListTasksAsync(
        string? cursor = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default
    ) {
        var page = await _store.ListAsync(sessionId, cursor, cancellationToken).ConfigureAwait(false);

        var tasks = new McpTask[page.Tasks.Count];
        for (var i = 0; i < tasks.Length; i++)
            tasks[i] = ToMcpTask(page.Tasks[i]);

        return new ListTasksResult { Tasks = tasks, NextCursor = page.NextCursor };
    }

    /// <inheritdoc/>
    public async Task<McpTask> CancelTaskAsync(
        string taskId,
        string? sessionId = null,
        CancellationToken cancellationToken = default
    ) {
        var updated = await _store.UpdateAsync(
            taskId,
            entry => {
                ThrowIfSessionMismatch(entry, sessionId, taskId);
                // Idempotent: already-terminal tasks are returned unchanged (no throw, no downgrade).
                return entry.Status.IsTerminal() ? entry : entry with { Status = McpTaskState.Cancelled };
            },
            cancellationToken).ConfigureAwait(false);

        return ToMcpTask(RequireFound(updated, taskId));
    }

    public void Dispose() {
        if (_ownsStore)
            _store.Dispose();
    }

    static void ThrowIfSessionMismatch(TaskRecord record, string? sessionId, string taskId) {
        if (sessionId is not null && record.SessionId != sessionId)
            throw new InvalidOperationException($"Task not found: {taskId}");
    }

    static TaskRecord RequireFound(TaskRecord? record, string taskId) =>
        record ?? throw new InvalidOperationException($"Task not found: {taskId}");

    static McpTask ToMcpTask(TaskRecord r) => new() {
        TaskId        = r.TaskId,
        Status        = ToStatus(r.Status),
        StatusMessage = r.StatusMessage,
        CreatedAt     = r.CreatedAt,
        LastUpdatedAt = r.LastUpdatedAt,
        TimeToLive    = r.TimeToLive,
        PollInterval  = r.PollInterval
    };

    static McpTaskStatus ToStatus(McpTaskState state) => state switch {
        McpTaskState.Working       => McpTaskStatus.Working,
        McpTaskState.InputRequired => McpTaskStatus.InputRequired,
        McpTaskState.Completed     => McpTaskStatus.Completed,
        McpTaskState.Failed        => McpTaskStatus.Failed,
        McpTaskState.Cancelled     => McpTaskStatus.Cancelled,
        _                          => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown task state.")
    };

    static McpTaskState ToState(McpTaskStatus status) => status switch {
        McpTaskStatus.Working       => McpTaskState.Working,
        McpTaskStatus.InputRequired => McpTaskState.InputRequired,
        McpTaskStatus.Completed     => McpTaskState.Completed,
        McpTaskStatus.Failed        => McpTaskState.Failed,
        McpTaskStatus.Cancelled     => McpTaskState.Cancelled,
        _                           => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown task status.")
    };

    static JsonElement ParseJson(string json) {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
