using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Kurrent.Client.Queries;
using Kurrent.Client.Streams;
using SqlParser;
using SqlParser.Dialects;

BenchmarkRunner.Run<ParserBench>();

[MemoryDiagnoser]
[ShortRunJob]
public class ParserBench {
    // Three complexity tiers. Each pair is equivalent — the SQL wraps the predicate
    // in `SELECT stream, data.amount FROM kdb.records WHERE ...` so all three parsers
    // see apples-to-apples input on the full-query rows (predicate-only row feeds just
    // the predicate to ExpressionFilterParser as a baseline).
    public static readonly string[] Predicates = [
        // Simple — single comparison
        "stream = 'orders'",

        // Medium — three ANDed predicates with a dotted path
        "stream = 'orders' AND data.amount > 100 AND schema.name = 'OrderPlaced'",

        // Complex — nested groups, OR, LIKE, IN
        "stream = 'orders' AND (data.amount > 100 OR data.category LIKE 'premium%') " +
        "AND data.customer.country IN ('US', 'UK', 'DE') AND timestamp > '2026-01-01'"
    ];

    public static readonly string[] FullQueries = [
        $"SELECT stream, data.amount FROM kdb.records WHERE {Predicates[0]}",
        $"SELECT stream, data.amount FROM kdb.records WHERE {Predicates[1]}",
        $"SELECT stream, data.amount FROM kdb.records WHERE {Predicates[2]}"
    ];

    [Params(0, 1, 2)]
    public int Tier { get; set; }

    // One instance per run — construction cost is out of scope.
    static readonly Parser        SqlParser  = new();
    static readonly DuckDbDialect SqlDialect = new();

    string _predicate = "";
    string _fullQuery = "";

    [GlobalSetup]
    public void Setup() {
        _predicate = Predicates[Tier];
        _fullQuery = FullQueries[Tier];
    }

    [Benchmark(Baseline = true, Description = "ExpressionFilterParser (predicate only)")]
    public IFilterExpression ExpressionFilter_Predicate() =>
        ExpressionFilterParser.Parse(_predicate);

    [Benchmark(Description = "QuerySqlParser (bespoke, full SELECT)")]
    public ParsedQuery QuerySqlParser_FullQuery() =>
        QuerySqlParser.Parse(_fullQuery);

    [Benchmark(Description = "SqlParserCS (full SELECT)")]
    public object SqlParser_FullQuery() =>
        SqlParser.ParseSql(_fullQuery, SqlDialect);
}
