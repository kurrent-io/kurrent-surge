using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;

namespace Kurrent.Surge.Mcp;

/// <summary>
/// Hands out <see cref="DuckDBAdvancedConnection"/> instances to callers. Pool-backed
/// providers open a fresh pooled connection per call; single-connection providers return
/// one shared connection (which the caller must not dispose).
/// </summary>
[PublicAPI]
public interface IDuckDBConnectionProvider {
    /// <summary>Returns a connection the caller owns for the duration of the call.</summary>
    DuckDBAdvancedConnection GetConnection();
}

/// <summary>
/// Pool-backed <see cref="IDuckDBConnectionProvider"/>. File-based connections only —
/// <see cref="DuckDBConnectionPool"/> rejects in-memory data sources by design (each
/// <c>:memory:</c> connection is its own isolated database).
/// </summary>
[PublicAPI]
public sealed class DuckDBPooledConnectionProvider : IDuckDBConnectionProvider, IDisposable {
    readonly DuckDBConnectionPool _pool;
    readonly bool                 _ownsPool;

    /// <summary>Use an externally-owned pool. The caller is responsible for disposing it.</summary>
    public DuckDBPooledConnectionProvider(DuckDBConnectionPool pool) {
        _pool     = pool;
        _ownsPool = false;
    }

    /// <summary>
    /// Construct and own a pool for the given file-based connection string. Throws
    /// <see cref="ArgumentException"/> for in-memory connection strings — use
    /// <see cref="DuckDBMemoryConnectionProvider"/> (or <see cref="DuckDBConnectionProvider.Create"/>) instead.
    /// </summary>
    public DuckDBPooledConnectionProvider(DuckDBConnectionString connectionString) {
        if (IsInMemory(connectionString))
            throw new ArgumentException(
                "In-memory connection strings are not supported by the pooled provider. " +
                "Use DuckDBMemoryConnectionProvider or DuckDBConnectionProvider.Create instead.",
                nameof(connectionString));

        _pool     = new DuckDBConnectionPool(connectionString);
        _ownsPool = true;
    }

    public DuckDBAdvancedConnection GetConnection() =>
        _pool.Open();

    public void Dispose() {
        if (_ownsPool)
            _pool.Dispose();
    }

    internal static bool IsInMemory(DuckDBConnectionString cs) =>
        cs.InMemory || cs.DataSource.StartsWith(":memory:", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Single-connection <see cref="IDuckDBConnectionProvider"/> backed by one in-memory
/// <see cref="DuckDBAdvancedConnection"/>. Intended for tests and local development — state
/// dies with the process, so a task store pointed at one of these loses all tasks on restart.
/// </summary>
[PublicAPI]
public sealed class DuckDBMemoryConnectionProvider : IDuckDBConnectionProvider, IDisposable {
    readonly DuckDBAdvancedConnection _connection;

    /// <summary>Opens a fresh isolated in-memory database.</summary>
    public DuckDBMemoryConnectionProvider() {
        _connection = new DuckDBAdvancedConnection { ConnectionString = "DataSource=:memory:" };
        _connection.Open();
    }

    public DuckDBAdvancedConnection GetConnection() => _connection;

    public void Dispose() => _connection.Dispose();
}

/// <summary>
/// Factory that builds the right <see cref="IDuckDBConnectionProvider"/> for a given
/// connection string — memory provider for <c>:memory:</c>-style sources, pooled for files.
/// </summary>
[PublicAPI]
public static class DuckDBConnectionProvider {
    public static IDuckDBConnectionProvider Create(DuckDBConnectionString connectionString) {
        return DuckDBPooledConnectionProvider.IsInMemory(connectionString)
            ? new DuckDBMemoryConnectionProvider()
            : new DuckDBPooledConnectionProvider(connectionString);
    }
}
