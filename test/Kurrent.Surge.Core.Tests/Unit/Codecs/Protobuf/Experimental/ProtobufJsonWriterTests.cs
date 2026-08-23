using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using JetBrains.Annotations;
using Kurrent.Surge.Codecs.Protobuf.Experimental;
using Transcoder.Probe;
using Duration = Google.Protobuf.WellKnownTypes.Duration;

namespace Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;

public class ProtobufJsonWriterTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask matches_google_json_for_a_fully_populated_order() {
        // Arrange
        var order    = FullyPopulatedOrder();
        var expected = Normalize(ProbeSchema.Oracle.Format(order));

        // Act
        var actual = Normalize(ProbeSchema.Write(order.ToByteArray()));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask matches_google_json_for_an_empty_message() {
        // Arrange
        var order    = new Order();
        var expected = Normalize(ProbeSchema.Oracle.Format(order));

        // Act
        var actual = Normalize(ProbeSchema.Write(order.ToByteArray()));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask emits_a_proto3_optional_field_set_to_its_default() {
        // A field with explicit presence set to "" is on the wire and must survive. Treating proto3
        // presence as implicit drops it, which is the defect this case pins.

        // Arrange
        var order    = new Order { Note = "" };
        var expected = Normalize(ProbeSchema.Oracle.Format(order));

        // Act
        var actual = Normalize(ProbeSchema.Write(order.ToByteArray()));

        // Assert
        await Assert.That(actual).Contains("\"note\"");
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask omits_a_plain_proto3_field_set_to_its_default() {
        // Arrange
        var order    = new Order { OrderId = "" };
        var expected = Normalize(ProbeSchema.Oracle.Format(order));

        // Act
        var actual = Normalize(ProbeSchema.Write(order.ToByteArray()));

        // Assert
        await Assert.That(actual).DoesNotContain("\"orderId\"");
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask matches_google_json_for_well_known_types() {
        // Arrange
        var order = new Order {
            CreatedAt = new Timestamp { Seconds = 1_724_000_000, Nanos = 123_000_000 },
            Ttl       = new Duration { Seconds = 90, Nanos = 500_000 },
            Label     = "shipped",
            Retries   = 3,
            Mask      = new FieldMask { Paths = { "order_id", "lines" } },
            Metadata  = new Struct {
                Fields = {
                    ["channel"] = Value.ForString("web"),
                    ["weight"]  = Value.ForNumber(12.5),
                    ["flagged"] = Value.ForBool(false),
                    ["absent"]  = Value.ForNull(),
                    ["nested"]  = Value.ForStruct(new Struct { Fields = { ["a"] = Value.ForNumber(1) } }),
                    ["list"]    = Value.ForList(Value.ForString("x"), Value.ForNumber(2))
                }
            }
        };

        var expected = Normalize(ProbeSchema.Oracle.Format(order));

        // Act
        var actual = Normalize(ProbeSchema.Write(order.ToByteArray()));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask matches_google_json_for_a_timestamp_without_fractional_seconds() {
        // Arrange
        var order    = new Order { CreatedAt = new Timestamp { Seconds = 1_724_000_000 } };
        var expected = Normalize(ProbeSchema.Oracle.Format(order));

        // Act
        var actual = Normalize(ProbeSchema.Write(order.ToByteArray()));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask matches_google_json_for_an_any_payload() {
        // Arrange
        var order    = new Order { Envelope = Any.Pack(new Money { Currency = "EUR", Amount = 4_200 }) };
        var expected = Normalize(ProbeSchema.Oracle.Format(order));

        // Act
        var actual = Normalize(ProbeSchema.Write(order.ToByteArray()));

        // Assert
        await Assert.That(actual).Contains("@type");
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask matches_google_json_for_non_finite_floating_point() {
        // Arrange
        var order    = new Order { Ratio = double.NaN, Score = float.PositiveInfinity };
        var expected = Normalize(ProbeSchema.Oracle.Format(order));

        // Act
        var actual = Normalize(ProbeSchema.Write(order.ToByteArray()));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask matches_google_json_for_an_unpacked_repeated_run() {
        // protoc writes a packed run, so the unpacked form has to be built by hand. Both encodings
        // are legal for the same field and a reader must accept either.

        // Arrange
        var expected = Normalize(ProbeSchema.Oracle.Format(new Order { Flags = { 1, 2, 3 } }));

        var wire = new List<byte>();

        foreach (var value in (int[])[1, 2, 3]) {
            wire.Add(6 << 3 | 0);   // field 6, varint
            wire.Add((byte)value);
        }

        // Act
        var actual = Normalize(ProbeSchema.Write(wire.ToArray()));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask joins_a_repeated_field_split_across_non_adjacent_runs() {
        // Arrange
        var expected = Normalize(ProbeSchema.Oracle.Format(new Order { OrderId = "A", Flags = { 1, 2 } }));

        var wire = new List<byte> {
            6 << 3 | 0, 1,                    // flags = 1
            1 << 3 | 2, 1, (byte)'A',         // order_id = "A", interleaved
            6 << 3 | 0, 2                     // flags = 2
        };

        // Act
        var actual = Normalize(ProbeSchema.Write(wire.ToArray()));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask keeps_the_last_value_of_a_repeated_singular_field() {
        // Arrange
        var expected = Normalize(ProbeSchema.Oracle.Format(new Order { OrderId = "B" }));

        var wire = new List<byte> {
            1 << 3 | 2, 1, (byte)'A',
            1 << 3 | 2, 1, (byte)'B'
        };

        // Act
        var actual = Normalize(ProbeSchema.Write(wire.ToArray()));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask skips_a_field_the_schema_does_not_declare() {
        // Arrange
        var expected = Normalize(ProbeSchema.Oracle.Format(new Order { OrderId = "A" }));

        // Field 24 is not declared. Its tag is 192, which needs two varint bytes — every field
        // number small enough for a one-byte tag is already taken by the schema.
        var wire = new List<byte> {
            1 << 3 | 2, 1, (byte)'A',
            0xC0, 0x01, 7
        };

        // Act
        var actual = Normalize(ProbeSchema.Write(wire.ToArray()));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask emits_proto_field_names_when_asked() {
        // Arrange
        var order   = new Order { OrderId = Faker.Random.Guid().ToString() };
        var options = new ProtobufJsonWriterOptions { UseProtoFieldNames = true };

        // Act
        var actual = ProbeSchema.Write(order.ToByteArray(), options: options);

        // Assert
        await Assert.That(actual).Contains("\"order_id\"").And.DoesNotContain("\"orderId\"");
    }

    [Test]
    public async ValueTask emits_enums_as_integers_when_asked() {
        // Arrange
        var order   = new Order { Priority = Priority.High };
        var options = new ProtobufJsonWriterOptions { EnumsAsIntegers = true };

        // Act
        var actual = ProbeSchema.Write(order.ToByteArray(), options: options);

        // Assert
        await Assert.That(actual).Contains("\"priority\":2").And.DoesNotContain("PRIORITY_HIGH");
    }

    [Test]
    public async ValueTask emits_enum_names_by_default() {
        // Arrange
        var order    = new Order { Priority = Priority.Low };
        var expected = Normalize(ProbeSchema.Oracle.Format(order));

        // Act
        var actual = Normalize(ProbeSchema.Write(order.ToByteArray()));

        // Assert
        await Assert.That(actual).Contains("PRIORITY_LOW");
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask emits_a_number_for_an_enum_value_the_schema_does_not_declare() {
        // An open enum accepts a value no name is declared for, and the mapping emits the number.

        // Arrange
        var wire = new List<byte> { 5 << 3 | 0, 77 };

        // Act
        var actual = ProbeSchema.Write(wire.ToArray());

        // Assert
        await Assert.That(actual).Contains("\"priority\":77");
    }

    [Test]
    public async ValueTask matches_google_json_when_defaults_are_emitted() {
        // Arrange
        var order    = new Order { OrderId = "A" };
        var expected = Normalize(ProbeSchema.OracleWithDefaults.Format(order));
        var options  = new ProtobufJsonWriterOptions { EmitDefaults = true };

        // Act
        var actual = Normalize(ProbeSchema.Write(order.ToByteArray(), options: options));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask writes_a_nested_message_from_the_schema_it_declares() {
        // Arrange
        var money    = new Money { Currency = "GBP", Amount = 1_234_567_890_123 };
        var expected = Normalize(ProbeSchema.Oracle.Format(money));

        // Act
        var actual = Normalize(ProbeSchema.Write(money.ToByteArray(), ProbeSchema.MoneyTypeName));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask rejects_a_payload_that_does_not_match_the_schema() {
        // A length that runs past the end of the buffer is the cheapest way to be certain the
        // payload is not this message.

        // Arrange
        var wire = new byte[] { 1 << 3 | 2, 200 };

        // Act & Assert
        await Assert.That(() => ProbeSchema.Write(wire)).Throws<InvalidDataException>();
    }

    Order FullyPopulatedOrder() =>
        new() {
            OrderId  = Faker.Random.Guid().ToString(),
            Lines    = {
                new Line { Sku = Faker.Commerce.Ean13(), Quantity = 2, Price = new Money { Currency = "USD", Amount = 999 } },
                new Line { Sku = Faker.Commerce.Ean13(), Quantity = 7 }
            },
            Tags     = { ["channel"] = "web", ["region"] = "emea" },
            Totals   = { [1] = new Money { Currency = "USD", Amount = 1_998 }, [2] = new Money { Currency = "EUR", Amount = 10 } },
            Priority = Priority.High,
            Flags    = { 1, 2, 3, 5, 8 },
            Payload  = ByteString.CopyFrom(0xCA, 0xFE, 0xBA, 0xBE),
            Ratio    = 0.125,
            Score    = 2.5f,
            Sequence = ulong.MaxValue,
            Delta    = -7,
            Checksum = 0xDEADBEEFCAFEBABE,
            Archived = true,
            Note     = "handle with care",
            Web      = "storefront",
            CreatedAt = new Timestamp { Seconds = 1_724_000_000, Nanos = 42 },
            Ttl       = new Duration { Seconds = -30 },
            Label     = "shipped",
            Retries   = 3,
            Envelope  = Any.Pack(new Money { Currency = "CHF", Amount = 55 }),
            Mask      = new FieldMask { Paths = { "order_id" } }
        };

    /// <remarks>
    /// Reparsing normalises whitespace without reordering: <see cref="JsonNode"/> keeps insertion
    /// order, so a strategy that emits fields in the wrong order still fails.
    /// </remarks>
    static string Normalize(string json) => JsonNode.Parse(json)!.ToJsonString();
}
