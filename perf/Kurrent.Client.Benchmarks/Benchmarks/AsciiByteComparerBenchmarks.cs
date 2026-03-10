using System.Collections.Frozen;
using System.Text;
using BenchmarkDotNet.Attributes;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class AsciiByteComparerBenchmarks {
    byte[][] _identifiers = null!;
    AsciiByteComparer _comparer = null!;

    Dictionary<byte[], string> _dict = null!;
    FrozenDictionary<byte[], string> _frozen = null!;

    [ParamsSource(nameof(Scenarios))]
    public ComparerScenario Scenario { get; set; } = null!;

    public static IEnumerable<ComparerScenario> Scenarios => [
        new("short-5b",  ["schema", "data", "meta", "event", "topic"]),
        new("medium-10b", ["stream_id", "event_type", "created_at", "position", "correlation_id",
                           "causation_id", "log_position", "event_number", "stream_category", "metadata_val"]),
        new("long-30b",  ["stock_reservation_confirmed", "order_fulfillment_completed", "inventory_adjustment_applied"]),
    ];

    [GlobalSetup]
    public void Setup() {
        _identifiers = Scenario.Identifiers.Select(Encoding.UTF8.GetBytes).ToArray();
        _comparer    = new AsciiByteComparer();

        var pairs = _identifiers.Select((id, i) => KeyValuePair.Create(id, $"replacement_{i}")).ToArray();

        _dict   = new Dictionary<byte[], string>(pairs, _comparer);
        _frozen = pairs.ToFrozenDictionary(_comparer);
    }

    #region GetHashCode

    [Benchmark]
    public int HashCode() {
        var sum = 0;

        foreach (var id in _identifiers)
            sum += _comparer.GetHashCode(id);

        return sum;
    }

    #endregion

    #region Dictionary lookup

    [Benchmark]
    public int DictLookup() {
        var found = 0;

        foreach (var id in _identifiers) {
            if (_dict.GetAlternateLookup<ReadOnlySpan<byte>>().TryGetValue(id, out _))
                found++;
        }

        return found;
    }

    #endregion

    #region FrozenDictionary lookup

    [Benchmark]
    public int FrozenLookup() {
        var found = 0;

        foreach (var id in _identifiers) {
            if (_frozen.GetAlternateLookup<ReadOnlySpan<byte>>().TryGetValue(id, out _))
                found++;
        }

        return found;
    }

    #endregion

    public sealed class ComparerScenario(string label, string[] identifiers) {
        public string[] Identifiers => identifiers;

        public override string ToString() => label;
    }
}
