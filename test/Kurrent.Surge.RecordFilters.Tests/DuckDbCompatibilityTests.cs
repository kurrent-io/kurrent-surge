using DuckDB.NET.Data;
using Kurrent.Surge.RecordFilters.Tests.Targets;

namespace Kurrent.Surge.RecordFilters.Tests;

[Category("RecordFilters")]
[Category("DuckDB")]
public class DuckDbCompatibilityTests : IDisposable {
    readonly DuckDBConnection _connection;

    public DuckDbCompatibilityTests() {
        _connection = new DuckDBConnection("Data Source=:memory:");
        _connection.Open();

        using var cmd = _connection.CreateCommand();

        cmd.CommandText = """
        CREATE TABLE orders (
            stream          VARCHAR,
            stream_revision BIGINT,
            status          VARCHAR,
            amount          DECIMAL(18,2),
            created_at      BIGINT,
            tags            JSON,
            payload         JSON
        )
        """;

        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();

    async ValueTask ValidateExpressions(string category, string[] expressions, bool expressionIsValidSql = true) {
        // Arrange
        var duckDbFailures   = new List<string>();
        var parserFailures   = new List<string>();
        var renderedFailures = new List<string>();

        // Act
        foreach (var expression in expressions) {
            if (expressionIsValidSql && Explain(expression) is { } expressionError)
                duckDbFailures.Add($"[{category}] {expression}: {expressionError}");

            if (!OrderSchema.Default.TryParse(expression, out var filter, out var parseError)) {
                parserFailures.Add($"[{category}] {expression}: {parseError.ErrorMessage}");
                continue;
            }

            string rendered;

            try {
                rendered = filter.ToSql();
            }
            catch (Exception ex) {
                renderedFailures.Add($"[{category}] {expression}: render failed — {ex.Message}");
                continue;
            }

            if (Explain(rendered) is { } renderedError)
                renderedFailures.Add($"[{category}] {expression} rendered as {rendered}: {renderedError}");
        }

        // Assert
        await Assert.That(duckDbFailures).IsEmpty();
        await Assert.That(parserFailures).IsEmpty();
        await Assert.That(renderedFailures).IsEmpty();
    }

    string? Explain(string predicate) {
        try {
            using var cmd = _connection.CreateCommand();

            cmd.CommandText = $"EXPLAIN SELECT * FROM orders WHERE {predicate}";
            cmd.ExecuteNonQuery();

            return null;
        }
        catch (Exception ex) {
            return ex.Message;
        }
    }

    #region . Scalar Columns .

    [Test]
    public async ValueTask validates_all_comparison_operators() => await ValidateExpressions("comparison", [
        "stream = 'orders-1'",
        "stream != 'orders-1'",
        "stream <> 'orders-1'",
        "stream_revision > 100",
        "stream_revision < 100",
        "stream_revision >= 100",
        "stream_revision <= 100",
        "stream_revision > -10",
        "stream_revision = 5",
        "status = 'shipped'",
        "amount > 99.95",
        "amount <= 1000",
        "created_at = 1704067200000",
        "created_at != 0"
    ]);

    [Test]
    public async ValueTask validates_logical_operators() => await ValidateExpressions("logical", [
        "stream = 'orders-1' AND stream_revision > 0",
        "stream = 'orders-1' OR stream = 'payments-1'",
        "NOT (stream = 'orders-1')",
        "(stream = 'orders-1' OR stream = 'payments-1') AND stream_revision >= 0",
        "NOT (status = 'draft') AND stream = 'orders-1'",
        "stream = 'a' OR stream = 'b' OR stream = 'c'",
        "stream = 'orders-1' AND status = 'shipped' AND amount > 0"
    ]);

    [Test]
    public async ValueTask validates_pattern_matching() => await ValidateExpressions("pattern", [
        "stream LIKE 'order%'",
        "stream LIKE '%orders'",
        "stream LIKE '%order%'",
        "stream LIKE 'order_'",
        "stream ILIKE 'ORDER%'",
        "stream NOT LIKE 'test%'",
        "stream NOT ILIKE 'TEST%'",
        "status LIKE '%ped'",
        "status ILIKE '%SHIP%'",
        "status NOT LIKE 'draft%'"
    ]);

    [Test]
    public async ValueTask validates_set_membership() => await ValidateExpressions("set", [
        "stream IN ('orders-1', 'payments-1')",
        "stream IN ('orders-1', 'payments-1', 'refunds-1')",
        "stream NOT IN ('test', 'debug')",
        "status IN ('shipped', 'pending', 'cancelled')",
        "status NOT IN ('draft', 'void')",
        "created_at IN (1704067200000, 1735689600000)"
    ]);

    [Test]
    public async ValueTask validates_range_operators() => await ValidateExpressions("range", [
        "stream_revision BETWEEN 0 AND 100",
        "stream_revision NOT BETWEEN 0 AND 100",
        "amount BETWEEN 10 AND 500",
        "created_at BETWEEN 1704067200000 AND 1735689600000",
        "created_at NOT BETWEEN 0 AND 1704067200000"
    ]);

    [Test]
    public async ValueTask validates_null_checks() => await ValidateExpressions("null", [
        "stream IS NULL",
        "stream IS NOT NULL",
        "status IS NULL",
        "status IS NOT NULL",
        "tags IS NOT NULL",
        "payload IS NOT NULL"
    ]);

    #endregion

    #region . Map Column Access .

    [Test]
    public async ValueTask validates_map_bracket_access() => await ValidateExpressions("map bracket", [
        "tags['region'] = 'us-east'",
        "tags['region'] IS NOT NULL",
        "tags['region'] IS NULL",
        "tags['region'] IN ('us-east', 'us-west')",
        "tags['region'] LIKE 'us-%'",
        "tags['region'] NOT LIKE '%test%'",
        "tags['region'] ILIKE 'US-%'",
        "tags['order.total'] > '50'"
    ]);

    [Test]
    public async ValueTask validates_map_arrow_access() => await ValidateExpressions("map arrow", [
        "tags->>'region' = 'us-east'",
        "tags->>'region' IS NOT NULL",
        "tags->>'region' IN ('us-east', 'us-west')",
        "tags->>'region' NOT IN ('eu-west', 'ap-south')",
        "tags->>'score' BETWEEN '0' AND '100'"
    ]);

    [Test]
    public async ValueTask validates_map_dot_access() => await ValidateExpressions("map dot", [
        "tags.region = 'us-east'",
        "tags.region IS NOT NULL",
        "tags.region LIKE 'us-%'",
        "tags.region ILIKE 'US-%'"
    ]);

    #endregion

    #region . JSON Column Access .

    [Test]
    public async ValueTask validates_json_bracket_access() => await ValidateExpressions("json bracket", [
        "payload['x'] = 'y'",
        "payload['region'] IS NOT NULL",
        "payload['amount'] > '100'"
    ]);

    [Test]
    public async ValueTask validates_json_arrow_access() => await ValidateExpressions("json arrow", [
        "payload->>'x' = 'y'",
        "payload->>'tags' IS NOT NULL",
        "payload->>'region' IN ('us-east', 'us-west')",
        "payload->>'region' LIKE 'us-%'"
    ]);

    // A key already written as a JSONPath renders with a second '$.' in front of it. DuckDB plans
    // '$.$.x' without complaint, so both sides are valid SQL — they do not select the same member.
    [Test]
    public async ValueTask validates_json_arrow_with_jsonpath() => await ValidateExpressions("json JSONPath", [
        "payload->>'$.region' = 'us-east'",
        "payload->>'$.tags' IS NOT NULL"
    ]);

    [Test]
    public async ValueTask validates_json_dot_access() => await ValidateExpressions("json dot", [
        "payload.region = 'us-east'",
        "payload.region IS NULL"
    ]);

    [Test]
    public async ValueTask validates_json_nested_dot_access() => await ValidateExpressions("json nested dot", [
        "payload.address.city = 'lisbon'",
        "payload.user.profile.name = 'Alice'"
    ]);

    #endregion

    #region . Complex Queries .

    [Test]
    public async ValueTask validates_cross_column_filters() => await ValidateExpressions("cross-column", [
        "stream = 'orders-1' AND tags->>'region' IN ('us-east', 'us-west')",
        "status = 'shipped' AND payload->>'amount' > '100'",
        "stream LIKE 'order%' AND payload->>'region' = 'us-east'",
        "status = 'shipped' AND amount > 100",
        "stream = 'orders-1' AND stream_revision > 1000 AND created_at >= 1704067200000"
    ]);

    [Test]
    public async ValueTask validates_business_logic_queries() => await ValidateExpressions("business logic", [
        // multi-predicate AND chain
        "stream = 'orders-1' AND tags->>'amount' > '100' AND tags->>'region' IN ('us-east', 'us-west') AND tags->>'customerType' != 'internal'",
        // OR with grouping
        "(stream = 'orders-1' OR stream = 'refunds-1') AND tags->>'userId' = 'user-123'",
        // NOT with complex inner
        "NOT (status = 'draft' OR status = 'void')",
        // nested JSON with scalar predicates
        "payload.address.city = 'lisbon' AND stream = 'customers-1' AND created_at >= 1704067200000",
        // mixed map and JSON access
        "tags->>'region' = 'us-east' AND payload->>'status' = 'active'",
        // pattern matching with keyed access
        "stream LIKE 'order%' AND tags->>'userId' IS NOT NULL AND payload->>'amount' > '0'",
        // range on scalar with set membership
        "stream_revision BETWEEN 1000 AND 5000 AND status IN ('shipped', 'pending')",
        // decimal comparison with map and timestamp
        "amount > 50 AND tags->>'priority' = 'high' AND created_at >= 1704067200000"
    ]);

    [Test]
    public async ValueTask validates_negated_operators() => await ValidateExpressions("negated", [
        "stream NOT ILIKE 'SYSTEM%'",
        "stream NOT IN ('$all', '$settings')",
        "stream_revision NOT BETWEEN 0 AND 10",
        "NOT (stream = 'test' AND status = 'draft')",
        "tags->>'region' NOT IN ('dev', 'staging')",
        "tags->>'region' NOT LIKE '%test%'",
        "payload->>'env' NOT IN ('dev', 'staging')"
    ]);

    #endregion

    #region . Aliases .

    // 'props', 'properties', 'data', 'revision', 'ts', the 'order.' qualifier and the 'field_' prefix
    // name no column DuckDB knows, so the expression as written is not SQL and check 1 is skipped.
    // Checks 2 and 3 carry the whole point: we accept the alias, and what we render is real DuckDB.
    [Test]
    public async ValueTask validates_alias_expressions_render_to_canonical_sql() =>
        await ValidateExpressions("alias", [
            "props['region'] = 'us-east'",
            "properties['region'] = 'us-east'",
            "data.address.city = 'lisbon'",
            "revision > 1",
            "ts > 0",
            "order.stream = 'x'",
            "field_status = 'x'"
        ], expressionIsValidSql: false);

    #endregion
}
