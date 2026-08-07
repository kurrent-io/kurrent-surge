using Kurrent.Projectors.Transactional;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kurrent.Projectors.DuckDB;

[PublicAPI]
public interface IDuckDBConnectionProvider : IProjectionClientProvider<DuckDBAdvancedConnection> {
    IDisposable GetScopedConnection(out DuckDBAdvancedConnection connection);
}

[PublicAPI]
public sealed class DuckDBPooledConnectionProvider : IDuckDBConnectionProvider, IDisposable {
    readonly DuckDBConnectionPool _pool;
    readonly bool                 _ownsPool;

    public DuckDBPooledConnectionProvider(DuckDBConnectionPool pool) {
        ArgumentNullException.ThrowIfNull(pool);
        _pool     = pool;
        _ownsPool = false;
    }

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

    public IDisposable GetScopedConnection(out DuckDBAdvancedConnection connection) =>
        _pool.Rent(out connection);

    public void Dispose() {
        if (_ownsPool)
            _pool.Dispose();
    }

    internal static bool IsInMemory(DuckDBConnectionString cs) =>
        cs.InMemory || cs.DataSource.StartsWith(":memory:", StringComparison.OrdinalIgnoreCase);

    public ValueTask<DuckDBAdvancedConnection> GetClient(CancellationToken cancellationToken) =>
        ValueTask.FromResult(_pool.Open());
}

[PublicAPI]
public sealed class DuckDBMemoryConnectionProvider : IDuckDBConnectionProvider, IDisposable {
    static readonly IDisposable NoOp = new NoOpDisposable();

    readonly DuckDBAdvancedConnection _connection;

    public DuckDBMemoryConnectionProvider() {
        _connection = new() { ConnectionString = "DataSource=:memory:" };
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

    public ValueTask<DuckDBAdvancedConnection> GetClient(CancellationToken cancellationToken) =>
        ValueTask.FromResult(_connection);
}

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
