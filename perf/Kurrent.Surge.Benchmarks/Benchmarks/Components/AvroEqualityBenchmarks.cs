using BenchmarkDotNet.Attributes;
using Benchmarks.Avro;

namespace Benchmarks;

/// <summary>
/// The cost of the value equality the generator emits for every message type.
/// </summary>
/// <remarks>
/// A record's synthesized equality compares a list or a dictionary member by reference, which is fast and
/// wrong. The generated pair compares element by element instead. These measure what that correctness
/// costs, on a flat record and on one carrying a nested record, an array, a map, an enum and a union.
///
/// Equal and unequal cases are both measured. A comparison that differs in the first field exits early,
/// so only the equal case pays the full traversal — that is the number worth watching.
/// </remarks>
[MemoryDiagnoser(displayGenColumns: false), Config(typeof(CodecBenchmarkConfig))]
[BenchmarkCategory("Codecs")]
public class AvroEqualityBenchmarks {
    BenchFlatOrder    _flat            = null!;
    BenchFlatOrder    _flatSame        = null!;
    BenchFlatOrder    _flatDifferent   = null!;
    BenchComplexOrder _complex         = null!;
    BenchComplexOrder _complexSame     = null!;
    BenchComplexOrder _complexLastItem = null!;

    [GlobalSetup]
    public void Setup() {
        _flat          = NewFlat("ORD-1");
        _flatSame      = NewFlat("ORD-1");
        _flatDifferent = NewFlat("ORD-2");

        _complex     = NewComplex("handle with care");
        _complexSame = NewComplex("handle with care");

        // Differs only in the last element of the array, so the comparison walks everything before it.
        _complexLastItem = NewComplex("handle with care") with {
            Lines = [new OrderLine { Sku = Fresh("SKU-A"), Quantity = 2 }, new OrderLine { Sku = Fresh("SKU-B"), Quantity = 8 }]
        };
    }

    /// <summary>
    /// A copy of <paramref name="value"/> that shares no reference with it.
    /// </summary>
    /// <remarks>
    /// Two fields assigned the same literal are the same interned instance, so <c>string.Equals</c>
    /// returns on reference identity without reading a character — which would measure interning rather
    /// than comparison, and make the equal case look cheaper than the early-exit one. A decoded message
    /// carries fresh strings off the wire, so this is the shape the codec actually compares.
    /// </remarks>
    static string Fresh(string value) => new(value.AsSpan());

    static BenchFlatOrder NewFlat(string orderId) => new() {
        OrderId  = Fresh(orderId),
        Sku      = Fresh("SKU-9"),
        Quantity = 7
    };

    static BenchComplexOrder NewComplex(string note) => new() {
        OrderId  = Fresh("ORD-2"),
        Customer = new Customer { Name = Fresh("Ripley"), Age = 32 },
        Lines    = [new OrderLine { Sku = Fresh("SKU-A"), Quantity = 2 }, new OrderLine { Sku = Fresh("SKU-B"), Quantity = 7 }],
        Counters = new Dictionary<string, long> { [Fresh("hypersleep")] = 57L, [Fresh("decks")] = 3L },
        Status   = OrderStatus.Shipped,
        Note     = Fresh(note)
    };

    [Benchmark(Baseline = true), BenchmarkCategory("EqualsFlat")]
    public bool EqualsFlat_Equal() => _flat.Equals(_flatSame);

    [Benchmark, BenchmarkCategory("EqualsFlat")]
    public bool EqualsFlat_Different() => _flat.Equals(_flatDifferent);

    [Benchmark(Baseline = true), BenchmarkCategory("EqualsComplex")]
    public bool EqualsComplex_Equal() => _complex.Equals(_complexSame);

    [Benchmark, BenchmarkCategory("EqualsComplex")]
    public bool EqualsComplex_LastElementDiffers() => _complex.Equals(_complexLastItem);

    [Benchmark(Baseline = true), BenchmarkCategory("HashCode")]
    public int HashCodeFlat() => _flat.GetHashCode();

    [Benchmark, BenchmarkCategory("HashCode")]
    public int HashCodeComplex() => _complex.GetHashCode();
}
