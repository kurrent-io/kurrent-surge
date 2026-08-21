using System.Globalization;
using System.Text.Json.Nodes;
using DuckDB.NET.Data;
using Kurrent.Surge.RecordFilters.Tests.Targets;

namespace Kurrent.Surge.RecordFilters.Tests;

// DuckDB is fed the rendered SQL rather than the source text, so the renderer itself stays under test.
[Category("RecordFilters")]
[Category("DuckDB")]
public class DuckDbDifferentialTests : IDisposable {
    readonly DuckDBConnection _connection;

    public DuckDbDifferentialTests() {
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

        foreach (var order in Seed) {
            cmd.CommandText = InsertFor(order);
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose() => _connection.Dispose();

    #region . Seed .

    static readonly DateTimeOffset Epoch = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

    public static readonly Order[] Seed = [
        Row("orders-1",   1,  "shipped",   150.00m, 0, ("region", "us-east"), """{"address":{"city":"lisbon"},"priority":3}"""),
        Row("orders-2",   2,  "pending",    50.00m, 1, ("region", "eu-west"), """{"address":{"city":"porto"},"priority":1}"""),
        Row("orders-3",   3,  "SHIPPED",   150.00m, 2, ("region", "US-EAST"), """{"address":{"city":"Lisbon"},"priority":3}"""),
        Row("orders-10", 10,  "cancelled",   0.00m, 3, null,                  null),
        Row("orders-11", 11,  null,        -25.50m, 4, ("region", "apac"),    """{"priority":9}"""),
        Row("payments-1", 1,  "shipped",   999.99m, 5, ("region", "apac"),    """{"address":{"city":"tokyo"}}"""),
        Row("payments-2", 2,  "Apple",       1.00m, 6, ("region", "eu-west"), "{}"),
        Row("audit-1",    5,  "apple",       2.00m, 7, null,                  null),
        Row("audit-2",    6,  "Ärger",       3.00m, 8, ("region", "emea"),    null),
        Row("audit-3",    7,  "zebra",       4.00m, 9, null,                  null),

        // coercion and edge-case probes: a numeric-looking tag, a boolean-looking one,
        // an empty string, a zero revision, and a payload nested deeper than two levels.
        Row("edge-1",     0,  "",            5.00m, 10, ("priority", "5"),    """{"flag":"true","count":"7"}"""),
        Row("edge-2",    21,  "shipped",     7.00m, 11, ("flag", "true"),     """{"nested":{"deep":{"value":"x"}}}"""),

        // quoting probes: a value carrying an apostrophe, a hyphenated key, and a key with a space.
        // These exercise the renderer's escaping on the way back out to SQL.
        Row("edge-3",    30,  "it's",        8.00m, 12, ("user-id", "u-1"),   """{"a b":"spaced"}""")
    ];

    static Order Row(string stream, long revision, string? status, decimal amount, int tick, (string Key, string Value)? tag, string? payload) =>
        new() {
            Stream    = stream,
            Revision  = revision,
            Status    = status,
            Amount    = amount,
            CreatedAt = Epoch.AddSeconds(tick),
            Tags      = tag is { } t ? new Dictionary<string, object?> { [t.Key] = t.Value } : [],
            Payload   = payload is null ? null : JsonNode.Parse(payload)
        };

    static string InsertFor(Order order) {
        var tags = OrderSchema.TagsAsJson(order.Tags);

        return $"""
        INSERT INTO orders VALUES (
            {Text(order.Stream)},
            {order.Revision},
            {Text(order.Status)},
            {order.Amount.ToString(CultureInfo.InvariantCulture)},
            {order.CreatedAt.ToUnixTimeMilliseconds()},
            {Text(tags)},
            {Text(order.Payload?.ToJsonString())}
        )
        """;

        static string Text(string? value) =>
            value is null ? "NULL" : $"'{value.Replace("'", "''")}'";
    }

    #endregion

    async ValueTask AssertSameMatches(params string[] expressions) {
        // Arrange
        var divergences = new List<string>();

        // Act
        foreach (var expression in expressions) {
            var filter = OrderSchema.Default.Parse(expression);
            var sql    = filter.ToSql();

            var fromDuckDb = QueryStreams(sql);
            var fromDotNet = Seed.Where(filter.IsMatch).Select(o => o.Stream).Order().ToArray();

            if (!fromDuckDb.SequenceEqual(fromDotNet, StringComparer.Ordinal)) {
                divergences.Add(
                    $"{expression}  [sql: {sql}]{Environment.NewLine}" +
                    $"    duckdb: {string.Join(", ", fromDuckDb)}{Environment.NewLine}" +
                    $"    dotnet: {string.Join(", ", fromDotNet)}");
            }
        }

        // Assert
        await Assert.That(divergences).IsEmpty();
    }

    string[] QueryStreams(string predicate) {
        using var cmd = _connection.CreateCommand();

        cmd.CommandText = $"SELECT stream FROM orders WHERE {predicate} ORDER BY stream";

        using var reader = cmd.ExecuteReader();

        var streams = new List<string>();
        while (reader.Read())
            streams.Add(reader.GetString(0));

        return [.. streams.Order()];
    }

    #region . Equality and comparison .

    [Test]
    public async ValueTask agrees_on_string_equality() => await AssertSameMatches(
        "status = 'shipped'",
        "status = 'SHIPPED'",
        "status != 'shipped'",
        "status <> 'pending'",
        "stream = 'orders-1'",
        "stream != 'orders-1'"
    );

    [Test]
    public async ValueTask agrees_on_numeric_comparison() => await AssertSameMatches(
        "amount > 100",
        "amount >= 150",
        "amount < 100",
        "amount <= 0",
        "amount > -30",
        "amount = 150",
        "stream_revision > 5",
        "stream_revision <= 2"
    );

    [Test]
    public async ValueTask agrees_on_string_ordering() => await AssertSameMatches(
        "status > 'a'",
        "status < 'b'",
        "status >= 'Apple'",
        "status <= 'zebra'",
        "stream > 'orders'",
        "stream < 'p'"
    );

    [Test]
    public async ValueTask agrees_on_timestamp_comparison() => await AssertSameMatches(
        $"created_at > {Epoch.AddSeconds(4).ToUnixTimeMilliseconds()}",
        $"created_at <= {Epoch.AddSeconds(2).ToUnixTimeMilliseconds()}",
        $"created_at BETWEEN {Epoch.ToUnixTimeMilliseconds()} AND {Epoch.AddSeconds(3).ToUnixTimeMilliseconds()}"
    );

    #endregion

    #region . Null handling .

    [Test]
    public async ValueTask agrees_on_null_checks() => await AssertSameMatches(
        "status IS NULL",
        "status IS NOT NULL",
        "payload IS NULL",
        "payload IS NOT NULL",
        "tags['region'] IS NULL",
        "tags['region'] IS NOT NULL"
    );

    [Test]
    public async ValueTask agrees_on_predicates_over_null_columns() => await AssertSameMatches(
        "status = 'shipped'",
        "status != 'shipped'",
        "status LIKE 's%'",
        "status NOT LIKE 's%'",
        "status IN ('shipped', 'pending')",
        "status NOT IN ('shipped', 'pending')"
    );

    #endregion

    #region . Pattern matching .

    [Test]
    public async ValueTask agrees_on_like_patterns() => await AssertSameMatches(
        "stream LIKE 'orders%'",
        "stream LIKE '%-1'",
        "stream LIKE '%order%'",
        "stream LIKE 'orders-_'",
        "stream NOT LIKE 'orders%'",
        "status LIKE 'S%'",
        "status LIKE '%pp%'"
    );

    [Test]
    public async ValueTask agrees_on_ilike_patterns() => await AssertSameMatches(
        "status ILIKE 's%'",
        "status ILIKE '%SHIPPED%'",
        "stream ILIKE 'ORDERS%'",
        "status NOT ILIKE 'a%'"
    );

    #endregion

    #region . Sets and ranges .

    [Test]
    public async ValueTask agrees_on_set_membership() => await AssertSameMatches(
        "status IN ('shipped', 'pending')",
        "status IN ('SHIPPED')",
        "stream IN ('orders-1', 'audit-1', 'missing')",
        "stream NOT IN ('orders-1', 'orders-2')",
        "stream_revision IN (1, 2, 3)"
    );

    [Test]
    public async ValueTask agrees_on_ranges() => await AssertSameMatches(
        "amount BETWEEN 0 AND 150",
        "amount NOT BETWEEN 0 AND 150",
        "stream_revision BETWEEN 2 AND 6",
        "amount BETWEEN -30 AND 0"
    );

    #endregion

    #region . Map and JSON columns .

    [Test]
    public async ValueTask agrees_on_map_access() => await AssertSameMatches(
        "tags['region'] = 'us-east'",
        "tags['region'] = 'US-EAST'",
        "tags['region'] != 'apac'",
        "tags['region'] IN ('apac', 'emea')",
        "tags['region'] LIKE 'us%'",
        "tags['missing'] IS NULL"
    );

    [Test]
    public async ValueTask agrees_on_json_access() => await AssertSameMatches(
        "payload->>'priority' = '3'",
        "payload->>'priority' IS NULL",
        "payload.address.city = 'lisbon'",
        "payload.address.city = 'Lisbon'",
        "payload.address.city IS NOT NULL",
        "payload.address.city LIKE '%o%'"
    );

    #endregion

    #region . Aliases and composition .

    [Test]
    public async ValueTask agrees_when_the_expression_uses_aliases() => await AssertSameMatches(
        "props['region'] = 'us-east'",
        "properties['region'] IS NULL",
        "tags['region'] = 'apac'",
        "data.address.city = 'lisbon'",
        "data->>'priority' = '3'",
        "revision > 5",
        "stream_revision > 5",
        $"ts > {Epoch.AddSeconds(4).ToUnixTimeMilliseconds()}",
        $"created_at > {Epoch.AddSeconds(4).ToUnixTimeMilliseconds()}"
    );

    [Test]
    public async ValueTask agrees_when_the_column_is_qualified_or_prefixed() => await AssertSameMatches(
        "order.stream LIKE 'orders%'",
        "order.status = 'shipped'",
        "order.tags['region'] = 'apac'",
        "order.data.address.city = 'lisbon'",
        "field_stream LIKE 'orders%'",
        "field_status = 'shipped'",
        "field_revision > 5",
        "field_props['region'] = 'apac'"
    );

    [Test]
    public async ValueTask agrees_on_compound_expressions() => await AssertSameMatches(
        "stream LIKE 'orders%' AND amount > 100",
        "status = 'shipped' OR status = 'pending'",
        "(status = 'shipped' OR status = 'pending') AND amount < 500",
        "NOT (stream LIKE 'orders%')",
        "stream_revision > 1 AND stream_revision < 10 AND amount >= 0",
        "tags['region'] = 'apac' OR payload.address.city = 'lisbon'",
        "NOT (status = 'shipped' AND amount > 100)",
        "(stream LIKE 'orders%' OR stream LIKE 'audit%') AND status IS NOT NULL",
        "amount > 100 AND tags['region'] IS NOT NULL AND payload IS NOT NULL"
    );

    #endregion

    #region . Type coercion .

    [Test]
    public async ValueTask agrees_on_coercion_between_text_and_number() => await AssertSameMatches(
        "tags['priority'] = '5'",
        "tags['priority'] IS NOT NULL",
        "payload->>'count' = '7'",
        "amount = 150",
        "stream_revision = 0"
    );

    [Test]
    public async ValueTask agrees_on_boolean_shaped_values() => await AssertSameMatches(
        "tags['flag'] = 'true'",
        "tags['flag'] != 'false'",
        "payload->>'flag' = 'true'",
        "payload->>'flag' IS NULL"
    );

    #endregion

    #region . Arrow notation .

    [Test]
    public async ValueTask agrees_on_arrow_access() => await AssertSameMatches(
        "tags->>'region' = 'us-east'",
        "tags->>'region' IS NULL",
        "tags->>'region' IN ('apac', 'emea')",
        "tags->>'region' LIKE '%east'",
        "payload->>'priority' = '3'",
        "payload->>'priority' IS NOT NULL"
    );

    [Test]
    public async ValueTask agrees_on_deeply_nested_paths() => await AssertSameMatches(
        "payload.nested.deep.value = 'x'",
        "payload.nested.deep.value IS NULL",
        "payload.address.city IS NOT NULL",
        "payload.nested.missing.value IS NULL"
    );

    #endregion

    #region . Edge cases .

    [Test]
    public async ValueTask agrees_on_empty_and_zero_values() => await AssertSameMatches(
        "status = ''",
        "status != ''",
        "status > ''",
        "amount = 0",
        "amount < 0",
        "amount >= 0",
        "stream_revision = 0",
        "stream_revision > 0"
    );

    [Test]
    public async ValueTask agrees_on_negative_numbers() => await AssertSameMatches(
        "amount > -30",
        "amount < -10",
        "amount BETWEEN -30 AND -10",
        "amount NOT BETWEEN -30 AND -10"
    );

    [Test]
    public async ValueTask agrees_on_quoted_and_escaped_text() => await AssertSameMatches(
        @"status = 'it\'s'",
        @"status != 'it\'s'",
        @"status LIKE 'it%'",
        "tags['user-id'] = 'u-1'",
        "tags['user-id'] IS NULL",
        "payload->>'a b' = 'spaced'"
    );

    [Test]
    public async ValueTask agrees_regardless_of_keyword_case() => await AssertSameMatches(
        "status = 'shipped' and amount > 100",
        "status = 'shipped' AND amount > 100",
        "status = 'shipped' or status = 'pending'",
        "status is null",
        "status is not null",
        "stream_revision in (1, 2, 3)",
        "amount between 0 and 150",
        "stream not like 'orders%'",
        "Status = 'shipped' And Amount > 100"
    );

    [Test]
    public async ValueTask agrees_on_operator_precedence() => await AssertSameMatches(
        "status = 'shipped' OR status = 'pending' AND amount > 100",
        "(status = 'shipped' OR status = 'pending') AND amount > 100",
        "status = 'shipped' AND amount > 100 OR stream LIKE 'audit%'",
        "status = 'shipped' AND (amount > 100 OR stream LIKE 'audit%')",
        "NOT status = 'shipped' AND amount > 100",
        "NOT (status = 'shipped' AND amount > 100)"
    );

    [Test]
    public async ValueTask agrees_on_deeply_nested_expressions() => await AssertSameMatches(
        "((stream LIKE 'orders%' AND amount > 0) OR (stream LIKE 'audit%' AND stream_revision > 5)) AND status IS NOT NULL",
        "(((amount > 0)))"
    );

    // A predicate over a NULL column is UNKNOWN, not false, so it survives a surrounding NOT instead of flipping.
    [Test]
    public async ValueTask agrees_with_sql_on_three_valued_logic() => await AssertSameMatches(
        "NOT ((status = 'shipped' OR status = 'pending') AND (amount < 100 OR amount > 500))",
        "NOT (status = 'shipped')",
        "NOT (status != 'shipped')",
        "NOT (status LIKE 's%')",
        "NOT (status IN ('shipped', 'pending'))",
        "NOT (tags['region'] = 'apac')",
        "NOT (payload.address.city = 'lisbon')",
        "NOT (status = 'shipped' OR amount > 100)",
        "NOT (status = 'shipped' AND amount > 100)",
        "NOT (NOT (status = 'shipped'))",
        "status = 'shipped' OR NOT (tags['region'] = 'apac')",
        "NOT (status IS NULL)",
        "NOT (status IS NOT NULL)"
    );

    [Test]
    public async ValueTask agrees_on_the_optional_filter_idiom() => await AssertSameMatches(
        // The `col = @p OR @p IS NULL` shape, with a column standing in for the parameter. It works
        // because IS NULL is the one predicate that is never UNKNOWN, and TRUE OR UNKNOWN is TRUE.
        "status IS NULL OR status = 'shipped'",
        "status = 'shipped' OR status IS NULL",
        "tags['region'] IS NULL OR tags['region'] = 'apac'",
        "payload IS NULL OR payload.address.city = 'lisbon'",

        // the same shape written with `= NULL` instead, which silently matches far less
        "status = 'shipped' OR status = NULL",
        "tags['region'] = 'apac' OR tags['region'] = NULL"
    );

    [Test]
    public async ValueTask agrees_that_comparing_against_null_is_never_a_match() => await AssertSameMatches(
        "status = NULL",
        "status != NULL",
        "NOT (status = NULL)",
        "amount > NULL",
        "status IN ('shipped', NULL)",
        "status NOT IN ('shipped', NULL)",
        "amount BETWEEN NULL AND 100"
    );

    [Test]
    public async ValueTask agrees_on_patterns_that_match_nothing() => await AssertSameMatches(
        "stream = 'does-not-exist'",
        "stream LIKE 'zzz%'",
        "stream IN ('nope')",
        "amount > 99999"
    );

    #endregion
}
