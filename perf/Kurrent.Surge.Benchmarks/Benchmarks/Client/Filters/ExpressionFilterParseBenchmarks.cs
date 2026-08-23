using BenchmarkDotNet.Attributes;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.RecordFilters;

namespace Benchmarks;

[Config(typeof(BenchmarkConfig))]
public class ExpressionFilterParseBenchmarks {
    [ParamsSource(nameof(Expressions))]
    public string Expression { get; set; } = "";

    [Benchmark]
    public Filter<Record> Parse() => RecordFilterSchema.Default.Parse(Expression);

    public static IEnumerable<string> Expressions => [
        "stream = 'orders-123'",
        "position > 100",
        "stream LIKE 'orders-%'",
        "schema ILIKE '%user%'",
        "stream IN ('orders-1', 'orders-2', 'orders-3')",
        "position BETWEEN 10 AND 100",
        "metadata['tenant'] IS NOT NULL",
        "metadata['region'] = 'us-east'",
        "stream = 'orders-123' AND position > 10 AND schema = 'OrderCreated'",
        "stream = 'orders-123' OR stream = 'orders-456'"
    ];
}
