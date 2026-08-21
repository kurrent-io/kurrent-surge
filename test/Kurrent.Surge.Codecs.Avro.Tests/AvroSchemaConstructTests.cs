using System.Globalization;
using Avro.IO;

namespace Kurrent.Surge.Codecs.Avro.Tests;

/// <summary>
/// Covers every Avro schema construct the generated codec supports, and asserts wire parity with
/// Apache.Avro for each.
/// </summary>
/// <remarks>
/// Parity is the load-bearing assertion. The generated codec replaces Apache's
/// <c>SpecificDatumReader</c>/<c>Writer</c> to remove their runtime type resolution; if it also changed
/// the bytes, every stream written by one version would be unreadable by the other.
/// </remarks>
[Category("Codecs")]
public class AvroSchemaConstructTests {
    static readonly Faker Faker = new();

    static byte[] EncodeWithApache(ISpecificRecord record) {
        var writer = new SpecificDatumWriter<ISpecificRecord>(record.Schema);

        using var stream = new MemoryStream();
        writer.Write(record, new BinaryEncoder(stream));

        return stream.ToArray();
    }

    /// <summary>Encodes one field through Apache's Generic writer.</summary>
    /// <remarks>
    /// Needed only for a decimal backed by a fixed. Apache's logical conversion produces a
    /// <c>GenericFixed</c>, and <c>SpecificDatumWriter.WriteFixed</c> rejects anything that is not a
    /// <c>SpecificFixed</c>, so the Specific writer cannot encode that construct at all. Both writers
    /// share <c>Avro.Util.Decimal.ConvertToBaseValue</c>, so the Generic one is the same reference.
    /// </remarks>
    static byte[] EncodeWithApacheGeneric(global::Avro.Schema schema, string field, object value) {
        var record = new global::Avro.Generic.GenericRecord((RecordSchema)schema);

        record.Add(field, value);

        var writer = new global::Avro.Generic.GenericDatumWriter<global::Avro.Generic.GenericRecord>(schema);

        using var stream = new MemoryStream();
        writer.Write(record, new BinaryEncoder(stream));

        return stream.ToArray();
    }

    static ComplexOrder NewComplexOrder(string? note) => new() {
        OrderId  = Faker.Random.Guid().ToString(),
        Customer = new Customer { Name = Faker.Name.FullName(), Age = Faker.Random.Int(18, 90) },
        Lines    = [
            new OrderLine { Sku = Faker.Commerce.Ean13(), Quantity = Faker.Random.Int(1, 9) },
            new OrderLine { Sku = Faker.Commerce.Ean13(), Quantity = Faker.Random.Int(1, 9) }
        ],
        Counters = new Dictionary<string, long> { ["a"] = Faker.Random.Long(1, 999), ["b"] = Faker.Random.Long(1, 999) },
        Status   = OrderStatus.Shipped,
        Note     = note
    };

    [Test]
    [Arguments(true)]
    [Arguments(false)] // a null nullable field: the branch that hashes to zero rather than throwing
    public async ValueTask compares_a_decoded_message_equal_to_the_one_that_was_encoded(bool hasNote) {
        // Arrange — a record's synthesized equality compares a list or a dictionary by reference, so this
        // was false for any message holding a collection. The generated Equals compares element by
        // element instead, and == routes through it.
        var sut      = new AvroSchemaCodec();
        var expected = NewComplexOrder(hasNote ? Faker.Lorem.Sentence() : null);

        // Act
        var actual = (ComplexOrder)sut.Decode(sut.Encode(expected), typeof(ComplexOrder));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
        await Assert.That(actual == expected).IsTrue();
        await Assert.That(actual.GetHashCode()).IsEqualTo(expected.GetHashCode());
    }

    [Test]
    public async ValueTask compares_unequal_when_a_collection_element_differs() {
        // Arrange — the counterpart to the test above. Equality that always returns true would satisfy it,
        // so the difference has to sit inside a collection rather than in a scalar.
        var expected = NewComplexOrder(Faker.Lorem.Sentence());

        // Act
        var actual = expected with {
            Lines = [expected.Lines[0], new OrderLine { Sku = Faker.Commerce.Ean13(), Quantity = 99 }]
        };

        // Assert
        await Assert.That(actual).IsNotEqualTo(expected);
        await Assert.That(actual == expected).IsFalse();
    }

