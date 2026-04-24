using DuckDB.NET.Data;

namespace Kurrent.Projections.DuckDB;

public interface IDuckDBConnectionProvider {
    DuckDBConnection GetConnection();
}

public sealed class DuckDBConnectionProvider(DuckDBConnectionString connectionString) : IDuckDBConnectionProvider {
    public DuckDBConnection GetConnection() {
        var conn = new DuckDBConnection(connectionString);
        conn.Open();
        return conn;
    }
}