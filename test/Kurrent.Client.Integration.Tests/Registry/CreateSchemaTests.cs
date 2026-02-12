using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testing.Shouldly;

namespace Kurrent.Client.Tests.Registry;

[Category("Registry")]
[Category("Integration")]
public class CreateSchemaTests {
	[ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask registers_initial_version_of_new_schema(CancellationToken ct) {
		var schemaName       = KurrentClientTestFixture.NewSchemaName();
		var schemaDefinition = KurrentClientTestFixture.NewJsonSchemaDefinition().ToJson();

		var schemaVersion = await Fixture.Client.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct)
			.ShouldNotThrowOrFailAsync(version => {
                version.VersionId.ShouldBeGuid();
                version.VersionNumber.ShouldBe(1);
            });

        await KurrentClientTestFixture.WaitForEventualConsistency(ct);

        await Fixture.Client.Registry
            .GetSchemaVersionById(schemaVersion.VersionId, ct)
            .ShouldNotThrowOrFailAsync(version => {
                version.VersionId.ShouldBe(schemaVersion.VersionId);
                version.VersionNumber.ShouldBe(1);
                version.SchemaDefinition.ShouldBe(schemaDefinition);
                version.DataFormat.ShouldBe(SchemaDataFormat.Json);
                version.RegisteredAt.ShouldBeGreaterThan(DateTimeOffset.MinValue);
            });
	}

	[Test]
	public async ValueTask fails_to_create_schema_when_schema_already_exists(CancellationToken ct) {
        var schemaName       = KurrentClientTestFixture.NewSchemaName();
        var schemaDefinition = KurrentClientTestFixture.NewJsonSchemaDefinition().ToJson();

		await Fixture.Client.Registry
			.CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct)
			.ShouldNotThrowOrFailAsync();

		await Fixture.Client.Registry
			.CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct)
			.ShouldFailAsync(failure => failure.Case.ShouldBe(CreateSchemaError.CreateSchemaErrorCase.SchemaAlreadyExistsException));
	}
}
