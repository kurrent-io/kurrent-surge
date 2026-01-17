using System.Text.Json;
using DuckDB.NET.Data;

namespace Kurrent.Client.Streams.Processing;

/// <summary>
/// DuckDB-backed implementation of the state store.
/// </summary>
/// <remarks>
/// <para>
/// Provides persistent state management using DuckDB as the storage backend.
/// State entries are stored as JSON in a single table with partition-based organization.
/// </para>
/// <para>
/// <b>Storage Model:</b> Each state entry is stored with a partition, state type,
/// state key, entry key (for map/table), and JSON-serialized data.
/// </para>
/// <para>
/// <b>Thread Safety:</b> This class is thread-safe for concurrent operations.
/// DuckDB handles internal locking for concurrent access.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Create an in-memory state store
/// await using var store = new DuckDbStateStore();
///
/// // Create a file-backed state store
/// await using var store = new DuckDbStateStore("Data Source=/path/to/state.db");
///
/// // Use the fluent API
/// await store.Value&lt;OrderStats&gt;("stats").StoreAsync(new OrderStats { Count = 1 });
/// var stats = await store.GetValueAsync&lt;OrderStats&gt;("stats");
/// </code>
/// </example>
[PublicAPI]
public sealed class DuckDbStateStore : IStateStore {
    readonly DuckDBConnection      _connection;
    readonly JsonSerializerOptions _jsonOptions;
    readonly bool                  _ownsConnection;
    readonly SemaphoreSlim         _lock = new(1, 1);

    bool _disposed;
    bool _initialized;

    /// <summary>
    /// Creates a new in-memory DuckDB state store.
    /// </summary>
    public DuckDbStateStore() : this("Data Source=:memory:") { }

    /// <summary>
    /// Creates a new DuckDB state store with the specified connection string.
    /// </summary>
    /// <param name="connectionString">
    /// The DuckDB connection string. Use "Data Source=:memory:" for in-memory storage
    /// or "Data Source=/path/to/file.db" for file-backed storage.
    /// </param>
    /// <param name="jsonOptions">
    /// Optional JSON serializer options for state serialization.
    /// Defaults to case-insensitive property names with camelCase naming.
    /// </param>
    public DuckDbStateStore(string connectionString, JsonSerializerOptions? jsonOptions = null) {
        _connection     = new DuckDBConnection(connectionString);
        _ownsConnection = true;
        _jsonOptions    = jsonOptions ?? CreateDefaultJsonOptions();
    }

