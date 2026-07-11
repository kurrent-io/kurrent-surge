using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Kurrent.Surge.Mcp;

/// <summary>
/// DuckDB-backed <see cref="IMcpTaskStore"/> for ModelContextProtocol.Core 2.0. Maps the 2.0
/// <see cref="McpTaskInfo"/> task model onto the version-neutral <see cref="DuckDBTaskStore"/>
/// engine, serializing results, errors, and pending input requests as JSON via the SDK's own
/// <see cref="McpJsonUtilities.DefaultOptions"/>.
/// </summary>
/// <remarks>
/// The store must be registered as a singleton (see the DI extension) — the 2.0 contract requires
/// a single shared instance so <c>tasks/get</c> polls resolve across requests. The
/// <see cref="InputResponseReceived"/> event is in-process; a durable multi-instance deployment
/// still needs external coordination for cross-instance input notification.
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
    public async Task<McpTaskInfo> CreateTaskAsync(CancellationToken cancellationToken = default) {
        var record = await _store.CreateAsync(new CreateTaskInput(), cancellationToken).ConfigureAwait(false);
        return ToInfo(record);
    }

    /// <inheritdoc/>
    public async Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) {
        var record = await _store.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        return record is null ? null : ToInfo(record);
    }

    /// <inheritdoc/>
    public async Task SetCompletedAsync(string taskId, JsonElement result, CancellationToken cancellationToken = default) {
        var raw = result.GetRawText();
        var updated = await _store.UpdateAsync(
            taskId,
            entry => entry.Status.IsTerminal() ? entry : entry with { Status = McpTaskState.Completed, ResultJson = raw },
            cancellationToken).ConfigureAwait(false);

        ThrowIfNotFound(updated, taskId);
    }

    /// <inheritdoc/>
    public async Task SetFailedAsync(string taskId, JsonElement error, CancellationToken cancellationToken = default) {
        var raw = error.GetRawText();
        var updated = await _store.UpdateAsync(
            taskId,
            entry => entry.Status.IsTerminal() ? entry : entry with { Status = McpTaskState.Failed, ErrorJson = raw },
            cancellationToken).ConfigureAwait(false);

        ThrowIfNotFound(updated, taskId);
    }

    /// <inheritdoc/>
    public async Task<bool> SetCancelledAsync(string taskId, CancellationToken cancellationToken = default) {
        var transitioned = false;
        var updated = await _store.UpdateAsync(
            taskId,
            entry => {
                if (entry.Status.IsTerminal())
                    return entry;
                transitioned = true;
                return entry with { Status = McpTaskState.Cancelled };
            },
            cancellationToken).ConfigureAwait(false);

        // false for both "not found" (updated is null) and "already terminal" (no transition).
        return updated is not null && transitioned;
    }

    /// <inheritdoc/>
    public async Task SetInputRequestsAsync(
        string taskId,
        IDictionary<string, InputRequest> inputRequests,
        CancellationToken cancellationToken = default
    ) {
        var updated = await _store.UpdateAsync(
            taskId,
            entry => {
                if (entry.Status.IsTerminal())
                    return entry;

                var requests = DeserializeInputRequests(entry.InputRequestsJson);
                foreach (var kvp in inputRequests)
                    requests[kvp.Key] = kvp.Value;

                return entry with {
                    InputRequestsJson = SerializeInputRequests(requests),
                    Status            = McpTaskState.InputRequired
                };
            },
            cancellationToken).ConfigureAwait(false);

        ThrowIfNotFound(updated, taskId);
    }

    /// <inheritdoc/>
    public async Task ResolveInputRequestsAsync(
        string taskId,
        IDictionary<string, InputResponse> inputResponses,
        CancellationToken cancellationToken = default
    ) {
        var wasTerminal = false;
        var updated = await _store.UpdateAsync(
            taskId,
            entry => {
                if (entry.Status.IsTerminal()) {
                    wasTerminal = true;
                    return entry;
                }

                var requests = DeserializeInputRequests(entry.InputRequestsJson);
                foreach (var key in inputResponses.Keys)
                    requests.Remove(key);

                // Back to Working once every pending request has been satisfied.
                var status = requests.Count == 0 ? McpTaskState.Working : entry.Status;
                return entry with { InputRequestsJson = SerializeInputRequests(requests), Status = status };
            },
            cancellationToken).ConfigureAwait(false);

        ThrowIfNotFound(updated, taskId);

        if (wasTerminal)
            return; // drop responses targeting a terminal task — nothing can act on them

        foreach (var kvp in inputResponses)
            InputResponseReceived?.Invoke(new InputResponseReceivedEventArgs {
                TaskId    = taskId,
                RequestId = kvp.Key,
                Response  = kvp.Value
            });
    }

    /// <inheritdoc/>
    public event Action<InputResponseReceivedEventArgs>? InputResponseReceived;

    public void Dispose() {
        if (_ownsStore)
            _store.Dispose();
    }

    static void ThrowIfNotFound(TaskRecord? updated, string taskId) {
        if (updated is null)
            throw new InvalidOperationException($"Task '{taskId}' not found.");
    }

    static McpTaskInfo ToInfo(TaskRecord r) => new(
        r.TaskId,
        ToStatus(r.Status),
        r.CreatedAt,
        r.LastUpdatedAt,
        r.TimeToLive,
        r.PollInterval is { } poll ? (long?)poll.TotalMilliseconds : null,
        r.StatusMessage,
        ParseJson(r.ResultJson),
        ParseJson(r.ErrorJson),
        ParseInputRequests(r.InputRequestsJson));

    static McpTaskStatus ToStatus(McpTaskState state) => state switch {
        McpTaskState.Working       => McpTaskStatus.Working,
        McpTaskState.InputRequired => McpTaskStatus.InputRequired,
        McpTaskState.Completed     => McpTaskStatus.Completed,
        McpTaskState.Failed        => McpTaskStatus.Failed,
        McpTaskState.Cancelled     => McpTaskStatus.Cancelled,
        _                          => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown task state.")
    };

    static JsonElement? ParseJson(string? json) {
        if (json is null)
            return null;

        // Clone so the element survives disposal of the parsed document.
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    static Dictionary<string, InputRequest> DeserializeInputRequests(string? json) =>
        json is null
            ? new Dictionary<string, InputRequest>()
            : JsonSerializer.Deserialize<Dictionary<string, InputRequest>>(json, McpJsonUtilities.DefaultOptions)
              ?? new Dictionary<string, InputRequest>();

    static string? SerializeInputRequests(Dictionary<string, InputRequest> requests) =>
        requests.Count == 0
            ? null
            : JsonSerializer.Serialize(requests, McpJsonUtilities.DefaultOptions);

    static IReadOnlyDictionary<string, InputRequest>? ParseInputRequests(string? json) =>
        json is null
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, InputRequest>>(json, McpJsonUtilities.DefaultOptions);
}
