using BenchmarkDotNet.Attributes;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Filters;

namespace Kurrent.Client.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class ExpressionFilterEvalBenchmarks {
    IFilterExpression _filter = null!;
    Record _record = null!;

    [ParamsSource(nameof(Scenarios))]
    public EvalScenario Scenario { get; set; } = null!;

    [GlobalSetup]
    public void Setup() {
        var parser = new ExpressionFilterParser();
        _filter = parser.Parse(Scenario.Expression).Value;
        _record = Scenario.CreateRecord();
    }

    [Benchmark]
    public bool IsMatch() => _filter.IsMatch(_record);

    public static IEnumerable<EvalScenario> Scenarios { get; } = CreateScenarios();

    static IEnumerable<EvalScenario> CreateScenarios() {
        var faker = new RecordFaker();

        return [
            // Stream equality
            new("eq-hit", "stream = 'orders-123'", () => faker.With(stream: "orders-123")),
            new("eq-miss", "stream = 'orders-123'", () => faker.With(stream: "orders-999")),

            // Numeric comparison
            new("gt-hit", "position > 100", () => faker.With(position: 200)),
            new("gt-miss", "position > 100", () => faker.With(position: 50)),

            // LIKE pattern
            new("like-hit", "stream LIKE 'orders-%'", () => faker.With(stream: "orders-abc")),
            new("like-miss", "stream LIKE 'orders-%'", () => faker.With(stream: "users-abc")),

            // ILIKE pattern
            new("ilike-hit", "schema ILIKE '%user%'", () => faker.With(schemaName: "CreateUserV1")),

            // IN set membership
            new("in-hit", "stream IN ('orders-1', 'orders-2', 'orders-3')", () => faker.With(stream: "orders-2")),
            new("in-miss", "stream IN ('orders-1', 'orders-2', 'orders-3')", () => faker.With(stream: "orders-9")),

            // BETWEEN range
            new("between-hit", "position BETWEEN 10 AND 100", () => faker.With(position: 50)),

            // IS NOT NULL
            new("notnull-hit", "metadata['tenant'] IS NOT NULL",
                () => faker.With(metadata: Metadata.New.With("tenant", "acme"))),

            // Keyed metadata access
            new("meta-hit", "metadata['region'] = 'us-east'",
                () => faker.With(metadata: Metadata.New.With("region", "us-east"))),

            // 3-clause AND (match + miss@1st clause for short-circuit)
            new("and3-hit", "stream = 'orders-123' AND position > 10 AND schema = 'OrderCreated'",
                () => faker.With(stream: "orders-123", position: 50, schemaName: "OrderCreated")),
            new("and3-miss@1", "stream = 'orders-123' AND position > 10 AND schema = 'OrderCreated'",
                () => faker.With(stream: "orders-999", position: 50, schemaName: "OrderCreated")),

            // 2-clause OR (match@1st + miss)
            new("or2-hit@1", "stream = 'orders-123' OR stream = 'orders-456'",
                () => faker.With(stream: "orders-123")),
            new("or2-miss", "stream = 'orders-123' OR stream = 'orders-456'",
                () => faker.With(stream: "orders-999")),

            // Category derivation
            new("cat-hit", "category = 'orders'", () => faker.With(stream: "orders-123")),

            // Complex: deep nesting with OR of two AND branches mixing fast paths + general path
            new("complex-nest-hit",
                "(category = 'order' AND position > 100) OR (schema ILIKE '%payment%' AND revision BETWEEN 1 AND 50)",
                () => faker.With(stream: "order-777", position: 200, schemaName: "OrderPlaced", streamRevision: 10)),
            new("complex-nest-miss",
                "(category = 'order' AND position > 100) OR (schema ILIKE '%payment%' AND revision BETWEEN 1 AND 50)",
                () => faker.With(stream: "user-1", position: 50, schemaName: "UserCreated", streamRevision: 100)),

            // Complex: wide 5-clause AND chain testing short-circuit depth
            new("wide-and5-hit",
                "stream = 'orders-123' AND schema = 'OrderPlaced' AND position > 50 AND revision < 1000 AND category = 'orders'",
                () => faker.With(stream: "orders-123", schemaName: "OrderPlaced", position: 100, streamRevision: 500)),
            new("wide-and5-miss@1",
                "stream = 'orders-123' AND schema = 'OrderPlaced' AND position > 50 AND revision < 1000 AND category = 'orders'",
                () => faker.With(stream: "orders-999", schemaName: "OrderPlaced", position: 100, streamRevision: 500)),

            // Complex: mixed IN + BETWEEN + LIKE
            new("mixed-in-btwn-like-hit",
                "schema IN ('OrderPlaced', 'OrderShipped', 'OrderCancelled') AND position BETWEEN 100 AND 500 AND stream LIKE 'orders-%'",
                () => faker.With(stream: "orders-42", schemaName: "OrderShipped", position: 250)),
            new("mixed-in-btwn-like-miss",
                "schema IN ('OrderPlaced', 'OrderShipped', 'OrderCancelled') AND position BETWEEN 100 AND 500 AND stream LIKE 'orders-%'",
                () => faker.With(stream: "orders-42", schemaName: "UserCreated", position: 250)),

            // LIKE fast-path: suffix pattern
            new("like-suffix-hit", "stream LIKE '%-created'",
                () => faker.With(stream: "order-created")),
            new("like-suffix-miss", "stream LIKE '%-created'",
                () => faker.With(stream: "order-shipped")),

            // LIKE fast-path: exact pattern (no wildcards)
            new("like-exact-hit", "stream LIKE 'orders-123'",
                () => faker.With(stream: "orders-123")),
            new("like-exact-miss", "stream LIKE 'orders-123'",
                () => faker.With(stream: "orders-456")),

            // LIKE fast-path: match-all
            new("like-matchall", "stream LIKE '%'",
                () => faker.With(stream: "anything")),

            // LIKE complex: underscore wildcard (Regex fallback)
            new("like-complex-hit", "stream LIKE 'order_-%'",
                () => faker.With(stream: "orders-123")),

            // IN with larger set (FrozenSet benefit)
            new("in-large-hit",
                "schema IN ('OrderCreated', 'OrderUpdated', 'OrderShipped', 'OrderCancelled', 'OrderRefunded', 'OrderArchived', 'OrderPlaced', 'OrderConfirmed')",
                () => faker.With(schemaName: "OrderConfirmed")),
            new("in-large-miss",
                "schema IN ('OrderCreated', 'OrderUpdated', 'OrderShipped', 'OrderCancelled', 'OrderRefunded', 'OrderArchived', 'OrderPlaced', 'OrderConfirmed')",
                () => faker.With(schemaName: "UserCreated"))
        ];
    }

    public sealed class EvalScenario(string label, string expression, Func<Record> recordFactory) {
        public string Expression => expression;

        public Record CreateRecord() => recordFactory();

        public override string ToString() => label;
    }
}
