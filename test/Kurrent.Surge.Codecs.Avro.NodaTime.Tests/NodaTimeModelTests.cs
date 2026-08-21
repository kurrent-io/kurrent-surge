using Avro.IO;
using Kurrent.Surge.Codecs.Avro;

// The generated types take their namespace from the schemas, which are shared with the BCL-model test
// project. They are generated into this assembly, not referenced from that one.
using Kurrent.Surge.Codecs.Avro.Tests;

namespace Kurrent.Surge.Codecs.Avro.NodaTime.Tests;

/// <summary>
/// Round-trips the NodaTime time model and asserts wire parity with Apache.Avro for every temporal
/// logical type, plus <c>duration</c>, which only this model can carry.
/// </summary>
/// <remarks>
/// The generated code converts each NodaTime value to its BCL counterpart and then runs the same
/// conversion the BCL model runs, so parity here and parity in
/// <c>Kurrent.Surge.Codecs.Avro.Tests</c> should never disagree. These assertions are what makes that
/// claim checkable rather than a comment.
/// </remarks>
[Category("Codecs")]
[Timeout(10_000)]
public class NodaTimeModelTests {
    static readonly Faker Faker = new();

    static byte[] EncodeWithApache(ISpecificRecord record) {
        var writer = new SpecificDatumWriter<ISpecificRecord>(record.Schema);

        using var stream = new MemoryStream();
        writer.Write(record, new BinaryEncoder(stream));

        return stream.ToArray();
    }

    /// <remarks>
    /// Apache's Specific writer cannot encode a logical type backed by a fixed — its conversion yields a
    /// <c>GenericFixed</c> and <c>WriteFixed</c> rejects anything that is not a <c>SpecificFixed</c>. The
    /// Generic writer shares the conversion, so it is the same reference for a fixed-backed construct.
    /// </remarks>
    static byte[] EncodeWithApacheGeneric(global::Avro.Schema schema, string field, object value) {
        var record = new global::Avro.Generic.GenericRecord((RecordSchema)schema);

        record.Add(field, value);

        var writer = new global::Avro.Generic.GenericDatumWriter<global::Avro.Generic.GenericRecord>(schema);

        using var stream = new MemoryStream();
        writer.Write(record, new BinaryEncoder(stream));

        return stream.ToArray();
    }

    static LogicalSample NewLogicalSample() => new() {
        Id      = Faker.Random.Guid(),
        // Fully qualified: our AvroDecimal and Apache's are both in scope, and generated properties
        // still carry Apache's until the emitter is switched over.
        Amount  = new global::Avro.AvroDecimal(decimal.Round(Faker.Random.Decimal(1, 9999), 2)),
        Day     = new LocalDate(2026, 8, 19),
        TimeMs  = LocalTime.FromSecondsSinceMidnight(Faker.Random.Int(0, 86_399)),
        StampMs = Instant.FromUtc(2026, 8, 19, 12, 34, 56),
        StampUs = Instant.FromUtc(2026, 8, 19, 12, 34, 56)
    };

    #region ->> Temporal types <<-

