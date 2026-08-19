namespace Kurrent.Surge.Codecs.NerdbankMessagePack.Tests;

[Category("Codecs")]
public class NerdbankMessagePackSchemaCodecTests {
    static readonly Faker Faker = new();

    static NerdbankMessagePackSchemaCodec NewCodec() =>
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
