namespace Kurrent.Surge.MessagePack.Tests;

[Category("SerDe")]
public class MessagePackSchemaCodecTests {
    static readonly Faker Faker = new();

    static MessagePackSchemaCodec NewCodec() =>
        new(new MessagePackSchemaCodecOptions { UserResolvers = [TestMessagesResolver.Instance] });

    [Test]
    public async ValueTask round_trips_message() {
        // Arrange
        var sut = NewCodec();
        var order = new TestOrder {
            OrderId  = Faker.Random.Guid(),
            Sku      = Faker.Commerce.Ean13(),
            Price    = Faker.Random.Decimal(1, 1_000),
            Quantity = Faker.Random.Int(1, 100)
        };

        // Act
        var encoded = sut.Encode(order);
        var decoded = sut.Decode(encoded, typeof(TestOrder));

        // Assert
        await Assert.That(decoded).IsEqualTo(order);
    }

    [Test]
    public async ValueTask encode_type_without_formatter_throws() {
        // Arrange — TestUnformattedMessage carries no [MessagePackObject] attribute, so no
        // generated or built-in formatter covers it. The codec must fail loud, never fall
        // back to Reflection.Emit.
        var sut     = NewCodec();
        var message = new TestUnformattedMessage(Faker.Lorem.Word());

        // Act & Assert — MessagePackSerializer wraps the FormatterNotRegisteredException.
        await Assert.That(() => sut.Encode(message)).Throws<MessagePackSerializationException>();
    }
}
