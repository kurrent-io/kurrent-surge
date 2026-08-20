using System.Globalization;
using Avro.IO;
using Avro.Specific;
using BenchmarkDotNet.Attributes;
using Benchmarks.Avro;
using Kurrent.Surge.Codecs.Avro;

namespace Benchmarks;

/// <summary>
/// Logical types, which the flat and complex record benchmarks never touch.
/// </summary>
/// <remarks>
/// Each logical type converts on the way in and out, and those conversions are where a codec does real
/// work beyond moving bytes. Four shapes are covered: the primitive-backed set, a nanosecond timestamp,
/// and the two fixed-backed variants.
///
/// The fixed-backed cases have no Apache arm. Apache's own Specific writer cannot encode a logical type
/// backed by a fixed — its conversion yields a GenericFixed and SpecificDatumWriter.WriteFixed rejects
/// anything else — so there is nothing to compare against on that path.
/// </remarks>
[MemoryDiagnoser(displayGenColumns: false), Config(typeof(ComponentBenchmarkConfig))]
[BenchmarkCategory("Codecs")]
public class AvroLogicalTypeBenchmarks {
    AvroSchemaCodec _codec = null!;

    BenchLogicalSample _logical = null!;
    BenchNanoSample    _nano    = null!;
    BenchDecimalFixed  _decimal = null!;
    BenchUuidFixed     _uuid    = null!;

    byte[] _logicalWire = null!;
    byte[] _nanoWire    = null!;
    byte[] _decimalWire = null!;
    byte[] _uuidWire    = null!;

    SpecificDatumWriter<ISpecificRecord> _logicalWriter = null!;
    SpecificDatumReader<ISpecificRecord> _logicalReader = null!;
    SpecificDatumWriter<ISpecificRecord> _nanoWriter    = null!;
    SpecificDatumReader<ISpecificRecord> _nanoReader    = null!;

    [GlobalSetup]
    public void Setup() {
        _codec = new AvroSchemaCodec();

        var moment = new DateTime(2026, 8, 19, 12, 34, 56, DateTimeKind.Utc);

        _logical = new BenchLogicalSample {
            Id      = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
            Amount  = new global::Avro.AvroDecimal(decimal.Parse("1234.56", CultureInfo.InvariantCulture)),
            Day     = moment.Date,
            TimeMs  = moment.TimeOfDay,
            StampMs = moment,
            StampUs = moment
        };

        _nano    = new BenchNanoSample { Precise = new NanoInstant(1_700_000_000_123_456_789L) };
        _decimal = new BenchDecimalFixed { Amount = new global::Avro.AvroDecimal(decimal.Parse("-1234.56", CultureInfo.InvariantCulture)) };
        _uuid    = new BenchUuidFixed { Id = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff") };

        _logicalWire = _codec.Encode(_logical).ToArray();
        _nanoWire    = _codec.Encode(_nano).ToArray();
        _decimalWire = _codec.Encode(_decimal).ToArray();
        _uuidWire    = _codec.Encode(_uuid).ToArray();

        var logicalSchema = ((ISpecificRecord)_logical).Schema;
        var nanoSchema    = ((ISpecificRecord)_nano).Schema;

        _logicalWriter = new SpecificDatumWriter<ISpecificRecord>(logicalSchema);
        _logicalReader = new SpecificDatumReader<ISpecificRecord>(logicalSchema, logicalSchema);
        _nanoWriter    = new SpecificDatumWriter<ISpecificRecord>(nanoSchema);
        _nanoReader    = new SpecificDatumReader<ISpecificRecord>(nanoSchema, nanoSchema);
    }

    static byte[] EncodeWithApache(ISpecificRecord record, SpecificDatumWriter<ISpecificRecord> writer) {
        using var stream = new MemoryStream();

        writer.Write(record, new BinaryEncoder(stream));

        return stream.ToArray();
    }

    static ISpecificRecord DecodeWithApache(byte[] wire, SpecificDatumReader<ISpecificRecord> reader, Type recordType) {
        var reuse = (ISpecificRecord)Activator.CreateInstance(recordType)!;

        using var stream = new MemoryStream(wire, writable: false);

        return reader.Read(reuse, new BinaryDecoder(stream));
    }

    #region ->> Primitive-backed logical types <<-

    [Benchmark(Baseline = true), BenchmarkCategory("EncodeLogical")]
    public int EncodeLogical_Generated() => _codec.Encode(_logical).Length;

    [Benchmark, BenchmarkCategory("EncodeLogical")]
    public int EncodeLogical_Apache() => EncodeWithApache(_logical, _logicalWriter).Length;

    [Benchmark, BenchmarkCategory("DecodeLogical")]
    public object DecodeLogical_Generated() => _codec.Decode(_logicalWire, typeof(BenchLogicalSample));

    [Benchmark, BenchmarkCategory("DecodeLogical")]
    public object DecodeLogical_Apache() => DecodeWithApache(_logicalWire, _logicalReader, typeof(BenchLogicalSample));

    #endregion // Primitive-backed logical types

    #region ->> Nanosecond timestamps <<-

    [Benchmark, BenchmarkCategory("EncodeNano")]
    public int EncodeNano_Generated() => _codec.Encode(_nano).Length;

    [Benchmark, BenchmarkCategory("EncodeNano")]
    public int EncodeNano_Apache() => EncodeWithApache(_nano, _nanoWriter).Length;

    [Benchmark, BenchmarkCategory("DecodeNano")]
    public object DecodeNano_Generated() => _codec.Decode(_nanoWire, typeof(BenchNanoSample));

    [Benchmark, BenchmarkCategory("DecodeNano")]
    public object DecodeNano_Apache() => DecodeWithApache(_nanoWire, _nanoReader, typeof(BenchNanoSample));

    #endregion // Nanosecond timestamps

    #region ->> Fixed-backed logical types <<-

    [Benchmark, BenchmarkCategory("FixedLogical")]
    public int EncodeDecimalFixed_Generated() => _codec.Encode(_decimal).Length;

    [Benchmark, BenchmarkCategory("FixedLogical")]
    public object DecodeDecimalFixed_Generated() => _codec.Decode(_decimalWire, typeof(BenchDecimalFixed));

    [Benchmark, BenchmarkCategory("FixedLogical")]
    public int EncodeUuidFixed_Generated() => _codec.Encode(_uuid).Length;

    [Benchmark, BenchmarkCategory("FixedLogical")]
    public object DecodeUuidFixed_Generated() => _codec.Decode(_uuidWire, typeof(BenchUuidFixed));

    #endregion // Fixed-backed logical types
}
