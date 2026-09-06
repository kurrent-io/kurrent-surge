using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Transcoder.Probe;
using Duration = Google.Protobuf.WellKnownTypes.Duration;

namespace Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;

public class ProtobufJsonWriterTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }
    
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
