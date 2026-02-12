using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testing.Shouldly;

namespace Kurrent.Client.Tests.Registry;

[Category("Registry")]
[Category("Integration")]
public class GetSchemaTests {
	[ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask get_schema(CancellationToken ct) {
        var schemaName       = KurrentClientTestFixture.NewSchemaName();
        var schemaDefinition = KurrentClientTestFixture.NewJsonSchemaDefinition().ToJson();
        var description      = Fixture.Faker.Lorem.Sentence();
        var tags             = new Dictionary<string, string> { [Fixture.Faker.Lorem.Word()] = Fixture.Faker.Lorem.Word(), [Fixture.Faker.Lorem.Word()] = Fixture.Faker.Lorem.Word() };

		var schemaVersion = await Fixture.Client.Registry
            .CreateSchema(
			    schemaName, schemaDefinition, SchemaDataFormat.Json,
			    CompatibilityMode.Backward, description, tags, ct
		    )
            .ShouldNotThrowOrFailAsync();

        await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		await Fixture.Client.Registry
            .GetSchema(schemaName, ct)
			.ShouldNotThrowOrFailAsync(schema => {
				schema.LatestSchemaVersion.ShouldBe(schemaVersion.VersionNumber);
				schema.SchemaName.Value.ShouldBe(schemaName);
				schema.CreatedAt.ShouldBeGreaterThan(DateTimeOffset.MinValue);
				schema.UpdatedAt.ShouldBeGreaterThan(DateTimeOffset.MinValue);
				schema.Details.Compatibility.ShouldBe(CompatibilityMode.Backward);
				schema.Details.Description.ShouldBe(description);
				schema.Details.Tags.ShouldBe(tags);
				schema.Details.DataFormat.ShouldBe(SchemaDataFormat.Json);
			});
	}

	[Test]
	public async ValueTask get_schema_when_schema_does_not_exist(CancellationToken ct) {
		var schemaName = KurrentClientTestFixture.NewSchemaName();

		await Fixture.Client.Registry
			.GetSchema(schemaName, ct)
			.ShouldFailAsync(failure =>
                failure.Case.ShouldBe(GetSchemaError.GetSchemaErrorCase.SchemaNotFoundException));
	}

	[Test]
	public async ValueTask get_schema_when_schema_deleted(CancellationToken ct) {
		var schemaName       = KurrentClientTestFixture.NewSchemaName();
        var schemaDefinition = KurrentClientTestFixture.NewJsonSchemaDefinition().ToJson();

		await Fixture.Client.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct)
            .ShouldNotThrowOrFailAsync();

        await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		await Fixture.Client.Registry
            .DeleteSchema(schemaName, ct)
            .ShouldNotThrowOrFailAsync();

        await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		await Fixture.Client.Registry
			.GetSchema(schemaName, ct)
			.ShouldFailAsync(failure =>
                failure.Case.ShouldBe(GetSchemaError.GetSchemaErrorCase.SchemaNotFoundException));
	}
}