    [Test]
    public async ValueTask round_trips_temporal_types() {
        // Arrange
        var sut      = new AvroSchemaCodec();
        var expected = NewLogicalSample();

        // Act
        var actual = (LogicalSample)sut.Decode(sut.Encode(expected), typeof(LogicalSample));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask encodes_temporal_types_identically_to_apache() {
        // Arrange — the assertion that makes the time model a choice of surface type rather than a
        // second encoding: the same schema must produce the same bytes under either model.
        var expected = NewLogicalSample();
        var wire     = EncodeWithApache(expected);

        // Act
        var actual = new AvroSchemaCodec().Encode(expected).ToArray();

        // Assert
        await Assert.That(actual).IsEquivalentTo(wire);
    }

    [Test]
    public async ValueTask round_trips_local_timestamps_as_zone_free_readings() {
        // Arrange — LocalDateTime carries no zone, which is the whole point of the local- variants.
        var sut = new AvroSchemaCodec();

        var expected = new LocalTimestampSample {
            LocalMs = new LocalDateTime(2026, 8, 19, 12, 34, 56),
            LocalUs = new LocalDateTime(2026, 8, 19, 12, 34, 56)
        };

        // Act
        var actual = (LocalTimestampSample)sut.Decode(sut.Encode(expected), typeof(LocalTimestampSample));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask round_trips_nanosecond_timestamps_without_truncating() {
        // Arrange — the reason Instant is used here at all: DateTime resolves to 100ns and would drop
        // the last two digits of this value.
        var sut      = new AvroSchemaCodec();
        var expected = new NanoSample { Precise = NodaConstants.UnixEpoch.PlusNanoseconds(1_700_000_000_123_456_789L) };

        // Act
        var actual = (NanoSample)sut.Decode(sut.Encode(expected), typeof(NanoSample));

        // Assert
        await Assert.That(actual.Precise).IsEqualTo(expected.Precise);
    }

    [Test]
    public async ValueTask round_trips_a_pre_epoch_nanosecond_timestamp() {
        // Arrange — the read floors rather than truncating, so a negative count keeps a non-negative
        // nano-of-second. Truncation toward zero would land this one second out.
        var sut      = new AvroSchemaCodec();
        var expected = new NanoSample { Precise = NodaConstants.UnixEpoch.PlusNanoseconds(-1_500_000_000L) };

        // Act
        var actual = (NanoSample)sut.Decode(sut.Encode(expected), typeof(NanoSample));

        // Assert
        await Assert.That(actual.Precise).IsEqualTo(expected.Precise);
    }

    #endregion // Temporal types

    #region ->> Duration <<-

    [Test]
    public async ValueTask round_trips_a_duration() {
        // Arrange — months, days and milliseconds, the three components the format stores.
        var sut = new AvroSchemaCodec();

        var expected = new DurationSample {
            Span = new PeriodBuilder { Months = 14, Days = 3, Milliseconds = 5_000 }.Build()
        };

        // Act
        var actual = (DurationSample)sut.Decode(sut.Encode(expected), typeof(DurationSample));

        // Assert — normalised on the way out, so the comparison is against the stored triple.
        await Assert.That(actual.Span.Months).IsEqualTo(14);
        await Assert.That(actual.Span.Days).IsEqualTo(3);
        await Assert.That(actual.Span.Milliseconds).IsEqualTo(5_000);
    }

    [Test]
    public async ValueTask folds_the_units_a_duration_cannot_store_into_the_ones_it_can() {
        // Arrange — years, weeks, hours, minutes and seconds all convert exactly.
        var sut = new AvroSchemaCodec();

        var expected = new DurationSample {
            Span = new PeriodBuilder { Years = 1, Months = 2, Weeks = 1, Days = 3, Hours = 1, Seconds = 30 }.Build()
        };

        // Act
        var actual = (DurationSample)sut.Decode(sut.Encode(expected), typeof(DurationSample));

        // Assert — 1y2m = 14 months, 1w3d = 10 days, 1h30s = 3,630,000 ms.
        await Assert.That(actual.Span.Months).IsEqualTo(14);
        await Assert.That(actual.Span.Days).IsEqualTo(10);
        await Assert.That(actual.Span.Milliseconds).IsEqualTo(3_630_000);
    }

    [Test]
    public async ValueTask encodes_a_duration_exactly_as_the_specification_describes() {
        // Arrange — asserted against the Avro specification rather than against Apache. Apache models no
        // 'duration' logical type in C#: it resolves to UnknownLogicalType over a fixed and its writers
        // demand a GenericFixed, which a generated type cannot supply without reading
        // ISpecificRecord.Schema — and that roots Avro.Schema.Parse, and Newtonsoft with it.
        //
        // The specification says: fixed(12) holding three little-endian unsigned 32-bit values, in the
        // order months, days, milliseconds. A fixed carries no length prefix, so this is the whole body.
        var value = new DurationSample {
            Span = new PeriodBuilder { Months = 14, Days = 3, Milliseconds = 5_000 }.Build()
        };

        byte[] expected = [
            0x0E, 0x00, 0x00, 0x00,  // months       14
            0x03, 0x00, 0x00, 0x00,  // days          3
            0x88, 0x13, 0x00, 0x00   // milliseconds  5,000
        ];

        // Act
        var actual = new AvroSchemaCodec().Encode(value).ToArray();

        // Assert
        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    public async ValueTask rejects_a_period_carrying_sub_millisecond_precision() {
        // Arrange — the format resolves to milliseconds. Silently dropping the remainder would put a
        // value on the wire that is not the one the caller wrote.
        var sut = new AvroSchemaCodec();

        var value = new DurationSample {
            Span = new PeriodBuilder { Months = 1, Nanoseconds = 500 }.Build()
        };

        // Act & Assert
        await Assert.That(() => sut.Encode(value)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async ValueTask rejects_a_negative_period() {
        // Arrange — the three components are unsigned on the wire.
        var sut = new AvroSchemaCodec();

        var value = new DurationSample {
            Span = new PeriodBuilder { Days = -1 }.Build()
        };

        // Act & Assert
        await Assert.That(() => sut.Encode(value)).Throws<ArgumentOutOfRangeException>();
    }

    #endregion // Duration

    #region ->> NanoInstant bridge <<-

    [Test]
    public async ValueTask converts_between_nano_instant_and_instant_without_loss() {
        // Arrange — both are nanosecond-precision points on the global timeline, so a value survives the
        // round trip exactly. Going through DateTime instead would truncate to 100ns.
        var expected = new NanoInstant(1_700_000_000_123_456_789L);

        // Act
        var actual = expected.ToInstant().ToNanoInstant();

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    #endregion // NanoInstant bridge
}
