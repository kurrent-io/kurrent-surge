using Kurrent.Client.Exceptions;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Sdk.Testkit.Fixtures;

namespace Kurrent.Client.Tests.Registry;

[Category("Registry")]
[Category("Integration")]
public class GetSchemaTests {
	[ClassDataSource<KurrentDBTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentDBTestFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask get_schema(CancellationToken ct) {
        var schemaName       = KurrentDBTestFixture.NewSchemaName();
        var schemaDefinition = KurrentDBTestFixture.NewJsonSchemaDefinition().ToJson();
        var description      = Fixture.Faker.Lorem.Sentence();
        var tags             = new Dictionary<string, string> { [Fixture.Faker.Lorem.Word()] = Fixture.Faker.Lorem.Word(), [Fixture.Faker.Lorem.Word()] = Fixture.Faker.Lorem.Word() };

		var schemaVersion = await Fixture.Client.Registry
            .CreateSchema(
			    schemaName, schemaDefinition, SchemaDataFormat.Json,
			    CompatibilityMode.Backward, description, tags, ct
		    );

        await KurrentDBTestFixture.WaitForEventualConsistency(ct);

		var schema = await Fixture.Client.Registry
            .GetSchema(schemaName, ct);

		await Assert.That(schema.LatestSchemaVersion).IsEqualTo(schemaVersion.VersionNumber);
		await Assert.That(schema.SchemaName.Value).IsEqualTo((string)schemaName);
		await Assert.That(schema.CreatedAt).IsGreaterThan(DateTimeOffset.MinValue);
		await Assert.That(schema.UpdatedAt).IsGreaterThan(DateTimeOffset.MinValue);
		await Assert.That(schema.Details.Compatibility).IsEqualTo(CompatibilityMode.Backward);
		await Assert.That(schema.Details.Description).IsEqualTo(description);
		await Assert.That(schema.Details.Tags).IsEquivalentTo(tags);
		await Assert.That(schema.Details.DataFormat).IsEqualTo(SchemaDataFormat.Json);
	}

	[Test]
	public async ValueTask get_schema_when_schema_does_not_exist(CancellationToken ct) {
		var schemaName = KurrentDBTestFixture.NewSchemaName();

		await Assert.ThrowsAsync<SchemaNotFoundException>(
			async () => await Fixture.Client.Registry.GetSchema(schemaName, ct));
	}

	[Test]
	public async ValueTask get_schema_when_schema_deleted(CancellationToken ct) {
		var schemaName       = KurrentDBTestFixture.NewSchemaName();
        var schemaDefinition = KurrentDBTestFixture.NewJsonSchemaDefinition().ToJson();

		await Fixture.Client.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

        await KurrentDBTestFixture.WaitForEventualConsistency(ct);

		await Fixture.Client.Registry
            .DeleteSchema(schemaName, ct);

        await KurrentDBTestFixture.WaitForEventualConsistency(ct);

		await Assert.ThrowsAsync<SchemaNotFoundException>(
			async () => await Fixture.Client.Registry.GetSchema(schemaName, ct));
	}
}
