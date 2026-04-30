using Kurrent.Projections.DuckDB;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;

namespace Kurrent.Processors.Tests.Projections.DuckDB;

/// <summary>
/// Test fixture for tests that need a real file-backed DuckDB pool. Creates a unique
/// temp DuckDB file and a <see cref="DuckDBConnectionPool"/> in the constructor,
/// exposes an <see cref="IDuckDBConnectionProvider"/> over it, and disposes both —
/// plus deletes the file — on teardown.
/// </summary>
/// <remarks>
/// Use with <c>[ClassDataSource&lt;DuckDBTestFixture&gt;(Shared = SharedType.None)]</c>
/// to get a fresh database per test.
/// </remarks>
public sealed class DuckDBTestFixture : IDisposable {
    readonly DuckDBConnectionPool           _pool;
    readonly DuckDBPooledConnectionProvider _provider;

    public DuckDBTestFixture() {
        var dbPath     = Path.Combine(Path.GetTempPath(), $"{PunyId.New()}.ddb");
        var connstring = $"Data Source={dbPath}";
        
        _pool     = new DuckDBConnectionPool($"Data Source={dbPath}");
        _provider = new DuckDBPooledConnectionProvider(connstring);
    }

    /// <summary>Provider backed by the fixture's pool.</summary>
    public IDuckDBConnectionProvider ConnectionProvider => _provider;

    /// <summary>
    /// Opens a fresh connection to the fixture's DuckDB file, bypassing the pool.
    /// Caller owns and must dispose.
    /// </summary>
    public DuckDBAdvancedConnection GetConnection() => _pool.Open();

    public void Dispose() {
        _provider.Dispose();
        _pool.Dispose();
    }
}
