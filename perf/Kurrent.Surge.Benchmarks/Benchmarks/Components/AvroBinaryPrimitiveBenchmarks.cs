using Avro.IO;
using BenchmarkDotNet.Attributes;
using Kurrent.Surge.Codecs.Avro.Binary;

namespace Benchmarks;

/// <summary>
/// Our binary primitives against Apache.Avro's, which they are byte-for-byte identical to.
/// </summary>
/// <remarks>
/// Same operations, same values, same run — the only difference is which implementation writes. These
/// are the primitives every generated codec is built from, so a gain here is a gain on every field of
/// every message.
/// <para>
/// Both arms reuse their destination, reset per invocation, so the measurement is the encoding rather
/// than the buffer growth underneath it. Ours writes into a buffer it owns and Apache's needs a
/// <see cref="MemoryStream"/>; that its encoder cannot write anywhere else is part of what is compared,
/// exactly as on the read side.
/// </para>
/// </remarks>
[MemoryDiagnoser(displayGenColumns: false), Config(typeof(CodecBenchmarkConfig))]
[BenchmarkCategory("Codecs")]
public class AvroBinaryPrimitiveBenchmarks {
    MemoryStream _apacheStream = null!;
    string       _text         = null!;

    AvroBinaryEncoder _ours   = null!;
    BinaryEncoder     _apache = null!;

    // Ours reads memory directly; Apache's needs a Stream. Both reuse their reader and their payload, so
    // the measurement is the decoding rather than the scaffolding — a fresh MemoryStream plus a fresh
    // decoder is about 88 bytes, which swamps a twenty-nanosecond operation. That the Apache arm needs a
    // stream at all is part of what is being compared.
    byte[] _longWire   = null!;
    byte[] _stringWire = null!;

    MemoryStream _apacheLongStream   = null!;
    MemoryStream _apacheStringStream = null!;

    AvroBinaryDecoder _oursLong     = null!;
    BinaryDecoder     _apacheLong   = null!;
    AvroBinaryDecoder _oursString   = null!;
    BinaryDecoder     _apacheString = null!;

    [GlobalSetup]
    public void Setup() {
        _apacheStream = new MemoryStream(1024);
        _ours         = new AvroBinaryEncoder(1024);
        _apache       = new BinaryEncoder(_apacheStream);

        // Long enough to be a realistic field, short enough to stay on the encoder's stack path.
        _text = "Nostromo commercial towing vehicle, crew of seven";

        // One payload per read benchmark, so reading a string does not first pay for reading a long.
        _longWire   = Encode(e => e.WriteLong(long.MaxValue));
        _stringWire = Encode(e => e.WriteString(_text));

        _oursLong   = new AvroBinaryDecoder(_longWire);
        _oursString = new AvroBinaryDecoder(_stringWire);

        _apacheLongStream   = new MemoryStream(_longWire, writable: false);
        _apacheStringStream = new MemoryStream(_stringWire, writable: false);
        _apacheLong         = new BinaryDecoder(_apacheLongStream);
        _apacheString       = new BinaryDecoder(_apacheStringStream);
    }

    static byte[] Encode(Action<BinaryEncoder> write) {
        using var stream = new MemoryStream();

        write(new BinaryEncoder(stream));

        return stream.ToArray();
    }

    MemoryStream ResetApache() {
        _apacheStream.SetLength(0);

        return _apacheStream;
    }

    #region ->> Varint — the hottest operation in the format <<-

    /// <remarks>
    /// Every int, long, length prefix, union index and block count routes through this. Ten bytes is the
    /// worst case, which is where Apache's byte-at-a-time loop costs the most.
    /// </remarks>
    [Benchmark(Baseline = true), BenchmarkCategory("WriteLong")]
    public long WriteLong_Ours() {
        _ours.Reset();

        _ours.WriteLong(long.MaxValue);

        return _ours.WrittenCount;
    }

    [Benchmark, BenchmarkCategory("WriteLong")]
    public long WriteLong_Apache() {
        var stream = ResetApache();

        _apache.WriteLong(long.MaxValue);

        return stream.Length;
    }

    #endregion // Varint

    #region ->> String — the most common field type <<-

    [Benchmark(Baseline = true), BenchmarkCategory("WriteString")]
    public long WriteString_Ours() {
        _ours.Reset();

        _ours.WriteString(_text);

        return _ours.WrittenCount;
    }

    [Benchmark, BenchmarkCategory("WriteString")]
    public long WriteString_Apache() {
        var stream = ResetApache();

        _apache.WriteString(_text);

        return stream.Length;
    }

    #endregion // String

    #region ->> Float and double <<-

    /// <remarks>Apache allocates a <c>byte[4]</c> per float through <c>BitConverter.GetBytes</c>.</remarks>
    [Benchmark(Baseline = true), BenchmarkCategory("WriteFloat")]
    public long WriteFloat_Ours() {
        _ours.Reset();

        _ours.WriteFloat(float.Pi);

        return _ours.WrittenCount;
    }

    [Benchmark, BenchmarkCategory("WriteFloat")]
    public long WriteFloat_Apache() {
        var stream = ResetApache();

        _apache.WriteFloat(float.Pi);

        return stream.Length;
    }

    #endregion // Float and double

    #region ->> Read side <<-

    [Benchmark(Baseline = true), BenchmarkCategory("ReadLong")]
    public long ReadLong_Ours() {
        _oursLong.Reset();

        return _oursLong.ReadLong();
    }

    [Benchmark, BenchmarkCategory("ReadLong")]
    public long ReadLong_Apache() {
        _apacheLongStream.Position = 0;

        return _apacheLong.ReadLong();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("ReadString")]
    public string ReadString_Ours() {
        _oursString.Reset();

        return _oursString.ReadString();
    }

    [Benchmark, BenchmarkCategory("ReadString")]
    public string ReadString_Apache() {
        _apacheStringStream.Position = 0;

        return _apacheString.ReadString();
    }

    #endregion // Read side
}
