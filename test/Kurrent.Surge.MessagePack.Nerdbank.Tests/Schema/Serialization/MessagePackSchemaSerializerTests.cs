using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Schema.Serialization.MessagePack;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;

namespace Kurrent.Surge.MessagePack.Nerdbank.Tests.Schema.Serialization;

[InheritsTests]
[Category("SerDe")]
public class MessagePackSchemaSerializerTests : Kurrent.Surge.Tests.Schema.Serialization.SchemaSerializerTestFixture {
	protected override SchemaDataFormat ExpectedDataFormat => SchemaDataFormat.MessagePack;

	protected override ISchemaSerializer CreateSut() {
		// No auto-mapping: every type the fixture serializes must be in the catalog.
		var catalog = new MessageCatalog("messagepack-nerdbank-tests")
			.Map<OrderPlaced>()
			.Map<OrderShipped>()
			.Map<InventoryAdjusted>();

		var schemaManager = new SchemaManager(null!, null!, MessageResolver.From(catalog));

		return new MessagePackSchemaSerializer(
			new MessagePackSchemaSerializerOptions {
				SchemaRegistration = SchemaRegistrationOptions.ManualMap,
				TypeShapeProvider  = MessageWitness.GeneratedTypeShapeProvider
			},
			schemaManager
		);
	}

	protected override object[] CreateTestMessages() => [
		new OrderPlaced(
			Faker.Random.Guid(),
			Faker.Person.FullName,
			Faker.Finance.Amount(),
			Faker.Date.RecentOffset()
		),
		new OrderShipped(
			Faker.Random.Guid(),
			Faker.Random.AlphaNumeric(12),
			Faker.Date.RecentOffset()
		),
		new InventoryAdjusted(
			Faker.Commerce.Ean13(),
			Faker.Random.Int(-100, 100)
		)
	];

	protected override SchemaSerializationContext CreateSerializeContext() => new() {
		Stream       = "test-stream",
		RecordSchema = new RecordSchemaInfo {
			Schema     = "test-schema",
			DataFormat = SchemaDataFormat.MessagePack
		}
	};
}
