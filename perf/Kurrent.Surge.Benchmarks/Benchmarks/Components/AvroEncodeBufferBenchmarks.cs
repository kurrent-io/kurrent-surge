using System.Numerics;
using BenchmarkDotNet.Attributes;
using Kurrent.Surge.Codecs.Avro.Binary;
using Benchmarks.Avro;
using Kurrent.Surge.Codecs.Avro.Generated;

namespace Benchmarks;

/// <summary>
/// Where the bytes of a message are staged: a fresh encoder per message against a reused one, and the
/// three ways a fixed-width value can reach the encoder.
/// </summary>
/// <remarks>
/// The encoder owns a growable buffer, so a fresh one per message allocates that buffer and throws it
/// away once the payload has been copied out. <c>AvroSchemaCodec</c> keeps one per thread instead, and
/// the first pair measures whether resetting costs less than allocating.
/// <para>
/// Comparing separate runs proved unreliable on a machine under sustained benchmark load, which is why
/// every arm sits in one run.
/// </para>
/// </remarks>
[MemoryDiagnoser(displayGenColumns: false), Config(typeof(CodecBenchmarkConfig))]
[BenchmarkCategory("Codecs")]
public class AvroEncodeBufferBenchmarks {
    BenchFlatOrder    _flat    = null!;
    BenchComplexOrder _complex = null!;
    AvroBinaryEncoder _encoder = null!;
    Guid              _id;
    BigInteger        _unscaled;

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

        _encoder = new AvroBinaryEncoder();

        _id       = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");
        _unscaled = new BigInteger(123456789012345678L);
    }

    #region ->> Fresh encoder against a reused one <<-

    [Benchmark(Baseline = true), BenchmarkCategory("BufferFlat")]
    public int Flat_FreshEncoder() {
        var encoder = new AvroBinaryEncoder();

        Benchmarks_Avro_BenchFlatOrderAvroCodec.Encode(_flat, encoder);

        return encoder.WrittenCount;
    }

    [Benchmark, BenchmarkCategory("BufferFlat")]
    public int Flat_ReusedEncoder() {
        _encoder.Reset();

        Benchmarks_Avro_BenchFlatOrderAvroCodec.Encode(_flat, _encoder);

        return _encoder.WrittenCount;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("BufferComplex")]
    public int Complex_FreshEncoder() {
        var encoder = new AvroBinaryEncoder();

        Benchmarks_Avro_BenchComplexOrderAvroCodec.Encode(_complex, encoder);

        return encoder.WrittenCount;
    }

    [Benchmark, BenchmarkCategory("BufferComplex")]
    public int Complex_ReusedEncoder() {
        _encoder.Reset();

        Benchmarks_Avro_BenchComplexOrderAvroCodec.Encode(_complex, _encoder);

        return _encoder.WrittenCount;
    }

    #endregion // Fresh encoder against a reused one

    #region ->> Fixed size: uuid on a fixed(16) <<-

    // Three ways to get sixteen big-endian bytes into the output. A fourth, a thread-static scratch
    // array, was measured at 4.876 ns against this baseline's 3.207 — last of the four, because the
    // thread-local lookup costs more than the array it saves. It is gone; these are what remain.

    [Benchmark(Baseline = true), BenchmarkCategory("UuidBuffer")]
    public int Uuid_FreshArray() {
        _encoder.Reset();

        _encoder.WriteFixed(_id.ToByteArray(bigEndian: true));

        return _encoder.WrittenCount;
    }

    /// <remarks>
    /// The caveat this arm cannot show: emitted inline, a fixed field inside an array loop would
    /// <c>stackalloc</c> per iteration and grow the stack. A method that returns releases it instead,
    /// which is the other reason the winning arm is a method on the encoder.
    /// </remarks>
    [Benchmark, BenchmarkCategory("UuidBuffer")]
    public int Uuid_StackBuffer() {
        _encoder.Reset();

        Span<byte> buffer = stackalloc byte[16];

        _id.TryWriteBytes(buffer, bigEndian: true, out _);
        _encoder.WriteFixed(buffer);

        return _encoder.WrittenCount;
    }

    /// <remarks>
    /// No intermediate buffer at all — the value is written straight into the output. Only reachable
    /// because the encoder owns its buffer rather than a stream.
    /// </remarks>
    [Benchmark, BenchmarkCategory("UuidBuffer")]
    public int Uuid_InPlace() {
        _encoder.Reset();

        _encoder.WriteUuid(_id);

        return _encoder.WrittenCount;
    }

    #endregion // Fixed size

    #region ->> Runtime-variable size: decimal on bytes <<-

    // The size comes from BigInteger.GetByteCount(), so the stack arm is a variable stackalloc rather
    // than a constant one. That is a different question from the fixed-size sites and was measured
    // separately — with the same answer. The thread-static scratch arm ran at 17.164 ns against this
    // baseline's 16.577, last again, so the runtime-variable size did not rescue it either.

    [Benchmark(Baseline = true), BenchmarkCategory("DecimalBuffer")]
    public int Decimal_VariableStack() {
        _encoder.Reset();

        var count = _unscaled.GetByteCount();

        Span<byte> span = stackalloc byte[count];

        _unscaled.TryWriteBytes(span, out _);
        span.Reverse();

        _encoder.WriteBytes(span);

        return _encoder.WrittenCount;
    }

    /// <remarks>Assembled directly in the output: length prefix, then the unscaled value reversed in place.</remarks>
    [Benchmark, BenchmarkCategory("DecimalBuffer")]
    public int Decimal_InPlace() {
        _encoder.Reset();

        var count = _unscaled.GetByteCount();

        _unscaled.TryWriteBytes(_encoder.WriteBytesSpan(count), out _, isUnsigned: false, isBigEndian: true);

        return _encoder.WrittenCount;
    }

    #endregion // Runtime-variable size
}
