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
[MemoryDiagnoser(displayGenColumns: false), Config(typeof(ComponentBenchmarkConfig))]
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
        _flat          = new BenchFlatOrder { OrderId = "ORD-1", Sku = "SKU-9", Quantity = 7 };
        _flatSame      = new BenchFlatOrder { OrderId = "ORD-1", Sku = "SKU-9", Quantity = 7 };
        _flatDifferent = new BenchFlatOrder { OrderId = "ORD-2", Sku = "SKU-9", Quantity = 7 };

        _complex     = NewComplex("handle with care");
        _complexSame = NewComplex("handle with care");

        // Differs only in the last element of the array, so the comparison walks everything before it.
        _complexLastItem = NewComplex("handle with care") with {
            Lines = [new OrderLine { Sku = "SKU-A", Quantity = 2 }, new OrderLine { Sku = "SKU-B", Quantity = 8 }]
        };
    }

    static BenchComplexOrder NewComplex(string note) => new() {
        OrderId  = "ORD-2",
        Customer = new Customer { Name = "Ripley", Age = 32 },
        Lines    = [new OrderLine { Sku = "SKU-A", Quantity = 2 }, new OrderLine { Sku = "SKU-B", Quantity = 7 }],
        Counters = new Dictionary<string, long> { ["hypersleep"] = 57L, ["decks"] = 3L },
        Status   = OrderStatus.Shipped,
        Note     = note
    };

    [Benchmark(Baseline = true), BenchmarkCategory("EqualsFlat")]
    public bool EqualsFlat_Equal() => _flat.Equals(_flatSame);

    [Benchmark, BenchmarkCategory("EqualsFlat")]
    public bool EqualsFlat_Different() => _flat.Equals(_flatDifferent);

    [Benchmark, BenchmarkCategory("EqualsComplex")]
    public bool EqualsComplex_Equal() => _complex.Equals(_complexSame);

    [Benchmark, BenchmarkCategory("EqualsComplex")]
    public bool EqualsComplex_LastElementDiffers() => _complex.Equals(_complexLastItem);

    [Benchmark, BenchmarkCategory("HashCode")]
    public int HashCodeFlat() => _flat.GetHashCode();

    [Benchmark, BenchmarkCategory("HashCode")]
    public int HashCodeComplex() => _complex.GetHashCode();
}
