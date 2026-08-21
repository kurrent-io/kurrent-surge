using System.Globalization;
using System.Text.Json.Nodes;
using Avro.Generic;
using Avro.IO;

namespace Kurrent.Surge.Codecs.Avro.Tests;

/// <summary>
/// Asserts that <see cref="AvroJsonWriter"/> rewrites a payload exactly as Apache.Avro's own transcode
/// does, for every schema construct the codec supports.
/// </summary>
/// <remarks>
/// Apache is the independent implementation of the specification's JSON encoding, so agreement with it
/// is what makes owning the transcode safe. The comparison is semantic — both sides are parsed and
/// compared as JSON — because escaping and whitespace are writer choices, not encoding.
/// </remarks>
[Category("Codecs")]
[Timeout(10_000)]
public class AvroJsonWriterTests {
    static readonly Faker Faker = new();

    /// <summary>Transcodes a payload to JSON the way Apache does: Generic reader, then JsonEncoder.</summary>
    static string ApacheToJson(ReadOnlyMemory<byte> payload, global::Avro.Schema schema) {
        using var input = new MemoryStream(payload.ToArray(), writable: false);

        var reader = new GenericDatumReader<GenericRecord>(schema, schema);
        var record = reader.Read(default!, new BinaryDecoder(input));

        using var output = new MemoryStream();

        var writer  = new GenericDatumWriter<GenericRecord>(schema);
        var encoder = new JsonEncoder(schema, output);

        writer.Write(record, encoder);
        encoder.Flush();

        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }

    /// <summary>The schema text a message was generated from, as authored.</summary>
    /// <remarks>
    /// Apache's own <c>Schema.ToString()</c> cannot supply this. For a logical type over a named base it
    /// writes <c>{"type":{"type":"fixed",...},"logicalType":"decimal"}</c>, nesting the base under
    /// <c>type</c> where the specification puts <c>logicalType</c> on the base's own object. Apache for
    /// C# re-reads that shape; nothing else does. Pinned by
    /// <see cref="apache_writes_an_unparseable_schema_for_a_logical_type_over_a_fixed"/>.
    /// </remarks>
    static string SchemaTextOf(global::Avro.Schema schema) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Schemas", $"{((NamedSchema)schema).Name}.avsc"));

