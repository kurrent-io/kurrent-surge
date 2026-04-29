using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kurrent.Projections.DuckDB;

/// <summary>
/// Hands out <see cref="DuckDBAdvancedConnection"/> instances to callers. The scoped
/// variant returns an <see cref="IDisposable"/> whose Dispose releases the rental —
/// pool-backed providers return it to the pool; single-connection providers no-op.
/// </summary>
[PublicAPI]
public interface IDuckDBConnectionProvider {
    /// <summary>
    /// Returns a connection the caller owns for the duration of the call.
    /// Pool-backed providers open a fresh unmanaged connection here; single-connection
    /// providers return the shared connection (caller must not dispose it).
    /// </summary>
    DuckDBAdvancedConnection GetConnection();
}

/// <summary>
/// Pool-backed <see cref="IDuckDBConnectionProvider"/>. File-based connections only —
/// <see cref="DuckDBConnectionPool"/> rejects in-memory data sources by design (each
/// <c>:memory:</c> connection is its own isolated database, so pool rentals would see
/// inconsistent data).
/// </summary>
[PublicAPI]
public sealed class DuckDBPooledConnectionProvider : IDuckDBConnectionProvider, IDisposable {
    readonly DuckDBConnectionPool _pool;
    readonly bool                 _ownsPool;

    /// <summary>Use an externally-owned pool. The caller is responsible for disposing it.</summary>
    public DuckDBPooledConnectionProvider(DuckDBConnectionPool pool) {
        ArgumentNullException.ThrowIfNull(pool);
        _pool     = pool;
        _ownsPool = false;
    }

    /// <summary>
    /// Construct and own a pool for the given file-based connection string.
    /// Throws <see cref="ArgumentException"/> if the connection string is in-memory —
    /// use <see cref="DuckDBMemoryConnectionProvider"/> (or <see cref="DuckDBConnectionProvider.Create"/>) for that.
    /// </summary>
    public DuckDBPooledConnectionProvider(DuckDBConnectionString connectionString) {
        ArgumentNullException.ThrowIfNull(connectionString);

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

    public IDisposable GetScopedConnection(out DuckDBAdvancedConnection connection) =>
        _pool.Rent(out connection);

    public void Dispose() {
        if (_ownsPool)
            _pool.Dispose();
    }

    internal static bool IsInMemory(DuckDBConnectionString cs) =>
        cs.InMemory || cs.DataSource.StartsWith(":memory:", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Single-connection <see cref="IDuckDBConnectionProvider"/> backed by one in-memory
/// <see cref="DuckDBAdvancedConnection"/>. Intended for tests and local development —
/// state dies with the process, so a checkpoint store pointed at one of these will
/// lose its position on every restart.
/// </summary>
[PublicAPI]
public sealed class DuckDBMemoryConnectionProvider : IDuckDBConnectionProvider, IDisposable {
    static readonly IDisposable NoOp = new NoOpDisposable();

    readonly DuckDBAdvancedConnection _connection;

    /// <summary>Opens a fresh isolated in-memory database.</summary>
    public DuckDBMemoryConnectionProvider() {
        _connection = new DuckDBAdvancedConnection { ConnectionString = "DataSource=:memory:" };
        _connection.Open();
    }

    public DuckDBAdvancedConnection GetConnection() => _connection;

    public IDisposable GetScopedConnection(out DuckDBAdvancedConnection connection) {
        connection = _connection;
        return NoOp;
    }

    public void Dispose() => _connection.Dispose();

    sealed class NoOpDisposable : IDisposable {
        public void Dispose() { }
    }
}

/// <summary>
/// Factory that builds the right <see cref="IDuckDBConnectionProvider"/> for a given
/// connection string — memory provider for <c>:memory:</c>-style sources, pooled for files.
/// </summary>
[PublicAPI]
public static class DuckDBConnectionProvider {
    public static IDuckDBConnectionProvider Create(DuckDBConnectionString connectionString) {
        ArgumentNullException.ThrowIfNull(connectionString);

        return DuckDBPooledConnectionProvider.IsInMemory(connectionString)
            ? new DuckDBMemoryConnectionProvider()
            : new DuckDBPooledConnectionProvider(connectionString);
    }
}

[PublicAPI]
public static class ServiceCollectionExtensions {
    public static IServiceCollection AddDuckDBConnectionProvider(
        this IServiceCollection services,
        DuckDBConnectionString connectionString
    ) {
        services.TryAddSingleton(_ => DuckDBConnectionProvider.Create(connectionString));
        return services;
    }

    public static IServiceCollection AddDuckDBConnectionProvider(
        this IServiceCollection services,
        object serviceKey,
        DuckDBConnectionString connectionString
    ) {
        services.TryAddKeyedSingleton<IDuckDBConnectionProvider>(
            serviceKey,
            (_, _) => DuckDBConnectionProvider.Create(connectionString));
        return services;
    }
}
