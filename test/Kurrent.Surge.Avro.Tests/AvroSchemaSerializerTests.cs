using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Schema.Serialization.Avro;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Kurrent.Surge.Avro.Tests;

[InheritsTests]
[Category("SerDe")]
public class AvroSchemaSerializerTests : SchemaSerializerTestFixture {
    protected override SchemaDataFormat ExpectedDataFormat => SchemaDataFormat.Avro;

    protected override ISchemaSerializer CreateSut() {
        // No auto-mapping: every type the fixture serializes must be in the catalog.
        var catalog = new MessageCatalog("avro-tests")
            .Map<TestOrder>()
            .Map<TestCustomer>()
            .Map<TestInvoiceLine>()
            .Map<TestNonSpecificRecord>(); // for serialize_non_specific_record_throws

        var schemaManager = new SchemaManager(null!, null!, MessageResolver.From(catalog));

        return new AvroSchemaSerializer(
            new AvroSchemaSerializerOptions {
                SchemaRegistration = SchemaRegistrationOptions.ManualMap
            },
            schemaManager
        );
    }

    protected override object[] CreateTestMessages() => [
        new TestOrder {
            OrderId  = Faker.Random.Guid().ToString(),
            Sku      = Faker.Commerce.Ean13(),
            Quantity = Faker.Random.Int(1, 100)
        },
        new TestCustomer {
            Name     = Faker.Person.FullName,
            Email    = Faker.Person.Email,
            IsActive = Faker.Random.Bool()
        },
        new TestInvoiceLine {
            Description = Faker.Commerce.ProductName(),
            UnitPrice   = Faker.Random.Double(1, 1_000),
            Quantity    = Faker.Random.Int(1, 100)
        }
    ];

    protected override SchemaSerializationContext CreateSerializeContext() => new() {
        Stream = "test-stream",
        RecordSchema = new RecordSchemaInfo {
            Schema     = "test-schema",
            DataFormat = SchemaDataFormat.Avro
        }
    };

    [Test]
    public async ValueTask serialize_non_specific_record_throws() {
        // Arrange — TestNonSpecificRecord is catalog-mapped but does not implement ISpecificRecord,
        // so EnsureValueIsAvroRecord must reject it. The serializer must fail loud, never silently
        // treat a plain POCO as an Avro record.
        var context = CreateSerializeContext();
        var message = new TestNonSpecificRecord(Faker.Lorem.Word());

        // Act & Assert
        await Assert.That(async () => await Sut.Serialize(message, context).ConfigureAwait(false))
            .ThrowsException();
    }
}
