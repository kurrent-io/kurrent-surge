using Avro.IO;
using BenchmarkDotNet.Attributes;
using Benchmarks.Avro;
using Kurrent.Surge.Codecs.Avro;
using Kurrent.Surge.Codecs.Avro.Generated;

namespace Benchmarks;

/// <summary>
/// The scratch buffer encoding writes through: a fresh <see cref="MemoryStream"/> per message against a
/// reused one.
/// </summary>
/// <remarks>
/// A growable MemoryStream takes a 256-byte buffer on its first write, which is thrown away once the
/// payload has been copied out. Reusing one per thread removes that, and this measures whether resetting
/// costs more than allocating.
///
/// Both arms run the same generated codec over the same record, so the buffer is the only difference.
/// Comparing separate runs proved unreliable on a machine under sustained benchmark load, which is why
/// both arms sit in one run.
/// </remarks>
[MemoryDiagnoser(displayGenColumns: false), Config(typeof(ComponentBenchmarkConfig))]
[BenchmarkCategory("Codecs")]
public class AvroEncodeBufferBenchmarks {
    BenchFlatOrder    _flat    = null!;
    BenchComplexOrder _complex = null!;
    MemoryStream      _reused  = null!;
    Guid              _id;

    [GlobalSetup]
    public void Setup() {
        _flat = new BenchFlatOrder { OrderId = "ORD-1", Sku = "SKU-9", Quantity = 7 };

        _complex = new BenchComplexOrder {
            OrderId  = "ORD-2",
            Customer = new Customer { Name = "Ripley", Age = 32 },
            Lines    = [new OrderLine { Sku = "SKU-A", Quantity = 2 }, new OrderLine { Sku = "SKU-B", Quantity = 7 }],
            Counters = new Dictionary<string, long> { ["hypersleep"] = 57L, ["decks"] = 3L },
            Status   = OrderStatus.Shipped,
            Note     = "handle with care"
        };

        _reused = new MemoryStream(256);
        _id     = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");
    }

    [Benchmark(Baseline = true), BenchmarkCategory("BufferFlat")]
    public int Flat_FreshBuffer() {
        using var stream = new MemoryStream();

        Benchmarks_Avro_BenchFlatOrderAvroCodec.Encode(_flat, new BinaryEncoder(stream));

        return stream.ToArray().Length;
    }

    [Benchmark, BenchmarkCategory("BufferFlat")]
    public int Flat_ReusedBuffer() {
        _reused.SetLength(0);

        Benchmarks_Avro_BenchFlatOrderAvroCodec.Encode(_flat, new BinaryEncoder(_reused));

        return _reused.ToArray().Length;
    }

    [Benchmark, BenchmarkCategory("BufferComplex")]
    public int Complex_FreshBuffer() {
        using var stream = new MemoryStream();

        Benchmarks_Avro_BenchComplexOrderAvroCodec.Encode(_complex, new BinaryEncoder(stream));

        return stream.ToArray().Length;
    }

    [Benchmark, BenchmarkCategory("BufferComplex")]
    public int Complex_ReusedBuffer() {
        _reused.SetLength(0);

        Benchmarks_Avro_BenchComplexOrderAvroCodec.Encode(_complex, new BinaryEncoder(_reused));

        return _reused.ToArray().Length;
    }

    #region ->> Fixed-width scratch buffer <<-

    // Encoder.WriteFixed takes no span, so a uuid backed by a fixed has to reach it as a byte[]. Either a
    // fresh array per field, or one reused through the offset-and-length overload.

    [Benchmark, BenchmarkCategory("UuidBuffer")]
    public int Uuid_FreshArray() {
        _reused.SetLength(0);

        new BinaryEncoder(_reused).WriteFixed(_id.ToByteArray(bigEndian: true));

        return (int)_reused.Length;
    }

    [Benchmark, BenchmarkCategory("UuidBuffer")]
    public int Uuid_RentedBuffer() {
        _reused.SetLength(0);

        var buffer = AvroBuffers.Rent(16);

        _id.TryWriteBytes(buffer, bigEndian: true, out _);
        new BinaryEncoder(_reused).WriteFixed(buffer, 0, 16);

        return (int)_reused.Length;
    }

    #endregion // Fixed-width scratch buffer
}
