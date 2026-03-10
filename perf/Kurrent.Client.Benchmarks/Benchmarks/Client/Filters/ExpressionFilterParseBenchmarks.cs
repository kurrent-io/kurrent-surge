using BenchmarkDotNet.Attributes;
using Kurrent.Client.Streams;

namespace Benchmarks;

[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class ExpressionFilterParseBenchmarks {
    ExpressionFilterParser _parser = null!;

    [GlobalSetup]
    public void Setup() => _parser = new ExpressionFilterParser();

    [ParamsSource(nameof(Expressions))]
    public string Expression { get; set; } = "";

    [Benchmark]
    public IFilterExpression Parse() => _parser.Parse(Expression).Value;

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
