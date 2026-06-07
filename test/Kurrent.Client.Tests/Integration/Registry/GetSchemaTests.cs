using Kurrent.Client.Exceptions;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Registry;

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
                CreateSchemaRequest.For(schemaName)
                    .WithDefinition(schemaDefinition)
                    .WithDataFormat(SchemaDataFormat.Json)
                    .WithCompatibility(CompatibilityMode.Backward)
                    .WithDescription(description)
                    .WithTags(tags)
                    .Build(),
                ct);

		await Eventually(async () => {
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
		}, ct: ct);
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

		await Eventually(
			async () => await Assert.ThrowsAsync<SchemaNotFoundException>(
				async () => await Fixture.Client.Registry.GetSchema(schemaName, ct)),
			ct: ct);
	}
}