    static async ValueTask AssertMatchesApache(ISpecificRecord message) {
        // Arrange
        var schema   = message.Schema;
        var text     = SchemaTextOf(schema);
        var payload  = new AvroSchemaCodec().Encode(message);
        var expected = ApacheToJson(payload, schema);

        // Act
        var actual = new AvroJsonWriter(text).Write(payload);

        // Assert
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(actual), JsonNode.Parse(expected)))
            .IsTrue()
            .Because(
                $"the JSON must match Apache's transcode of the same payload.{Environment.NewLine}" +
                $"ours:   {actual}{Environment.NewLine}" +
                $"apache: {expected}{Environment.NewLine}" +
                $"schema: {text}"
            );
    }

    static string PropertyOrder(string json) =>
        string.Join(",", JsonNode.Parse(json)!.AsObject().Select(property => property.Key));

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

    #region ->> Records and primitives <<-

    [Test]
    public async ValueTask writes_record_fields_in_the_same_order_as_apache() {
        // Arrange — JsonNode.DeepEquals compares an object's properties by name and ignores their order,
        // so every other test here would pass on a writer that emitted the fields shuffled. This is the
        // one assertion that holds the order.
        var message  = NewComplexOrder(Faker.Lorem.Sentence());
        var schema   = ((ISpecificRecord)message).Schema;
        var payload  = new AvroSchemaCodec().Encode(message);
        var expected = PropertyOrder(ApacheToJson(payload, schema));

        // Act
        var actual = PropertyOrder(new AvroJsonWriter(SchemaTextOf(schema)).Write(payload));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask writes_a_flat_record_as_apache_does() {
        // Arrange
        var message = new TestOrder {
            OrderId  = Faker.Random.Guid().ToString(),
            Sku      = Faker.Commerce.Ean13(),
            Quantity = Faker.Random.Int(1, 100)
        };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    [Test]
    public async ValueTask writes_a_boolean_field_as_apache_does() {
        // Arrange — the only sample carrying a boolean, which JSON renders unquoted.
        var message = new TestCustomer {
            Name     = Faker.Name.FullName(),
            Email    = Faker.Internet.Email(),
            IsActive = Faker.Random.Bool()
        };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    [Test]
    public async ValueTask writes_a_double_field_as_apache_does() {
        // Arrange — a double is the one primitive whose text form depends on the writer's number
        // formatting, so the two implementations have to agree on the shortest round-trippable form.
        var message = new TestInvoiceLine {
            Description = Faker.Commerce.ProductName(),
            UnitPrice   = Faker.Random.Double(1, 10_000),
            Quantity    = Faker.Random.Int(1, 50)
        };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    [Test]
    public async ValueTask writes_a_multi_byte_string_as_apache_does() {
        // Arrange — Utf8JsonWriter escapes non-ASCII to \uXXXX and Newtonsoft writes the character, so
        // this is the case that fails if the comparison is ever weakened to raw text.
        var message = new TestOrder {
            OrderId  = "Ripley — 日本語 — 🚀",
            Sku      = "\"quoted\"\\ / \b\f\n\r\t",
            Quantity = Faker.Random.Int(1, 100)
        };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    #endregion // Records and primitives

    #region ->> Nested records, arrays, maps and enums <<-

    [Test]
    [Arguments(true)]
    [Arguments(false)] // the null branch of ["null", "string"], written bare rather than tagged
    public async ValueTask writes_a_complex_record_as_apache_does(bool hasNote) {
        // Arrange
        var message = NewComplexOrder(hasNote ? Faker.Lorem.Sentence() : null);

        // Act & Assert
        await AssertMatchesApache(message);
    }

    [Test]
    public async ValueTask writes_empty_collections_as_apache_does() {
        // Arrange — a blocked array and map both terminate with a single zero count, and the JSON form
        // is one empty array and one empty object rather than a representation of the blocks.
        var message = NewComplexOrder(Faker.Lorem.Sentence()) with {
            Lines    = [],
            Counters = new Dictionary<string, long>()
        };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    [Test]
    public async ValueTask writes_map_keys_that_need_escaping_as_apache_does() {
        // Arrange — a map key is a JSON property name, which is the other place escaping applies.
        var message = NewComplexOrder(Faker.Lorem.Sentence()) with {
            Counters = new Dictionary<string, long> {
                ["with \"quotes\""] = Faker.Random.Long(1, 999),
                ["日本語"]            = Faker.Random.Long(1, 999),
                [""]               = Faker.Random.Long(1, 999)
            }
        };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    #endregion // Nested records, arrays, maps and enums

    #region ->> Unions <<-

    [Test]
    public async ValueTask writes_multi_branch_unions_as_apache_does() {
        // Arrange — a non-null branch is tagged with the branch's name, which is the fullname for a
        // named type and the Avro type name for a primitive.
        var message = new UnionSample {
            ThreeWay         = Faker.Random.Int(1, 500),
            NullableThreeWay = Faker.Lorem.Word(),
            RecordUnion      = new Dog { Bark = Faker.Lorem.Word() }
        };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    [Test]
    public async ValueTask writes_the_null_branch_of_a_multi_branch_union_as_apache_does() {
        // Arrange — the string branch of ThreeWay and the null branch of NullableThreeWay, so the two
        // shapes a union can take appear in the same message.
        var message = new UnionSample {
            ThreeWay         = Faker.Lorem.Word(),
            NullableThreeWay = null,
            RecordUnion      = new Cat { Lives = Faker.Random.Int(1, 9) }
        };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    [Test]
    public async ValueTask writes_a_boolean_union_branch_as_apache_does() {
        // Arrange — the third branch of ThreeWay, unreachable from the other two cases.
        var message = new UnionSample {
            ThreeWay         = Faker.Random.Bool(),
            NullableThreeWay = Faker.Random.Int(1, 500),
            RecordUnion      = new Cat { Lives = Faker.Random.Int(1, 9) }
        };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    #endregion // Unions

    #region ->> Bytes and fixed <<-

    [Test]
    public async ValueTask writes_bytes_as_apache_does() {
        // Arrange
        var message = new BytesSample { Payload = Faker.Random.Bytes(32) };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    [Test]
    public async ValueTask writes_every_byte_value_as_apache_does() {
        // Arrange — the specification writes each byte as the code point of the same value, so the whole
        // 0-255 range is the assertion: control codes, ASCII, and everything above 0x7F that a Base64
        // assumption would get wrong.
        var payload = new byte[256];

        for (var i = 0; i < payload.Length; i++) payload[i] = (byte)i;

        // Act & Assert
        await AssertMatchesApache(new BytesSample { Payload = payload });
    }

    [Test]
    public async ValueTask writes_a_bytes_field_longer_than_the_stack_buffer_as_apache_does() {
        // Arrange — past 256 bytes the writer rents a buffer instead of stack-allocating one.
        var message = new BytesSample { Payload = Faker.Random.Bytes(4_000) };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    [Test]
    public async ValueTask writes_fixed_as_apache_does() {
        // Arrange
        var message = new FixedSample { Checksum = new Md5 { Value = Faker.Random.Bytes(16) } };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    #endregion // Bytes and fixed

    #region ->> Logical types <<-

    [Test]
    public async ValueTask writes_logical_types_as_apache_does() {
        // Arrange — a logical type does not change the JSON encoding, so every one of these must appear
        // as its base type: a day count, a millisecond count, a byte string.
        var message = new LogicalSample {
            Id      = Faker.Random.Guid(),
            Amount  = new global::Avro.AvroDecimal(decimal.Round(Faker.Random.Decimal(1, 9999), 2)),
            Day     = new DateOnly(2026, 8, 19),
            TimeMs  = TimeOnly.FromTimeSpan(TimeSpan.FromMilliseconds(Faker.Random.Int(0, 86_399_000))),
            StampMs = new DateTime(2026, 8, 19, 12, 34, 56, DateTimeKind.Utc),
            StampUs = new DateTime(2026, 8, 19, 12, 34, 56, DateTimeKind.Utc)
        };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    [Test]
    [Arguments("1234.56")]  // positive: the unscaled value is left-padded with 0x00
    [Arguments("-1234.56")] // negative: sign-extended with 0xFF, a byte string of high code points
    [Arguments("0.00")]
    [Arguments("-0.01")]
    public async ValueTask writes_a_decimal_backed_by_a_fixed_as_apache_does(string amount) {
        // Arrange
        var message = new DecimalFixedSample {
            Amount = new global::Avro.AvroDecimal(decimal.Parse(amount, CultureInfo.InvariantCulture))
        };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    [Test]
    public async ValueTask writes_a_uuid_backed_by_a_fixed_as_apache_does() {
        // Arrange — sixteen bytes in RFC 4122 order, written as a byte string rather than the textual
        // uuid form the string-backed variant carries.
        var message = new FixedUuidSample { Id = Faker.Random.Guid() };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    [Test]
    [Arguments(1_700_000_000_123_456_789L)]
    [Arguments(0L)]
    [Arguments(-1L)]
    [Arguments(-6_795_364_578_871_345_152L)]
    public async ValueTask writes_nanosecond_timestamps_as_apache_does(long nanoseconds) {
        // Arrange — Apache does not model timestamp-nanos, so it sees a plain long and so must we.
        var message = new NanoSample { Precise = new NanoInstant(nanoseconds) };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    [Test]
    public async ValueTask writes_local_timestamps_as_apache_does() {
        // Arrange
        var moment  = new DateTime(2026, 8, 19, 12, 34, 56, DateTimeKind.Unspecified);
        var message = new LocalTimestampSample { LocalMs = moment, LocalUs = moment };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    [Test]
    public async ValueTask writes_logical_types_it_does_not_model_as_apache_does() {
        // Arrange — 'local-timestamp-nanos' and 'varchar' both degrade to the underlying primitive.
        var message = new UnknownLogicalSample {
            Nanos    = Faker.Random.Long(0, 1_700_000_000_000_000_000),
            Invented = Faker.Lorem.Word()
        };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    [Test]
    public async ValueTask writes_a_duration_as_apache_does() {
        // Arrange — 'duration' is a fixed(12) neither implementation models, so both write the twelve
        // bytes as a byte string.
        var message = new DurationSample { Span = new Span12 { Value = Faker.Random.Bytes(12) } };

        // Act & Assert
        await AssertMatchesApache(message);
    }

    [Test]
    public async ValueTask apache_writes_an_unparseable_schema_for_a_logical_type_over_a_fixed() {
        // Arrange — pins why the schema text comes from the .avsc rather than from the message. The
        // specification puts 'logicalType' on the base type's own object; Apache for C# nests the base
        // under 'type' instead, and Java rejects the result. If a later Avro fixes that, this test fails
        // and the schema text can come from Schema.ToString() again.
        var message  = new DecimalFixedSample { Amount = new global::Avro.AvroDecimal(1234.56m) };
        var expected = "\"type\":{\"type\":\"fixed\",\"name\":\"Money\"";

        // Act
        var actual = ((ISpecificRecord)message).Schema.ToString();

        // Assert
        await Assert.That(actual).Contains(expected);
        await Assert.That(() => new AvroJsonWriter(actual)).Throws<ArgumentException>();
    }

    #endregion // Logical types
}
