using System.Text.Json;
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
sealed class QuackMcpTaskStore : IMcpTaskStore, IDisposable {
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
    public QuackMcpTaskStore(
        DuckDBConnectionPool connectionPool,
        DuckDBTaskStoreOptions? options = null,
        TimeProvider? timeProvider = null,
        ResiliencePipelineBuilder? pipelineBuilder = null
    ) {
        _pool    = connectionPool;
        _writer  = _pool.Open(); // a dedicated connection, deliberately NOT returned to the pool
        _options = options      ?? new DuckDBTaskStoreOptions();
        _time    = timeProvider ?? TimeProvider.System;

        // A caller-supplied builder is used as-is — it fully owns the pipeline, including whether to
        // retry (pass an empty builder to disable retry entirely). When none is supplied we default
        // to the write-conflict retry (dormant while writes are serialized, but kept for safety).
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

        _writer.EnsureTaskSchema();
    }

    public QuackMcpTaskStore(
        string connectionString,
        DuckDBTaskStoreOptions? options = null,
        TimeProvider? timeProvider = null,
        ResiliencePipelineBuilder? pipelineBuilder = null
    ) : this(
        new DuckDBConnectionPool(connectionString), options, timeProvider,
        pipelineBuilder) { }

    // Writes: serialized on the dedicated connection (one writer at a time), through the pipeline.
    T ExecuteWrite<T>(Func<DuckDBAdvancedConnection, T> execute) =>
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

    /// <inheritdoc/>
    public Task<McpTaskInfo> CreateTaskAsync(CancellationToken cancellationToken = default) {
        var now = _time.GetUtcNow();

        var info = new McpTaskInfo(
            Guid.CreateVersion7(now).ToString(),
            McpTaskStatus.Working,
            now, now,
            _options.DefaultTimeToLive,
            _options.DefaultPollIntervalMs);

        ExecuteWrite(conn => conn.CreateTask(info));

        return Task.FromResult(info);
    }

    /// <inheritdoc/>
    public Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) {
        var info = ExecuteRead(conn => conn.GetTask(taskId));
        return Task.FromResult(info);
    }

    /// <inheritdoc/>
    public Task SetCompletedAsync(string taskId, JsonElement result, CancellationToken cancellationToken = default) {
        var affected = ExecuteWrite(conn => conn.SetCompleted(taskId, result, _time.GetUtcNow()));

        // 0 rows => not found (throw) or already terminal (silent no-op).
        if (affected == 0)
            ThrowIfMissing(taskId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetFailedAsync(string taskId, JsonElement error, CancellationToken cancellationToken = default) {
        var affected = ExecuteWrite(conn => conn.SetFailed(taskId, error, _time.GetUtcNow()));

        if (affected == 0)
            ThrowIfMissing(taskId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> SetCancelledAsync(string taskId, CancellationToken cancellationToken = default) {
        var affected = ExecuteWrite(conn => conn.SetCancelled(taskId, _time.GetUtcNow()));

        // true only if a live task actually transitioned; false for not-found and already-terminal.
        return Task.FromResult(affected == 1);
    }

    /// <inheritdoc/>
    public Task SetInputRequestsAsync(
        string taskId,
        IDictionary<string, InputRequest> inputRequests,
        CancellationToken cancellationToken = default
    ) {
        var affected = ExecuteWrite(conn => conn.SetInputRequests(taskId, inputRequests, _time.GetUtcNow()));

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
        var affected = ExecuteWrite(conn => conn.ResolveInputRequests(taskId, inputResponses.Keys, _time.GetUtcNow()));

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
        var exists = ExecuteRead(conn => conn.TaskExists(taskId));

        if (!exists)
            throw new InvalidOperationException($"Task '{taskId}' not found.");
    }
}
