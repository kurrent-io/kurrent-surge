using Avro.IO;
using Avro.Specific;
using BenchmarkDotNet.Attributes;
using Benchmarks.Avro;
using Kurrent.Surge.Codecs.Avro;

namespace Benchmarks;

/// <summary>
/// Generated Avro encode/decode against Apache.Avro's <see cref="SpecificDatumWriter{T}"/> and
/// <see cref="SpecificDatumReader{T}"/>, which the generated codec replaced.
/// </summary>
/// <remarks>
/// The replacement was made for Native AOT, not for speed: Apache's readers resolve types at runtime
/// through <c>ObjectCreator</c>, which needs a JIT. These measure what that change cost or bought, on a
/// flat record and on one with a nested record, an array, a map, an enum and a nullable union — the
/// shapes where the two implementations differ most.
///
/// Both arms build their encoder/decoder per invocation because that is exactly what
/// <see cref="AvroSchemaCodec"/> does per message; hoisting it into setup would measure a call pattern
/// the SDK never uses. Apache's reader/writer pair is hoisted, since it caches per type in the real
/// Apache path too.
/// </remarks>
[MemoryDiagnoser(displayGenColumns: false), Config(typeof(ComponentBenchmarkConfig))]
[BenchmarkCategory("Codecs")]
public class AvroCodecBenchmarks {
    AvroSchemaCodec _codec = null!;

    BenchFlatOrder    _flat    = null!;
    BenchComplexOrder _complex = null!;

    byte[] _flatWire    = null!;
    byte[] _complexWire = null!;

    SpecificDatumWriter<ISpecificRecord> _flatApacheWriter    = null!;
    SpecificDatumReader<ISpecificRecord> _flatApacheReader    = null!;
    SpecificDatumWriter<ISpecificRecord> _complexApacheWriter = null!;
    SpecificDatumReader<ISpecificRecord> _complexApacheReader = null!;

    [GlobalSetup]
    public void Setup() {
        _codec = new AvroSchemaCodec();

        _flat = new BenchFlatOrder { OrderId = "ORD-1", Sku = "SKU-9", Quantity = 7 };

        _complex = new BenchComplexOrder {
            OrderId  = "ORD-2",
            Customer = new Customer { Name = "Ripley", Age = 32 },
            Lines    = [new OrderLine { Sku = "SKU-A", Quantity = 2 }, new OrderLine { Sku = "SKU-B", Quantity = 7 }],
            Counters = new Dictionary<string, long> { ["hypersleep"] = 57L, ["decks"] = 3L },
            Status   = OrderStatus.Shipped,
            Note     = "handle with care"
        };

        var flatSchema    = ((ISpecificRecord)_flat).Schema;
        var complexSchema = ((ISpecificRecord)_complex).Schema;

        _flatApacheWriter    = new SpecificDatumWriter<ISpecificRecord>(flatSchema);
        _flatApacheReader    = new SpecificDatumReader<ISpecificRecord>(flatSchema, flatSchema);
        _complexApacheWriter = new SpecificDatumWriter<ISpecificRecord>(complexSchema);
        _complexApacheReader = new SpecificDatumReader<ISpecificRecord>(complexSchema, complexSchema);

        _flatWire    = _codec.Encode(_flat).ToArray();
        _complexWire = _codec.Encode(_complex).ToArray();
    }

    static byte[] EncodeWithApache(ISpecificRecord record, SpecificDatumWriter<ISpecificRecord> writer) {
        using var stream = new MemoryStream();
        writer.Write(record, new BinaryEncoder(stream));
        return stream.ToArray();
    }

    /// <remarks>
    /// The reuse instance comes from <see cref="Activator"/> because that is what the codec did before
    /// the generated path replaced it — generated records declare <c>required</c> members, so there is no
    /// other way to construct an empty one. That call is part of the old path's real cost and stays in
    /// the measurement. It is also the exact line that made this codec fail under Native AOT.
    /// </remarks>
    static ISpecificRecord DecodeWithApache(byte[] wire, SpecificDatumReader<ISpecificRecord> reader, Type recordType) {
        var reuse = (ISpecificRecord)Activator.CreateInstance(recordType)!;

        using var stream = new MemoryStream(wire, writable: false);
        return reader.Read(reuse, new BinaryDecoder(stream));
    }

    #region ->> Flat record <<-

    [Benchmark(Baseline = true), BenchmarkCategory("EncodeFlat")]
    public int EncodeFlat_Generated() => _codec.Encode(_flat).Length;

    [Benchmark, BenchmarkCategory("EncodeFlat")]
    public int EncodeFlat_Apache() => EncodeWithApache(_flat, _flatApacheWriter).Length;

    [Benchmark, BenchmarkCategory("DecodeFlat")]
    public object DecodeFlat_Generated() => _codec.Decode(_flatWire, typeof(BenchFlatOrder));

    [Benchmark, BenchmarkCategory("DecodeFlat")]
    public object DecodeFlat_Apache() => DecodeWithApache(_flatWire, _flatApacheReader, typeof(BenchFlatOrder));

    #endregion // Flat record

    #region ->> Nested record, array, map, enum, union <<-

    [Benchmark, BenchmarkCategory("EncodeComplex")]
    public int EncodeComplex_Generated() => _codec.Encode(_complex).Length;

    [Benchmark, BenchmarkCategory("EncodeComplex")]
    public int EncodeComplex_Apache() => EncodeWithApache(_complex, _complexApacheWriter).Length;

    [Benchmark, BenchmarkCategory("DecodeComplex")]
    public object DecodeComplex_Generated() => _codec.Decode(_complexWire, typeof(BenchComplexOrder));

    [Benchmark, BenchmarkCategory("DecodeComplex")]
    public object DecodeComplex_Apache() => DecodeWithApache(_complexWire, _complexApacheReader, typeof(BenchComplexOrder));

    #endregion // Nested record, array, map, enum, union
}
