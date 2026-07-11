using System.Text;
using Kurrent.Quack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Surge.Mcp;

/// <summary>
/// Input for creating a task. All fields optional — the 2.0 contract supplies none of them,
/// the 1.4.1 contract supplies a session, a requested TTL, and the serialized originating request.
/// </summary>
[PublicAPI]
public sealed record CreateTaskInput {
    public string?    SessionId           { get; init; }
    public TimeSpan?  RequestedTimeToLive { get; init; }
    public string?    RequestJson         { get; init; }
    public string?    RequestIdJson       { get; init; }
}

/// <summary>One page of a <see cref="DuckDBTaskStore.ListAsync"/> call.</summary>
[PublicAPI]
public readonly record struct TaskListPage(IReadOnlyList<TaskRecord> Tasks, string? NextCursor);

/// <summary>
/// DuckDB-backed, version-neutral durable task store. Exposes primitive create/get/update/list
/// operations plus TTL-based expiry (live filtering on list/count, and a background sweep). The
/// MCP-facing adapters compose these primitives to satisfy their respective <c>IMcpTaskStore</c>
/// contracts; this engine holds no ModelContextProtocol types.
/// </summary>
/// <remarks>
/// <para>
/// DuckDB is a single-writer embedded database. Mutating operations (create/update/cleanup) are
/// serialized through an in-process semaphore, which gives clean read-modify-write atomicity and
/// reproduces the reference store's "exactly one writer wins" semantics without a lock-free CAS
/// loop. Reads run without the semaphore — DuckDB's MVCC provides a consistent snapshot.
/// </para>
/// <para>
/// Task ids are UUIDv7 (time-ordered). Listing is keyset-paginated over <c>(created_at, task_id)</c>
/// with an opaque composite cursor, so pagination stays correct regardless of id string collation.
/// </para>
/// </remarks>
[PublicAPI]
public sealed partial class DuckDBTaskStore : IDisposable {
    internal const string TableName = "mcp_tasks";

    readonly IDuckDBConnectionProvider _provider;
    readonly bool                      _ownsProvider;
    readonly DuckDBTaskStoreOptions    _options;
    readonly TimeProvider              _time;
    readonly ILogger<DuckDBTaskStore>  _logger;
    readonly SemaphoreSlim             _mutationLock = new(1, 1);
    readonly ITimer?                   _cleanupTimer;

    /// <summary>Creates a store over an existing connection provider (caller owns the provider).</summary>
    public DuckDBTaskStore(
        IDuckDBConnectionProvider provider,
        DuckDBTaskStoreOptions? options = null,
        TimeProvider? timeProvider = null,
        ILogger<DuckDBTaskStore>? logger = null
    ) : this(provider, ownsProvider: false, options, timeProvider, logger) { }

    /// <summary>Creates a store over a connection string; the store owns the connection pool it builds.</summary>
    public DuckDBTaskStore(
        DuckDBConnectionString connectionString,
        DuckDBTaskStoreOptions? options = null,
        TimeProvider? timeProvider = null,
        ILogger<DuckDBTaskStore>? logger = null
    ) : this(DuckDBConnectionProvider.Create(connectionString), ownsProvider: true, options, timeProvider, logger) { }

    DuckDBTaskStore(
        IDuckDBConnectionProvider provider,
        bool ownsProvider,
        DuckDBTaskStoreOptions? options,
        TimeProvider? timeProvider,
        ILogger<DuckDBTaskStore>? logger
    ) {
        _provider     = provider;
        _ownsProvider = ownsProvider;
        _options      = options ?? new DuckDBTaskStoreOptions();
        _options.Validate();
        _time   = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<DuckDBTaskStore>.Instance;

        EnsureSchema();

        // Timeout.InfiniteTimeSpan disables the sweep (expiry is still enforced live on list/count).
        if (_options.CleanupInterval != Timeout.InfiniteTimeSpan)
            _cleanupTimer = _time.CreateTimer(_ => Cleanup(), null, _options.CleanupInterval, _options.CleanupInterval);
    }

    void EnsureSchema() {
        using var connection = _provider.GetConnection();

        if (connection.TableExists(TableName))
            return;

        connection.ExecuteNonQuery<EnsureTableStatement>();

        if (!connection.TableExists(TableName))
            throw new InvalidOperationException($"Failed to create task table '{TableName}'.");
    }

