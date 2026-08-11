namespace Kurrent.Surge.MessagePack.Tests;

[InheritsTests]
[Category("SerDe")]
public class MessagePackSchemaSerializerTests : SchemaSerializerTestFixture {
    protected override SchemaDataFormat ExpectedDataFormat => SchemaDataFormat.MessagePack;

    protected override ISchemaSerializer CreateSut() {
        // No auto-mapping: every type the fixture serializes must be in the catalog.
        var catalog = new MessageCatalog("messagepack-tests")
            .Map<TestOrder>()
            .Map<TestCustomer>()
            .Map<TestInvoiceLine>()
            .Map<TestUnformattedMessage>(); // for serialize_type_without_formatter_throws

        var schemaManager = new SchemaManager(null!, null!, MessageResolver.From(catalog));

        return new MessagePackSchemaSerializer(
            new MessagePackSchemaSerializerOptions {
                SchemaRegistration = SchemaRegistrationOptions.ManualMap,
                UserResolvers      = [TestMessagesResolver.Instance]
            },
            schemaManager
        );
    }

    protected override object[] CreateTestMessages() => [
        new TestOrder {
            OrderId  = Faker.Random.Guid(),
            Sku      = Faker.Commerce.Ean13(),
            Price    = Faker.Random.Decimal(1, 1_000),
            Quantity = Faker.Random.Int(1, 100)
        },
        new TestCustomer {
            Name     = Faker.Person.FullName,
            Email    = Faker.Person.Email,
            IsActive = Faker.Random.Bool()
        },
        new TestInvoiceLine {
            Description = Faker.Commerce.ProductName(),
            UnitPrice   = Faker.Random.Decimal(1, 1_000),
            Quantity    = Faker.Random.Int(1, 100)
        }
    ];

    protected override SchemaSerializationContext CreateSerializeContext() => new() {
        Stream = "test-stream",
        RecordSchema = new RecordSchemaInfo {
            Schema     = "test-schema",
            DataFormat = SchemaDataFormat.MessagePack
        }
    };

    [Test]
    public async ValueTask serialize_type_without_formatter_throws() {
        // Arrange — TestUnformattedMessage is catalog-mapped but carries no [MessagePackObject]
        // attribute, so no generated or built-in formatter covers it. The serializer must fail
        // loud (FormatterNotRegisteredException), never fall back to Reflection.Emit.
        var context = CreateSerializeContext();
        var message = new TestUnformattedMessage(Faker.Lorem.Word());

        // Act & Assert
        await Assert.That(async () => await Sut.Serialize(message, context).ConfigureAwait(false))
            .ThrowsException();
    }
}
