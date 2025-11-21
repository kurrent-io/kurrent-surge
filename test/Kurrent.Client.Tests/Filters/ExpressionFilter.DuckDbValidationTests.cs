using DuckDB.NET.Data;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
[Category("DuckDB")]
public class ExpressionFilterDuckDbValidationTests : IDisposable {
    readonly DuckDBConnection _connection;

    public ExpressionFilterDuckDbValidationTests() {
        _connection = new DuckDBConnection("Data Source=:memory:");
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
        CREATE TABLE records (
            record_id UUID,
            stream VARCHAR,
            schema_name VARCHAR,
            timestamp TIMESTAMPTZ,
            position BIGINT,
            stream_revision BIGINT,
            properties MAP(VARCHAR, VARCHAR)
        )
        """;

        cmd.ExecuteNonQuery();
    }

    public void Dispose() {
        _connection.Dispose();
    }

    [Test]
    public async Task validates_expressions_against_duckdb() {
        var expressions = new[] {
            "stream = 'orders'",
            "stream LIKE 'order%'",
            "position > 100",
            "stream IN ('orders', 'payments')",
            "schema_name = 'OrderCreated' AND position >= 0",
            "timestamp >= '2024-01-01'",
            "properties['userId'] IS NOT NULL"
        };

        var failures = new List<string>();

        foreach (var expression in expressions)
            try {
                await using var cmd = _connection.CreateCommand();
                cmd.CommandText = $"EXPLAIN SELECT * FROM records WHERE {expression}";
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex) {
                failures.Add($"{expression}: {ex.Message}");
            }

        failures.ShouldBeEmpty($"DuckDB validation failures:\n{string.Join(Environment.NewLine, failures)}");
    }
}
