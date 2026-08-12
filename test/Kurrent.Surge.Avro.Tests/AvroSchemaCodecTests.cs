namespace Kurrent.Surge.Avro.Tests;

[Category("SerDe")]
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
    public async ValueTask encode_non_specific_record_throws() {
        // Arrange — TestNonSpecificRecord does not implement ISpecificRecord, so
        // EnsureValueIsAvroRecord must reject it. The codec must fail loud, never silently
        // treat a plain POCO as an Avro record.
        var sut     = new AvroSchemaCodec();
        var message = new TestNonSpecificRecord(Faker.Lorem.Word());

        // Act & Assert
        await Assert.That(() => sut.Encode(message)).Throws<InvalidOperationException>();
    }
}