    [Test]
    public async ValueTask compares_unequal_when_a_map_value_differs() {
        // Arrange
        var expected = NewComplexOrder(Faker.Lorem.Sentence());

        // Act
        var actual = expected with {
            Counters = expected.Counters.ToDictionary(pair => pair.Key, pair => pair.Value + 1)
        };

        // Assert
        await Assert.That(actual).IsNotEqualTo(expected);
    }

    [Test]
    public async ValueTask compares_a_bytes_field_by_content() {
        // Arrange — byte[] is the other member a record compares by reference.
        var sut      = new AvroSchemaCodec();
        var expected = new BytesSample { Payload = Faker.Random.Bytes(32) };

        // Act
        var actual = (BytesSample)sut.Decode(sut.Encode(expected), typeof(BytesSample));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
        await Assert.That(actual.GetHashCode()).IsEqualTo(expected.GetHashCode());
        await Assert.That(actual with { Payload = Faker.Random.Bytes(32) }).IsNotEqualTo(expected);
    }

    [Test]
    public async ValueTask round_trips_nested_records_arrays_maps_and_enums() {
        // Arrange
        var sut      = new AvroSchemaCodec();
        var expected = NewComplexOrder(Faker.Lorem.Sentence());

        // Act
        var actual = (ComplexOrder)sut.Decode(sut.Encode(expected), typeof(ComplexOrder));

        // Assert
        await Assert.That(actual.Customer.Name).IsEqualTo(expected.Customer.Name);
        await Assert.That(actual.Lines.Select(x => x.Sku)).IsEquivalentTo(expected.Lines.Select(x => x.Sku));
        await Assert.That(actual.Counters).IsEquivalentTo(expected.Counters);
        await Assert.That(actual.Status).IsEqualTo(expected.Status);
        await Assert.That(actual.Note).IsEqualTo(expected.Note);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async ValueTask encodes_nullable_union_identically_to_apache(bool hasNote) {
        // Arrange — both branches of ["null", "string"] must match the reference encoder.
        var expected = NewComplexOrder(hasNote ? Faker.Lorem.Sentence() : null);
        var wire     = EncodeWithApache(expected);

        // Act
        var actual = new AvroSchemaCodec().Encode(expected).ToArray();

        // Assert
        await Assert.That(actual).IsEquivalentTo(wire);
    }

    [Test]
    public async ValueTask round_trips_multi_branch_unions() {
        // Arrange — object, object? and an all-record variant interface in one record.
        var sut = new AvroSchemaCodec();

        var expected = new UnionSample {
            ThreeWay         = Faker.Random.Int(1, 500),
            NullableThreeWay = Faker.Lorem.Word(),
            RecordUnion      = new Dog { Bark = Faker.Lorem.Word() }
        };

        // Act
        var actual = (UnionSample)sut.Decode(sut.Encode(expected), typeof(UnionSample));

        // Assert
        await Assert.That(actual.ThreeWay).IsEqualTo(expected.ThreeWay);
        await Assert.That(actual.NullableThreeWay).IsEqualTo(expected.NullableThreeWay);
        await Assert.That(actual.RecordUnion).IsTypeOf<Dog>();
    }

    [Test]
    public async ValueTask round_trips_the_null_branch_of_a_multi_branch_union() {
        // Arrange
        var sut = new AvroSchemaCodec();

        var expected = new UnionSample {
            ThreeWay         = Faker.Lorem.Word(),
            NullableThreeWay = null,
            RecordUnion      = new Cat { Lives = Faker.Random.Int(1, 9) }
        };

        // Act
        var actual = (UnionSample)sut.Decode(sut.Encode(expected), typeof(UnionSample));

        // Assert
        await Assert.That(actual.NullableThreeWay).IsNull();
        await Assert.That(actual.RecordUnion).IsTypeOf<Cat>();
    }

    [Test]
    public async ValueTask round_trips_logical_types() {
        // Arrange — a fixed scale is required: the schema declares scale 2 and the encoder rejects a
        // mismatch, exactly as Apache does.
        var sut = new AvroSchemaCodec();

        var expected = new LogicalSample {
            Id      = Faker.Random.Guid(),
            Amount  = new global::Avro.AvroDecimal(decimal.Round(Faker.Random.Decimal(1, 9999), 2)),
            Day     = new DateOnly(2026, 8, 19),
            TimeMs  = TimeOnly.FromTimeSpan(TimeSpan.FromMilliseconds(Faker.Random.Int(0, 86_399_000))),
            StampMs = new DateTime(2026, 8, 19, 12, 34, 56, DateTimeKind.Utc),
            StampUs = new DateTime(2026, 8, 19, 12, 34, 56, DateTimeKind.Utc)
        };

        // Act
        var actual = (LogicalSample)sut.Decode(sut.Encode(expected), typeof(LogicalSample));

        // Assert
        await Assert.That(actual.Id).IsEqualTo(expected.Id);
        await Assert.That(actual.Amount).IsEqualTo(expected.Amount);
        await Assert.That(actual.Day).IsEqualTo(expected.Day);
        await Assert.That(actual.TimeMs).IsEqualTo(expected.TimeMs);
        await Assert.That(actual.StampMs).IsEqualTo(expected.StampMs);
        await Assert.That(actual.StampUs).IsEqualTo(expected.StampUs);
    }

    [Test]
    public async ValueTask encodes_logical_types_identically_to_apache() {
        // Arrange — uuid, decimal, date, time and timestamp conversions are the likeliest place for a
        // silent wire divergence, so they are compared against the reference encoder directly.
        var expected = new LogicalSample {
            Id      = Faker.Random.Guid(),
            Amount  = new global::Avro.AvroDecimal(decimal.Round(Faker.Random.Decimal(1, 9999), 2)),
            Day     = new DateOnly(2026, 8, 19),
            TimeMs  = TimeOnly.FromTimeSpan(TimeSpan.FromMilliseconds(Faker.Random.Int(0, 86_399_000))),
            StampMs = new DateTime(2026, 8, 19, 12, 34, 56, DateTimeKind.Utc),
            StampUs = new DateTime(2026, 8, 19, 12, 34, 56, DateTimeKind.Utc)
        };

        var wire = EncodeWithApache(expected);

        // Act
        var actual = new AvroSchemaCodec().Encode(expected).ToArray();

        // Assert
        await Assert.That(actual).IsEquivalentTo(wire);
    }

    [Test]
    [Arguments("1900-03-04T05:06:07.8910000Z")] // pre-epoch: negative tick delta, truncation direction
    [Arguments("1969-12-31T23:59:59.9990000Z")] // one millisecond before the epoch
    [Arguments("1970-01-01T00:00:00.0000000Z")] // the epoch itself
    [Arguments("2026-08-19T12:34:56.7654321Z")] // sub-millisecond ticks, discarded by millis
    [Arguments("9999-12-31T23:59:59.9999999Z")] // DateTime.MaxValue, the double-precision worry
    public async ValueTask encodes_timestamp_edges_identically_to_apache(string timestamp) {
        // Arrange — Apache converts millis through a double and micros through integer division. These
        // are the inputs where the two could disagree, so parity is asserted at the edges rather than
        // argued from the arithmetic.
        var moment = DateTime.Parse(timestamp, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        var expected = new LogicalSample {
            Id      = Faker.Random.Guid(),
            Amount  = new global::Avro.AvroDecimal(decimal.Round(Faker.Random.Decimal(1, 9999), 2)),
            Day     = DateOnly.FromDateTime(moment),
            TimeMs  = TimeOnly.FromTimeSpan(moment.TimeOfDay),
            StampMs = moment,
            StampUs = moment
        };

        var wire = EncodeWithApache(expected);

        // Act
        var actual = new AvroSchemaCodec().Encode(expected).ToArray();

        // Assert
        await Assert.That(actual).IsEquivalentTo(wire);
    }

    [Test]
    public async ValueTask round_trips_fixed_types() {
        // Arrange
        var sut      = new AvroSchemaCodec();
        var expected = new FixedSample { Checksum = new Md5 { Value = Faker.Random.Bytes(16) } };

        // Act
        var actual = (FixedSample)sut.Decode(sut.Encode(expected), typeof(FixedSample));

        // Assert
        await Assert.That(actual.Checksum.Value).IsEquivalentTo(expected.Checksum.Value);
    }

    [Test]
    public async ValueTask encodes_fixed_identically_to_apache() {
        // Arrange
        var expected = new FixedSample { Checksum = new Md5 { Value = Faker.Random.Bytes(16) } };
        var wire     = EncodeWithApache(expected);

        // Act
        var actual = new AvroSchemaCodec().Encode(expected).ToArray();

        // Assert
        await Assert.That(actual).IsEquivalentTo(wire);
    }

    [Test]
    public async ValueTask initialises_a_field_from_its_schema_default() {
        // Arrange — 'Label' declares a default, so it is neither required nor unset. A field without one
        // stays required, which is what forces Checksum to be supplied here.
        var expected = "unnamed";

        // Act
        var actual = new FixedSample { Checksum = new Md5 { Value = Faker.Random.Bytes(16) } };

        // Assert
        await Assert.That(actual.Label).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask initialises_a_fixed_field_from_its_schema_default() {
        // Arrange — a fixed maps to a SpecificFixed subclass, so its default has to construct one rather
        // than assign a byte[]. The schema declares the four bytes as code points 0-255.
        byte[] expected = [0xDE, 0xAD, 0xBE, 0xEF];

        // Act
        var actual = new FixedSample { Checksum = new Md5 { Value = Faker.Random.Bytes(16) } };

        // Assert
        await Assert.That(actual.Salt.Value).IsEquivalentTo(expected);
    }

    [Test]
    public async ValueTask round_trips_local_timestamps() {
        // Arrange — Unspecified is the kind a local timestamp carries: the value is a wall clock reading
        // with no zone attached.
        var sut    = new AvroSchemaCodec();
        var moment = new DateTime(2026, 8, 19, 12, 34, 56, DateTimeKind.Unspecified);

        var expected = new LocalTimestampSample { LocalMs = moment, LocalUs = moment };

        // Act
        var actual = (LocalTimestampSample)sut.Decode(sut.Encode(expected), typeof(LocalTimestampSample));

        // Assert
        await Assert.That(actual.LocalMs).IsEqualTo(expected.LocalMs);
        await Assert.That(actual.LocalUs).IsEqualTo(expected.LocalUs);
    }

    [Test]
    public async ValueTask encodes_local_timestamps_identically_to_apache() {
        // Arrange — Apache converts a local timestamp through ToUniversalTime exactly as it does an
        // absolute one, and differs only on the way back. Parity is asserted rather than argued.
        var moment = new DateTime(2026, 8, 19, 12, 34, 56, DateTimeKind.Unspecified);

        var expected = new LocalTimestampSample { LocalMs = moment, LocalUs = moment };
        var wire     = EncodeWithApache(expected);

        // Act
        var actual = new AvroSchemaCodec().Encode(expected).ToArray();

        // Assert
        await Assert.That(actual).IsEquivalentTo(wire);
    }

    [Test]
    public async ValueTask decodes_local_timestamps_written_by_apache() {
        // Arrange — the read side is where the local variants differ from the absolute ones, so an
        // Apache producer and our consumer have to agree on the same bytes.
        var moment   = new DateTime(2026, 8, 19, 12, 34, 56, DateTimeKind.Unspecified);
        var expected = new LocalTimestampSample { LocalMs = moment, LocalUs = moment };
        var wire     = EncodeWithApache(expected);

        // Act
        var actual = (LocalTimestampSample)new AvroSchemaCodec().Decode(wire, typeof(LocalTimestampSample));

        // Assert
        await Assert.That(actual.LocalMs).IsEqualTo(expected.LocalMs);
        await Assert.That(actual.LocalUs).IsEqualTo(expected.LocalUs);
    }

    [Test]
    public async ValueTask round_trips_a_decimal_backed_by_a_fixed() {
        // Arrange — the schema declares scale 2, and the encoder rejects a mismatch exactly as Apache does.
        var sut      = new AvroSchemaCodec();
        var expected = new DecimalFixedSample { Amount = new global::Avro.AvroDecimal(decimal.Round(Faker.Random.Decimal(1, 9999), 2)) };

        // Act
        var actual = (DecimalFixedSample)sut.Decode(sut.Encode(expected), typeof(DecimalFixedSample));

        // Assert
        await Assert.That(actual.Amount).IsEqualTo(expected.Amount);
    }

    [Test]
    [Arguments("1234.56")]  // positive: left-padded with 0x00
    [Arguments("-1234.56")] // negative: sign-extended with 0xFF, the branch a positive value never reaches
    [Arguments("0.00")]
    [Arguments("-0.01")]    // every byte is padding
    public async ValueTask encodes_a_fixed_decimal_identically_to_apache(string amount) {
        // Arrange — the unscaled value is shorter than the fixed, so the padding byte is what differs
        // between a positive and a negative value. Both are asserted against the reference encoder.
        var expected = new DecimalFixedSample { Amount = new global::Avro.AvroDecimal(decimal.Parse(amount, CultureInfo.InvariantCulture)) };
        var wire     = EncodeWithApacheGeneric(((ISpecificRecord)expected).Schema, "Amount", expected.Amount);

        // Act
        var actual = new AvroSchemaCodec().Encode(expected).ToArray();

        // Assert
        await Assert.That(actual).IsEquivalentTo(wire);
    }

    [Test]
    public async ValueTask apache_specific_writer_cannot_encode_a_fixed_decimal() {
        // Arrange — pins why the test above uses the Generic writer as its reference. Apache's decimal
        // conversion returns a GenericFixed and SpecificDatumWriter.WriteFixed demands a SpecificFixed,
        // so the Specific path cannot carry this construct at all in 1.12.2. If a later Avro fixes that,
        // this test fails and the reference can move back.
        var sut = new DecimalFixedSample { Amount = new global::Avro.AvroDecimal(1234.56m) };

        // Act & Assert
        await Assert.That(() => EncodeWithApache(sut)).Throws<AvroTypeException>();
    }

    [Test]
    public async ValueTask round_trips_logical_types_it_does_not_model() {
        // Arrange — 'local-timestamp-nanos' is in the specification and 'varchar' is not, and Apache.Avro
        // implements neither. Both must degrade to the underlying primitive rather than fail.
        var sut = new AvroSchemaCodec();

        var expected = new UnknownLogicalSample {
            Nanos    = Faker.Random.Long(0, 1_700_000_000_000_000_000),
            Invented = Faker.Lorem.Word()
        };

        // Act
        var actual = (UnknownLogicalSample)sut.Decode(sut.Encode(expected), typeof(UnknownLogicalSample));

        // Assert
        await Assert.That(actual.Nanos).IsEqualTo(expected.Nanos);
        await Assert.That(actual.Invented).IsEqualTo(expected.Invented);
    }

    [Test]
    public async ValueTask encodes_logical_types_it_does_not_model_identically_to_apache() {
        // Arrange — Apache.Avro 1.12.2 resolves an unrecognised logical type to UnknownLogicalType, whose
        // conversion is the identity. That is the same policy we apply at compile time, so the two have to
        // agree on the bytes. Before 1.12.2 this schema could not be parsed at all.
        var expected = new UnknownLogicalSample {
            Nanos    = Faker.Random.Long(0, 1_700_000_000_000_000_000),
            Invented = Faker.Lorem.Word()
        };

        var wire = EncodeWithApache(expected);

        // Act
        var actual = new AvroSchemaCodec().Encode(expected).ToArray();

        // Assert
        await Assert.That(actual).IsEquivalentTo(wire);
    }

    [Test]
    [Arguments(1_700_000_000_123_456_789L)]
    [Arguments(0L)]
    [Arguments(-1L)]                          // pre-epoch by one nanosecond: the floor-division case
    [Arguments(-6_795_364_578_871_345_152L)]  // deep pre-epoch
    public async ValueTask round_trips_nanosecond_timestamps(long nanoseconds) {
        // Arrange — DateTime resolves to 100ns, so this is the precision no BCL type carries.
        var sut      = new AvroSchemaCodec();
        var expected = new NanoSample { Precise = new NanoInstant(nanoseconds) };

        // Act
        var actual = (NanoSample)sut.Decode(sut.Encode(expected), typeof(NanoSample));

        // Assert
        await Assert.That(actual.Precise).IsEqualTo(expected.Precise);
        await Assert.That(actual.Precise.TotalNanoseconds).IsEqualTo(nanoseconds);
    }

    [Test]
    public async ValueTask encodes_nanosecond_timestamps_identically_to_apache() {
        // Arrange — Apache does not model timestamp-nanos; it sees UnknownLogicalType over a long and
        // demands an Int64. The property is a NanoInstant, so ISpecificRecord.Get converts at the
        // boundary and Apache's own writer stays usable.
        var expected = new NanoSample { Precise = new NanoInstant(1_700_000_000_123_456_789L) };
        var wire     = EncodeWithApache(expected);

        // Act
        var actual = new AvroSchemaCodec().Encode(expected).ToArray();

        // Assert
        await Assert.That(actual).IsEquivalentTo(wire);
    }

    [Test]
    public async ValueTask round_trips_a_uuid_backed_by_a_fixed() {
        // Arrange
        var sut      = new AvroSchemaCodec();
        var expected = new FixedUuidSample { Id = Faker.Random.Guid() };

        // Act
        var actual = (FixedUuidSample)sut.Decode(sut.Encode(expected), typeof(FixedUuidSample));

        // Assert
        await Assert.That(actual.Id).IsEqualTo(expected.Id);
    }

    [Test]
    public async ValueTask encodes_a_fixed_uuid_identically_to_apache() {
        // Arrange — the schema only parses because the generated module initializer registered the
        // widened uuid logical type. Reference is the Generic writer for the same reason the fixed
        // decimal uses it: the logical conversion yields a GenericFixed the Specific writer rejects.
        var expected = new FixedUuidSample { Id = Faker.Random.Guid() };
        var wire     = EncodeWithApacheGeneric(((ISpecificRecord)expected).Schema, "Id", expected.Id);

        // Act
        var actual = new AvroSchemaCodec().Encode(expected).ToArray();

        // Assert
        await Assert.That(actual).IsEquivalentTo(wire);
    }

    [Test]
    public async ValueTask writes_a_fixed_uuid_in_rfc_4122_byte_order() {
        // Arrange — Guid.ToByteArray() without the flag is little-endian across the first three fields.
        // Getting this backwards yields bytes that decode to a plausible but different uuid, so the order
        // is asserted against the literal rather than against another Guid round-trip.
        var id = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

        byte[] expected = [
            0x6F, 0x96, 0x19, 0xFF, 0x8B, 0x86, 0xD0, 0x11,
            0xB4, 0x2D, 0x00, 0xC0, 0x4F, 0xC9, 0x64, 0xFF
        ];

        // Act
        var actual = new AvroSchemaCodec().Encode(new FixedUuidSample { Id = id }).ToArray();

        // Assert
        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    public async ValueTask encodes_multi_branch_unions_identically_to_apache() {
        // Arrange — Apache resolves a union branch by comparing the value's own ISpecificRecord.Schema
        // against each branch, so this is also the check that the lazily parsed schema carries the right
        // fullname for a type declared inline.
        var expected = new UnionSample {
            ThreeWay         = Faker.Random.Int(1, 500),
            NullableThreeWay = Faker.Lorem.Word(),
            RecordUnion      = new Dog { Bark = Faker.Lorem.Word() }
        };

        var wire = EncodeWithApache(expected);

        // Act
        var actual = new AvroSchemaCodec().Encode(expected).ToArray();

        // Assert
        await Assert.That(actual).IsEquivalentTo(wire);
    }

    [Test]
    public async ValueTask throws_when_no_codec_was_generated_for_the_type() {
        // Arrange — TestNonSpecificRecord has no .avsc, so no codec exists for it. There is no
        // reflection fallback by design, so this must fail loudly rather than silently degrade.
        var sut = new AvroSchemaCodec();

        // Act & Assert
        await Assert.That(() => sut.Decode(new byte[] { 0 }, typeof(TestNonSpecificRecord)))
            .Throws<AvroCodecNotGeneratedException>();
    }
}
