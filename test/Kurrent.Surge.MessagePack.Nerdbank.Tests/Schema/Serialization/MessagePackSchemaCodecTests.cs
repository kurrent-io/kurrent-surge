using Bogus;
using Kurrent.Surge.Client.Schema.Serialization.MessagePack;

namespace Kurrent.Surge.MessagePack.Nerdbank.Tests.Schema.Serialization;

[Category("SerDe")]
public class MessagePackSchemaCodecTests {
    static readonly Faker Faker = new();

    static MessagePackSchemaCodec NewCodec() =>
        new(new NerdbankMessagePackSchemaCodecOptions { TypeShapeProvider = MessageWitness.GeneratedTypeShapeProvider });

    [Test]
    public async ValueTask round_trips_message() {
        // Arrange
        var sut   = NewCodec();
        var order = new OrderPlaced(
            Faker.Random.Guid(),
            Faker.Person.FullName,
            Faker.Finance.Amount(),
            Faker.Date.RecentOffset());

        // Act
        var encoded = sut.Encode(order);
        var decoded = sut.Decode(encoded, typeof(OrderPlaced));

        // Assert
        await Assert.That(decoded).IsEqualTo(order);
    }
}
