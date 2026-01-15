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
public sealed class DuckDbStateStore : ICheckpointStateStore {
    const string ValueStateType = "value";
    const string MapStateType   = "map";
    const string TableStateType = "table";

    readonly DuckDBConnection     _connection;
    readonly JsonSerializerOptions _jsonOptions;
    readonly bool                 _ownsConnection;
    readonly SemaphoreSlim        _lock = new(1, 1);

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
    // IStateStore - Direct Access Shortcuts
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async ValueTask<T?> GetValueAsync<T>(StateKey key, CancellationToken ct = default) where T : class {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        return await LoadValueAsync<T>(key, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<T> GetValueAsync<T>(StateKey key, T defaultValue, CancellationToken ct = default) where T : class {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        return await LoadValueAsync<T>(key, ct).ConfigureAwait(false) ?? defaultValue;
    }

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
    // ICheckpointStateStore - Snapshot Operations
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>> SnapshotAsync(CancellationToken ct = default) {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        return await ExportToParquetAsync(null, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>> SnapshotAsync(int[] partitions, CancellationToken ct = default) {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var ranges = partitions.Select(PartitionRange.Single).ToArray();
        return await ExportToParquetAsync(ranges, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>> SnapshotAsync(PartitionRange[] ranges, CancellationToken ct = default) {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        return await ExportToParquetAsync(ranges, ct).ConfigureAwait(false);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // ICheckpointStateStore - Restore Operations
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async ValueTask RestoreAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        await ImportFromParquetAsync(data, null, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RestoreAsync(ReadOnlyMemory<byte> data, int[] partitions, CancellationToken ct = default) {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var ranges = partitions.Select(PartitionRange.Single).ToArray();
        await ImportFromParquetAsync(data, ranges, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RestoreAsync(ReadOnlyMemory<byte> data, PartitionRange[] ranges, CancellationToken ct = default) {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        await ImportFromParquetAsync(data, ranges, ct).ConfigureAwait(false);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // ICheckpointStateStore - Partition Management
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async ValueTask ClearPartitionsAsync(int[] partitions, CancellationToken ct = default) {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var ranges = partitions.Select(PartitionRange.Single).ToArray();
        await ClearPartitionRangesAsync(ranges, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask ClearPartitionsAsync(PartitionRange[] ranges, CancellationToken ct = default) {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        await ClearPartitionRangesAsync(ranges, ct).ConfigureAwait(false);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // IAsyncDisposable
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async ValueTask DisposeAsync() {
        if (_disposed)
            return;

        _disposed = true;
        _lock.Dispose();

        if (_ownsConnection)
            await _connection.DisposeAsync().ConfigureAwait(false);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Internal Operations (used by builders)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads a value from the store.
    /// </summary>
    internal async ValueTask<T?> LoadValueAsync<T>(StateKey key, CancellationToken ct) where T : class {
        await _lock.WaitAsync(ct).ConfigureAwait(false);

        try {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText     = """
                SELECT data FROM state_store
                WHERE partition = $partition AND state_type = $type AND state_key = $key AND entry_key = ''
                """;

            cmd.Parameters.Add(new DuckDBParameter("$partition", key.Partition));
            cmd.Parameters.Add(new DuckDBParameter("$type", ValueStateType));
            cmd.Parameters.Add(new DuckDBParameter("$key", key.Key));

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return null;

            var json = reader.GetString(0);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        finally {
            _lock.Release();
        }
    }

    /// <summary>
    /// Saves a value to the store.
    /// </summary>
    internal async ValueTask SaveValueAsync<T>(StateKey key, T value, CancellationToken ct) where T : class {
        await _lock.WaitAsync(ct).ConfigureAwait(false);

        try {
            var json     = JsonSerializer.Serialize(value, _jsonOptions);
            var typeName = typeof(T).FullName ?? typeof(T).Name;

            await using var cmd = _connection.CreateCommand();
            cmd.CommandText     = """
                INSERT INTO state_store (partition, state_type, state_key, entry_key, data, type_name, updated_at)
                VALUES ($partition, $type, $key, '', $data, $typeName, current_timestamp)
                ON CONFLICT (partition, state_type, state_key, entry_key)
                DO UPDATE SET data = $data, type_name = $typeName, updated_at = current_timestamp
                """;

            cmd.Parameters.Add(new DuckDBParameter("$partition", key.Partition));
            cmd.Parameters.Add(new DuckDBParameter("$type", ValueStateType));
            cmd.Parameters.Add(new DuckDBParameter("$key", key.Key));
            cmd.Parameters.Add(new DuckDBParameter("$data", json));
            cmd.Parameters.Add(new DuckDBParameter("$typeName", typeName));

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally {
            _lock.Release();
        }
    }

    /// <summary>
    /// Deletes a value from the store.
    /// </summary>
    internal async ValueTask DeleteValueAsync(StateKey key, CancellationToken ct) {
        await _lock.WaitAsync(ct).ConfigureAwait(false);

        try {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText     = """
                DELETE FROM state_store
                WHERE partition = $partition AND state_type = $type AND state_key = $key AND entry_key = ''
                """;

            cmd.Parameters.Add(new DuckDBParameter("$partition", key.Partition));
            cmd.Parameters.Add(new DuckDBParameter("$type", ValueStateType));
            cmd.Parameters.Add(new DuckDBParameter("$key", key.Key));

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally {
            _lock.Release();
        }
    }

    /// <summary>
    /// Checks if a value exists in the store.
    /// </summary>
    internal async ValueTask<bool> ValueExistsAsync(StateKey key, CancellationToken ct) {
        await _lock.WaitAsync(ct).ConfigureAwait(false);

        try {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText     = """
                SELECT 1 FROM state_store
                WHERE partition = $partition AND state_type = $type AND state_key = $key AND entry_key = ''
                LIMIT 1
                """;

            cmd.Parameters.Add(new DuckDBParameter("$partition", key.Partition));
            cmd.Parameters.Add(new DuckDBParameter("$type", ValueStateType));
            cmd.Parameters.Add(new DuckDBParameter("$key", key.Key));

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await reader.ReadAsync(ct).ConfigureAwait(false);
        }
        finally {
            _lock.Release();
        }
    }

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
    // Private Helpers
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

    async ValueTask<ReadOnlyMemory<byte>> ExportToParquetAsync(PartitionRange[]? ranges, CancellationToken ct) {
        await _lock.WaitAsync(ct).ConfigureAwait(false);

        try {
            var tempFile = Path.Combine(Path.GetTempPath(), $"state_snapshot_{Guid.NewGuid():N}.parquet");

            try {
                await using var cmd = _connection.CreateCommand();

                if (ranges is null || ranges.Length == 0) {
                    cmd.CommandText = $"""
                        COPY state_store
                        TO '{tempFile}' (FORMAT PARQUET, COMPRESSION GZIP)
                        """;
                }
                else {
                    var whereClause = BuildPartitionWhereClause(ranges);
                    cmd.CommandText = $"""
                        COPY (SELECT * FROM state_store WHERE {whereClause})
                        TO '{tempFile}' (FORMAT PARQUET, COMPRESSION GZIP)
                        """;
                }

                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                if (!File.Exists(tempFile))
                    return ReadOnlyMemory<byte>.Empty;

                return await File.ReadAllBytesAsync(tempFile, ct).ConfigureAwait(false);
            }
            finally {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }
        finally {
            _lock.Release();
        }
    }

    async ValueTask ImportFromParquetAsync(ReadOnlyMemory<byte> data, PartitionRange[]? ranges, CancellationToken ct) {
        if (data.IsEmpty)
            return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);

        try {
            var tempFile = Path.Combine(Path.GetTempPath(), $"state_restore_{Guid.NewGuid():N}.parquet");

            try {
                await File.WriteAllBytesAsync(tempFile, data.ToArray(), ct).ConfigureAwait(false);

                await using var cmd = _connection.CreateCommand();

                if (ranges is null || ranges.Length == 0) {
                    cmd.CommandText = $"""
                        INSERT OR REPLACE INTO state_store
                        SELECT * FROM read_parquet('{tempFile}')
                        """;
                }
                else {
                    var whereClause = BuildPartitionWhereClause(ranges);
                    cmd.CommandText = $"""
                        INSERT OR REPLACE INTO state_store
                        SELECT * FROM read_parquet('{tempFile}')
                        WHERE {whereClause}
                        """;
                }

                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            finally {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }
        finally {
            _lock.Release();
        }
    }

    async ValueTask ClearPartitionRangesAsync(PartitionRange[] ranges, CancellationToken ct) {
        if (ranges.Length == 0)
            return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);

        try {
            var whereClause = BuildPartitionWhereClause(ranges);

            await using var cmd = _connection.CreateCommand();
            cmd.CommandText     = $"DELETE FROM state_store WHERE {whereClause}";

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally {
            _lock.Release();
        }
    }

    static string BuildPartitionWhereClause(PartitionRange[] ranges) {
        var conditions = ranges.Select(r =>
            r.Start == r.End
                ? $"partition = {r.Start}"
                : $"partition BETWEEN {r.Start} AND {r.End}"
        );

        return string.Join(" OR ", conditions);
    }

    static JsonSerializerOptions CreateDefaultJsonOptions() => new() {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        WriteIndented               = false
    };
}
