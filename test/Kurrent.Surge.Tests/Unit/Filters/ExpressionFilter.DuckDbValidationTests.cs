using DuckDB.NET.Data;

namespace Kurrent.Surge.Tests.Filters;

[Category("Filters")]
[Category("DuckDB")]
[Category("Integration")]
public class ExpressionFilterDuckDbValidationTests : IDisposable {
    readonly DuckDBConnection _connection;

    public ExpressionFilterDuckDbValidationTests() {
        _connection = new DuckDBConnection("Data Source=:memory:");
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
        CREATE TABLE records (
            log_position BIGINT NOT NULL,
            commit_position BIGINT,
            stream_revision BIGINT NOT NULL,
            created_at BIGINT NOT NULL,
            expires_at BIGINT,
            stream VARCHAR NOT NULL,
            stream_hash UBIGINT NOT NULL,
            schema_name VARCHAR NOT NULL,
            category VARCHAR NOT NULL,
            deleted BOOLEAN NOT NULL,
            schema_id VARCHAR,
            schema_format VARCHAR NOT NULL,
            record_id BLOB NOT NULL,
            data JSON,
            metadata JSON
        )
        """;

        cmd.ExecuteNonQuery();
    }

    public void Dispose() {
        _connection.Dispose();
    }

    async Task ValidateExpressions(string category, string[] expressions) {
        var failures = new List<string>();

        foreach (var expression in expressions) {
            try {
                await using var cmd = _connection.CreateCommand();
                cmd.CommandText = $"EXPLAIN SELECT * FROM records WHERE {expression}";
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex) {
                failures.Add($"[{category}] {expression}: {ex.Message}");
            }
        }

        await Assert.That(failures).IsEmpty();
    }

    #region . Scalar Columns .

    [Test]
    public async Task validates_all_comparison_operators() => await ValidateExpressions("comparison", [
        "stream = 'orders'",
        "stream != 'orders'",
        "stream <> 'orders'",
        "log_position > 100",
        "log_position < 100",
        "log_position >= 100",
        "log_position <= 100",
        "log_position > -10",
        "stream_revision = 5",
        "schema_name = 'OrderCreated'",
        "category = 'orders'",
        "schema_format = 'json'",
        "deleted = true",
        "deleted = false"
    ]);

    [Test]
    public async Task validates_logical_operators() => await ValidateExpressions("logical", [
        "stream = 'orders' AND log_position > 0",
        "stream = 'orders' OR stream = 'payments'",
        "NOT (stream = 'orders')",
        "(stream = 'orders' OR stream = 'payments') AND log_position >= 0",
        "NOT (stream = 'test') AND schema_name = 'OrderCreated'",
        "stream = 'a' OR stream = 'b' OR stream = 'c'",
        "stream = 'orders' AND schema_name = 'OrderCreated' AND log_position > 0"
    ]);

    [Test]
    public async Task validates_pattern_matching() => await ValidateExpressions("pattern", [
        "stream LIKE 'order%'",
        "stream LIKE '%orders'",
        "stream LIKE '%order%'",
        "stream LIKE 'order_'",
        "stream ILIKE 'ORDER%'",
        "stream NOT LIKE 'test%'",
        "stream NOT ILIKE 'TEST%'",
        "schema_name LIKE '%Event'",
        "schema_name ILIKE '%event%'",
        "category LIKE 'order%'"
    ]);

    [Test]
    public async Task validates_null_checks() => await ValidateExpressions("null", [
        "stream IS NULL",
        "stream IS NOT NULL",
        "schema_id IS NULL",
        "schema_id IS NOT NULL",
        "expires_at IS NULL",
        "expires_at IS NOT NULL",
        "data IS NULL",
        "data IS NOT NULL",
        "metadata IS NULL",
        "metadata IS NOT NULL"
    ]);

    [Test]
    public async Task validates_set_membership() => await ValidateExpressions("set", [
        "stream IN ('orders', 'payments')",
        "stream IN ('orders', 'payments', 'refunds')",
        "stream NOT IN ('test', 'debug')",
        "schema_name IN ('OrderCreated', 'OrderUpdated', 'OrderDeleted')",
        "schema_name NOT IN ('SystemEvent', 'HeartbeatEvent')",
        "category IN ('orders', 'payments')"
    ]);

    [Test]
    public async Task validates_range_operators() => await ValidateExpressions("range", [
        "log_position BETWEEN 0 AND 100",
        "log_position NOT BETWEEN 0 AND 100",
        "stream_revision BETWEEN 0 AND 50",
        "created_at BETWEEN 1704067200000 AND 1735689600000",
        "created_at NOT BETWEEN 0 AND 1704067200000"
    ]);

    [Test]
    public async Task validates_created_at_as_bigint() => await ValidateExpressions("created_at", [
        "created_at >= 1704067200000",
        "created_at > 0",
        "created_at < 1735689600000",
        "created_at = 1704067200000",
        "created_at != 0",
        "created_at BETWEEN 1704067200000 AND 1735689600000",
        "created_at NOT BETWEEN 0 AND 1704067200000",
        "created_at IN (1704067200000, 1735689600000)"
    ]);

    #endregion

    #region . JSON Metadata Access .

    [Test]
    public async Task validates_metadata_bracket_access() => await ValidateExpressions("metadata bracket", [
        "metadata['userId'] = 'user-123'",
        "metadata['userId'] IS NOT NULL",
        "metadata['userId'] IS NULL",
        "metadata['amount'] > '100'",
        "metadata['region'] IN ('us-east', 'us-west')",
        "metadata['region'] LIKE 'us-%'",
        "metadata['region'] NOT LIKE '%test%'",
        "metadata['region'] ILIKE 'US-%'",
        "metadata['active'] = 'true'",
        "metadata['order.total'] > '50'"
    ]);

    [Test]
    public async Task validates_metadata_arrow_access() => await ValidateExpressions("metadata arrow", [
        "metadata->>'userId' = 'user-123'",
        "metadata->>'userId' IS NOT NULL",
        "metadata->>'userId' IS NULL",
        "metadata->>'amount' > '100'",
        "metadata->>'region' IN ('us-east', 'us-west')",
        "metadata->>'region' LIKE 'us-%'",
        "metadata->>'region' NOT IN ('eu-west', 'ap-south')",
        "metadata->>'score' BETWEEN '0' AND '100'"
    ]);

    [Test]
    public async Task validates_metadata_dot_access() => await ValidateExpressions("metadata dot", [
        "metadata.region = 'us-east'",
        "metadata.region IS NOT NULL",
        "metadata.region IS NULL",
        "metadata.region LIKE 'us-%'",
        "metadata.region ILIKE 'US-%'",
        "metadata.userId = 'user-123'"
    ]);

    #endregion

    #region . JSON Data Access .

    [Test]
    public async Task validates_data_bracket_access() => await ValidateExpressions("data bracket", [
        "data['region'] = 'us-east'",
        "data['region'] IS NOT NULL",
        "data['region'] IS NULL",
        "data['amount'] > '100'",
        "data['tags'] IS NOT NULL"
    ]);

    [Test]
    public async Task validates_data_arrow_access() => await ValidateExpressions("data arrow", [
        "data->>'region' = 'us-east'",
        "data->>'tags' IS NOT NULL",
        "data->>'amount' > '100'",
        "data->>'region' IN ('us-east', 'us-west')",
        "data->>'region' LIKE 'us-%'",
        "data->>'region' NOT LIKE '%test%'",
        "data->>'active' = 'true'"
    ]);

    [Test]
    public async Task validates_data_arrow_with_jsonpath() => await ValidateExpressions("data JSONPath", [
        "data->>'$.region' = 'us-east'",
        "data->>'$.tags' IS NOT NULL"
    ]);

    // -> (single arrow) is not supported. It returns JSON type, which is only useful
    // for chained traversal (data->'a'->'b'). Since ->> and dot notation cover all
    // filter use cases, there's no reason to add it.

    [Test]
    public async Task validates_data_dot_access() => await ValidateExpressions("data dot", [
        "data.region = 'us-east'",
        "data.region IS NOT NULL",
        "data.region IS NULL",
        "data.tags IS NOT NULL"
    ]);

    [Test]
    public async Task validates_data_nested_dot_access() => await ValidateExpressions("data nested dot", [
        "data.address.city = 'NYC'",
        "data.address.city IS NOT NULL",
        "data.user.profile.name = 'Alice'"
    ]);

    #endregion

    #region . Complex Queries .

    [Test]
    public async Task validates_cross_column_filters() => await ValidateExpressions("cross-column", [
        "stream = 'orders' AND metadata->>'region' IN ('us-east', 'us-west')",
        "schema_name = 'OrderCreated' AND data->>'amount' > '100'",
        "stream LIKE 'order%' AND data->>'region' = 'us-east'",
        "category = 'orders' AND schema_name = 'OrderCreated'",
        "stream = 'orders' AND log_position > 1000 AND created_at >= 1704067200000"
    ]);

    [Test]
    public async Task validates_business_logic_queries() => await ValidateExpressions("business logic", [
        // multi-predicate AND chain
        "stream = 'orders' AND metadata->>'amount' > '100' AND metadata->>'region' IN ('us-east', 'us-west') AND metadata->>'customerType' != 'internal'",
        // OR with grouping
        "(stream = 'orders' OR stream = 'refunds') AND metadata->>'userId' = 'user-123'",
        // NOT with complex inner
        "NOT (schema_name = 'SystemEvent' OR schema_name = 'HeartbeatEvent')",
        // nested JSON with scalar predicates
        "data.address.city = 'NYC' AND stream = 'customers' AND created_at >= 1704067200000",
        // mixed metadata access styles
        "metadata->>'region' = 'us-east' AND data->>'status' = 'active'",
        // pattern matching with JSON
        "stream LIKE 'order%' AND metadata->>'userId' IS NOT NULL AND data->>'amount' > '0'",
        // range on scalar with JSON filter
        "log_position BETWEEN 1000 AND 5000 AND schema_name IN ('OrderCreated', 'OrderUpdated')",
        // category filter with metadata
        "category = 'orders' AND metadata->>'priority' = 'high' AND created_at >= 1704067200000"
    ]);

    [Test]
    public async Task validates_negated_operators() => await ValidateExpressions("negated", [
        "stream NOT LIKE 'test%'",
        "stream NOT ILIKE 'SYSTEM%'",
        "stream NOT IN ('$all', '$settings')",
        "log_position NOT BETWEEN 0 AND 10",
        "NOT (stream = 'test' AND schema_name = 'Debug')",
        "metadata->>'region' NOT IN ('dev', 'staging')",
        "metadata->>'region' NOT LIKE '%test%'",
        "data->>'env' NOT IN ('dev', 'staging')"
    ]);

    #endregion
}
