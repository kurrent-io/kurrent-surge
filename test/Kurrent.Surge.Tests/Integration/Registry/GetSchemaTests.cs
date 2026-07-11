using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Tests.Fixtures;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Tests.Registry;

[Category("Registry")]
[Category("Integration")]
public class GetSchemaTests {
	[ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask get_schema(CancellationToken ct) {
        var schemaName       = KurrentDBFixture.NewSchemaName();
        var schemaDefinition = KurrentDBFixture.NewJsonSchemaDefinition().ToJson();
        var description      = Fixture.Faker.Lorem.Sentence();
        var tags             = new Dictionary<string, string> { [Fixture.Faker.Lorem.Word()] = Fixture.Faker.Lorem.Word(), [Fixture.Faker.Lorem.Word()] = Fixture.Faker.Lorem.Word() };

		var schemaVersion = await Fixture.Client.Registry
            .CreateSchema(
                schemaName,
                r => {
                    r.SchemaDefinition  = schemaDefinition;
                    r.DataFormat        = SchemaDataFormat.Json;
                    r.CompatibilityMode = CompatibilityMode.Backward;
                    r.Description       = description;
                    r.Tags              = tags;
                },
                ct);

		await Assert.That(async () => {
			var schema = await Fixture.Client.Registry
				.GetSchema(schemaName, ct);

			await Assert.That(schema.LatestSchemaVersion).IsEqualTo(schemaVersion.VersionNumber);
			await Assert.That(schema.SchemaName.Value).IsEqualTo(schemaName);
			await Assert.That(schema.CreatedAt).IsGreaterThan(DateTimeOffset.MinValue);
			await Assert.That(schema.UpdatedAt).IsGreaterThan(DateTimeOffset.MinValue);
			await Assert.That(schema.Details.Compatibility).IsEqualTo(CompatibilityMode.Backward);
			await Assert.That(schema.Details.Description).IsEqualTo(description);
			await Assert.That(schema.Details.Tags).IsEquivalentTo(tags);
			await Assert.That(schema.Details.DataFormat).IsEqualTo(SchemaDataFormat.Json);
		}).EventuallyPasses(RetryOnTransients(ct));
	}

	[Test]
	public async ValueTask get_schema_when_schema_does_not_exist(CancellationToken ct) {
		var schemaName = KurrentDBFixture.NewSchemaName();

		await Assert.ThrowsAsync<SchemaNotFoundException>(
			async () => await Fixture.Client.Registry.GetSchema(schemaName, ct));
	}

	[Test]
	public async ValueTask get_schema_when_schema_deleted(CancellationToken ct) {
		var schemaName       = KurrentDBFixture.NewSchemaName();
        var schemaDefinition = KurrentDBFixture.NewJsonSchemaDefinition().ToJson();

		await Fixture.Client.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

		await Fixture.Client.Registry
            .DeleteSchema(schemaName, ct);

		await Assert.That(async () => { await Assert.ThrowsAsync<SchemaNotFoundException>(
				async () => await Fixture.Client.Registry.GetSchema(schemaName, ct)); })
			.EventuallyPasses(RetryOnTransients(ct));
	}
}