    /// <summary>
    /// Creates a new DuckDB state store using an existing connection.
    /// </summary>
    /// <param name="connection">An existing DuckDB connection. The store does not take ownership.</param>
    /// <param name="jsonOptions">
    /// Optional JSON serializer options for state serialization.
    /// Defaults to case-insensitive property names with camelCase naming.
    /// </param>
    public DuckDbStateStore(DuckDBConnection connection, JsonSerializerOptions? jsonOptions = null) {
        _connection     = connection;
        _ownsConnection = false;
        _jsonOptions    = jsonOptions ?? CreateDefaultJsonOptions();
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Internal - Primitives for builders
    // ═══════════════════════════════════════════════════════════════════════════════

    internal DuckDBConnection Connection => _connection;

    internal JsonSerializerOptions JsonOptions => _jsonOptions;

    internal SemaphoreSlim Lock => _lock;

    /// <summary>
    /// Ensures the store is initialized before use.
    /// </summary>
    internal async ValueTask EnsureInitializedAsync(CancellationToken ct) {
        if (_initialized)
            return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);

        try {
            if (_initialized)
                return;

            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(ct).ConfigureAwait(false);

            await CreateSchemaAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally {
            _lock.Release();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // IStateStore - Direct Access Shortcuts
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async ValueTask<T?> GetValueAsync<T>(StateKey key, CancellationToken ct = default) where T : class =>
        await Value<T>(key).GetAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<T> GetValueAsync<T>(StateKey key, T defaultValue, CancellationToken ct = default) where T : class =>
        await Value<T>(key).GetAsync(defaultValue, ct).ConfigureAwait(false);

    // ═══════════════════════════════════════════════════════════════════════════════
    // IStateStore - Fluent Builders
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public IValueStateBuilder<T> Value<T>(StateKey key) where T : class =>
        new DuckDbValueStateBuilder<T>(this, key);

    /// <inheritdoc />
    public IMapStateBuilder<TKey, TValue> Map<TKey, TValue>(StateKey key)
        where TKey : notnull
        where TValue : class =>
        throw new NotImplementedException("MapState is not yet implemented for DuckDB backend.");

    /// <inheritdoc />
    public ITableStateBuilder<T> Table<T>(StateKey key) where T : class =>
        throw new NotImplementedException("TableState is not yet implemented for DuckDB backend.");

    // ═══════════════════════════════════════════════════════════════════════════════
    // IStateStore - Global Operations
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async ValueTask ClearAllAsync(CancellationToken ct = default) {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);

        try {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText     = "DELETE FROM state_store";
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally {
            _lock.Release();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // IAsyncDisposable
    // ═══════════════════════════════════════════════════════════════════════════════

    public async ValueTask DisposeAsync() {
        if (_disposed)
            return;

        _disposed = true;
        _lock.Dispose();

        if (_ownsConnection)
            await _connection.DisposeAsync().ConfigureAwait(false);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Private
    // ═══════════════════════════════════════════════════════════════════════════════

    async ValueTask CreateSchemaAsync(CancellationToken ct) {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText     = """
            CREATE TABLE IF NOT EXISTS state_store (
                partition   INTEGER     NOT NULL,
                state_type  VARCHAR     NOT NULL,
                state_key   VARCHAR     NOT NULL,
                entry_key   VARCHAR     NOT NULL,
                data        JSON        NOT NULL,
                type_name   VARCHAR     NOT NULL,
                updated_at  TIMESTAMP   DEFAULT current_timestamp,
                PRIMARY KEY (partition, state_type, state_key, entry_key)
            )
            """;

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

//     async ValueTask<ReadOnlyMemory<byte>> ExportToParquetAsync(PartitionRange[]? ranges, CancellationToken ct) {
//         await _lock.WaitAsync(ct).ConfigureAwait(false);
//
//         try {
//             var tempFile = Path.Combine(Path.GetTempPath(), $"state_snapshot_{Guid.NewGuid():N}.parquet");
//
//             try {
//                 await using var cmd = _connection.CreateCommand();
//
//                 if (ranges is null || ranges.Length == 0) {
//                     cmd.CommandText = $"""
//                         COPY state_store
//                         TO '{tempFile}' (FORMAT PARQUET, COMPRESSION GZIP)
//                         """;
//                 }
//                 else {
//                     var whereClause = BuildPartitionWhereClause(ranges);
//                     cmd.CommandText = $"""
//                         COPY (SELECT * FROM state_store WHERE {whereClause})
//                         TO '{tempFile}' (FORMAT PARQUET, COMPRESSION GZIP)
//                         """;
//                 }
//
//                 await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
//
//                 if (!File.Exists(tempFile))
//                     return ReadOnlyMemory<byte>.Empty;
//
//                 return await File.ReadAllBytesAsync(tempFile, ct).ConfigureAwait(false);
//             }
//             finally {
//                 if (File.Exists(tempFile))
//                     File.Delete(tempFile);
//             }
//         }
//         finally {
//             _lock.Release();
//         }
//     }
//
//     async ValueTask ImportFromParquetAsync(ReadOnlyMemory<byte> data, PartitionRange[]? ranges, CancellationToken ct) {
//         if (data.IsEmpty)
//             return;
//
//         await _lock.WaitAsync(ct).ConfigureAwait(false);
//
//         try {
//             var tempFile = Path.Combine(Path.GetTempPath(), $"state_restore_{Guid.NewGuid():N}.parquet");
//
//             try {
//                 await File.WriteAllBytesAsync(tempFile, data.ToArray(), ct).ConfigureAwait(false);
//
//                 await using var cmd = _connection.CreateCommand();
//
//                 if (ranges is null || ranges.Length == 0) {
//                     cmd.CommandText = $"""
//                         INSERT OR REPLACE INTO state_store
//                         SELECT * FROM read_parquet('{tempFile}')
//                         """;
//                 }
//                 else {
//                     var whereClause = BuildPartitionWhereClause(ranges);
//                     cmd.CommandText = $"""
//                         INSERT OR REPLACE INTO state_store
//                         SELECT * FROM read_parquet('{tempFile}')
//                         WHERE {whereClause}
//                         """;
//                 }
//
//                 await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
//             }
//             finally {
//                 if (File.Exists(tempFile))
//                     File.Delete(tempFile);
//             }
//         }
//         finally {
//             _lock.Release();
//         }
//     }
//
//     async ValueTask ClearPartitionRangesAsync(PartitionRange[] ranges, CancellationToken ct) {
//         if (ranges.Length == 0)
//             return;
//
//         await _lock.WaitAsync(ct).ConfigureAwait(false);
//
//         try {
//             var whereClause = BuildPartitionWhereClause(ranges);
//
//             await using var cmd = _connection.CreateCommand();
//             cmd.CommandText     = $"DELETE FROM state_store WHERE {whereClause}";
//
//             await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
//         }
//         finally {
//             _lock.Release();
//         }
//     }

    // static string BuildPartitionWhereClause(PartitionRange[] ranges) {
    //     var conditions = ranges.Select(r =>
    //         r.Start == r.End
    //             ? $"partition = {r.Start}"
    //             : $"partition BETWEEN {r.Start} AND {r.End}"
    //     );
    //
    //     return string.Join(" OR ", conditions);
    // }

    static JsonSerializerOptions CreateDefaultJsonOptions() => new() {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        WriteIndented               = false
    };
}
