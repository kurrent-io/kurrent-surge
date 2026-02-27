using Kurrent.Client.Registry;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Registry;

public class GetSchemaTests : KurrentClientTestFixture {
	const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema(CancellationToken ct) {
        var schemaName       = NewSchemaName();
        var schemaDefinition = NewJsonSchemaDefinition().ToJson();
        var description      = Faker.Lorem.Sentence();
        var tags             = new Dictionary<string, string> { [Faker.Lorem.Word()] = Faker.Lorem.Word(), [Faker.Lorem.Word()] = Faker.Lorem.Word() };

		var schemaVersion = await AutomaticClient.Registry
            .CreateSchema(
			    schemaName, schemaDefinition, SchemaDataFormat.Json,
			    CompatibilityMode.Backward, description, tags, ct
		    );

		var schema = await AutomaticClient.Registry
            .GetSchema(schemaName, cancellationToken: ct);

		await Assert.That(schema.LatestSchemaVersion).IsEqualTo(schemaVersion.VersionNumber);
		await Assert.That(schema.SchemaName.Value).IsEqualTo(schemaName);
		await Assert.That(schema.CreatedAt).IsGreaterThan(DateTimeOffset.MinValue);
		await Assert.That(schema.UpdatedAt).IsGreaterThan(DateTimeOffset.MinValue);
		await Assert.That(schema.Details.Compatibility).IsEqualTo(CompatibilityMode.Backward);
		await Assert.That(schema.Details.Description).IsEqualTo(description);
		await Assert.That(schema.Details.Tags).IsEqualTo(tags);
		await Assert.That(schema.Details.DataFormat).IsEqualTo(SchemaDataFormat.Json);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema_when_schema_does_no_exists(CancellationToken ct) {

		var schemaName = NewSchemaName();

		await Assert.That(async () => await AutomaticClient.Registry
			.GetSchema(schemaName, cancellationToken: ct))
			.ThrowsException();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema_when_schema_deleted(CancellationToken ct) {
		// Arrange
		var schemaName       = NewSchemaName();
        var schemaDefinition = NewJsonSchemaDefinition().ToJson();

		await AutomaticClient.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

		await AutomaticClient.Registry
            .DeleteSchema(schemaName, ct);

		await Assert.That(async () => await AutomaticClient.Registry
			.GetSchema(schemaName, cancellationToken: ct))
			.ThrowsException();
	}
}