    /// <summary>
    /// Creates a new task in <see cref="McpTaskState.Working"/>. Enforces the configured limits
    /// (global count includes not-yet-swept expired tasks; per-session count excludes expired and
    /// never applies to the null session), clamps the TTL, and stamps the id and timestamps.
    /// </summary>
    public async Task<TaskRecord> CreateAsync(CreateTaskInput input, CancellationToken cancellationToken = default) {
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            using var connection = _provider.GetConnection();

            if (_options.MaxTasks is { } maxTasks) {
                var total = connection.QueryFirstOrDefault<EmptyArgs, long, CountAllStatement>(default);
                var count = total is { IsUndefined: false } ? total.Value : 0;
                if (count >= maxTasks)
                    throw new InvalidOperationException(
                        $"Maximum number of tasks ({maxTasks}) has been reached. Cannot create new task.");
            }

            if (_options.MaxTasksPerSession is { } maxPerSession && input.SessionId is { } sessionId) {
                var nowTicks = _time.GetUtcNow().UtcTicks;
                var active   = connection.QueryFirstOrDefault<SessionCountArgs, long, CountActiveInSessionStatement>(new(sessionId, nowTicks));
                var count    = active is { IsUndefined: false } ? active.Value : 0;
                if (count >= maxPerSession)
                    throw new InvalidOperationException(
                        $"Maximum number of tasks per session ({maxPerSession}) has been reached for session '{sessionId}'. Cannot create new task.");
            }

            var now = _time.GetUtcNow();
            var ttl = ResolveTimeToLive(input.RequestedTimeToLive);

            var record = new TaskRecord {
                TaskId        = Guid.CreateVersion7(now).ToString(),
                SessionId     = input.SessionId,
                Status        = McpTaskState.Working,
                StatusMessage = null,
                CreatedAt     = now,
                LastUpdatedAt = now,
                TimeToLive    = ttl,
                ExpiresAt     = ttl is { } t ? now + t : null,
                PollInterval  = _options.PollInterval,
                RequestJson   = input.RequestJson,
                RequestIdJson = input.RequestIdJson
            };

            connection.ExecuteNonQuery<InsertArgs, InsertStatement>(ToInsertArgs(record));
            return record;
        }
        finally {
            _mutationLock.Release();
        }
    }

    /// <summary>
    /// Fetches a task by id, or <see langword="null"/> if absent. Does not filter by expiry —
    /// an expired-but-not-yet-swept task is still returned (matching the reference direct-get path);
    /// session access control is the adapter's responsibility.
    /// </summary>
    public Task<TaskRecord?> GetAsync(string taskId, CancellationToken cancellationToken = default) {
        using var connection = _provider.GetConnection();

        var row = connection.QueryFirstOrDefault<TaskIdArgs, TaskRow, SelectByIdStatement>(new(taskId));
        return Task.FromResult(row is { IsUndefined: false } ? FromRow(row.Value) : null);
    }

    /// <summary>
    /// Atomically read-modify-writes a task. Returns <see langword="null"/> if the task does not
    /// exist. The <paramref name="transform"/> runs under the mutation lock inside a transaction and
    /// may throw to abort. If it returns an unchanged record the store performs no write and leaves
    /// <see cref="TaskRecord.LastUpdatedAt"/> untouched (this is how idempotent no-ops are expressed).
    /// Otherwise <c>LastUpdatedAt</c> is stamped and the row rewritten; <c>TaskId</c>/<c>CreatedAt</c>
    /// are always preserved regardless of the transform.
    /// </summary>
    public async Task<TaskRecord?> UpdateAsync(
        string taskId,
        Func<TaskRecord, TaskRecord> transform,
        CancellationToken cancellationToken = default
    ) {
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            using var connection = _provider.GetConnection();
            using var tx         = connection.BeginTransaction();

            var row = connection.QueryFirstOrDefault<TaskIdArgs, TaskRow, SelectByIdStatement>(new(taskId));
            if (row is not { IsUndefined: false })
                return null;

            var current     = FromRow(row.Value);
            var transformed = transform(current);

            // Unchanged transform → no write, no timestamp bump (idempotent no-op).
            if (transformed == current)
                return current;

            var updated = transformed with {
                TaskId        = current.TaskId,
                CreatedAt     = current.CreatedAt,
                LastUpdatedAt = _time.GetUtcNow()
            };

            connection.ExecuteNonQuery<UpdateArgs, UpdateStatement>(ToUpdateArgs(updated));
            tx.CommitOnDispose();
            return updated;
        }
        finally {
            _mutationLock.Release();
        }
    }

    /// <summary>
    /// Lists tasks ordered by <c>(created_at, task_id)</c>, filtered by session (null lists every
    /// session) and excluding expired tasks. Returns up to <see cref="DuckDBTaskStoreOptions.PageSize"/>
    /// tasks plus an opaque cursor for the next page (null on the last page). A malformed cursor is
    /// treated as "from the beginning" rather than throwing.
    /// </summary>
    public Task<TaskListPage> ListAsync(
        string? sessionId = null,
        string? cursor = null,
        CancellationToken cancellationToken = default
    ) {
        using var connection = _provider.GetConnection();

        var nowTicks = _time.GetUtcNow().UtcTicks;
        var decoded  = DecodeCursor(cursor);
        var args = new ListArgs(
            SessionId:       sessionId,
            SessionIsNull:   sessionId is null ? 1 : 0,
            NowTicks:        nowTicks,
            CursorCreatedAt: decoded?.CreatedAt.UtcDateTime ?? DateTime.UnixEpoch,
            CursorTaskId:    decoded?.TaskId ?? "",
            HasCursor:       decoded is not null ? 1 : 0);

        var overFetch = _options.PageSize + 1;
        var records   = new List<TaskRecord>(overFetch);

        foreach (ref readonly var row in connection.ExecuteQuery<ListArgs, TaskRow, ListStatement>(args)) {
            records.Add(FromRow(in row));
            if (records.Count >= overFetch)
                break; // over-fetch by one to detect a next page; stop streaming early
        }

        string? nextCursor = null;
        if (records.Count > _options.PageSize) {
            var last = records[_options.PageSize - 1];
            nextCursor = EncodeCursor(last.CreatedAt, last.TaskId);
            records.RemoveRange(_options.PageSize, records.Count - _options.PageSize);
        }

        return Task.FromResult(new TaskListPage(records, nextCursor));
    }

    TimeSpan? ResolveTimeToLive(TimeSpan? requested) {
        var ttl = requested ?? _options.DefaultTimeToLive;
        if (ttl is { } value && _options.MaxTimeToLive is { } max && value > max)
            ttl = max; // silent clamp down, matching the reference
        return ttl;
    }

    void Cleanup() {
        _mutationLock.Wait();
        try {
            using var connection = _provider.GetConnection();
            connection.ExecuteNonQuery<DeleteExpiredArgs, DeleteExpiredStatement>(new(_time.GetUtcNow().UtcTicks));
        }
        catch (Exception ex) {
            // Best-effort background sweep — a transient failure is retried on the next tick.
            LogCleanupFailed(ex);
        }
        finally {
            _mutationLock.Release();
        }
    }

    // The cursor encodes the last row's (created_at, task_id) so keyset pagination survives id collation quirks.
    static string EncodeCursor(DateTimeOffset createdAt, string taskId) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{createdAt.UtcTicks}|{taskId}"));

    static (DateTimeOffset CreatedAt, string TaskId)? DecodeCursor(string? cursor) {
        if (string.IsNullOrEmpty(cursor))
            return null;

        try {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var sep  = text.IndexOf('|');
            if (sep > 0 && long.TryParse(text.AsSpan(0, sep), out var ticks))
                return (new DateTimeOffset(ticks, TimeSpan.Zero), text[(sep + 1)..]);
        }
        catch (FormatException) {
            // Malformed cursor → start from the beginning rather than throwing.
        }

        return null;
    }

    public void Dispose() {
        _cleanupTimer?.Dispose();
        _mutationLock.Dispose();
        if (_ownsProvider && _provider is IDisposable disposable)
            disposable.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Expired-task cleanup sweep failed; will retry next interval")]
    partial void LogCleanupFailed(Exception exception);
}
