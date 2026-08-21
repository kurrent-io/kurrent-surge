namespace Kurrent.Surge.Codecs.Avro.Tests;

[Category("Codecs")]
public class AvroSchemaCodecTests {
    static readonly Faker Faker = new();

    [Test]
    public async ValueTask round_trips_message() {
        // Arrange
        var sut = new AvroSchemaCodec();
        var order = new TestOrder {
            OrderId  = Faker.Random.Guid().ToString(),
            Sku      = Faker.Commerce.Ean13(),
            Quantity = Faker.Random.Int(1, 100)
        };

        // Act
        var encoded = sut.Encode(order);
        var decoded = sut.Decode(encoded, typeof(TestOrder));

        // Assert
        await Assert.That(decoded).IsEqualTo(order);
    }

    [Test]
    public async ValueTask encode_a_type_with_no_generated_codec_throws() {
        // Arrange — the registry lookup is the validation. A type with no generated codec must fail
        // loud, never be silently treated as an Avro record. Testing for ISpecificRecord first said
        // less than this does: a type can implement that interface and still have no codec.
        var sut     = new AvroSchemaCodec();
        var message = new TestNonSpecificRecord(Faker.Lorem.Word());

        // Act & Assert
        await Assert.That(() => sut.Encode(message)).Throws<AvroCodecNotGeneratedException>();
    }
}
