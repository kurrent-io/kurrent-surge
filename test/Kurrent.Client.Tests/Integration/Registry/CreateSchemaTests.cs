using Kurrent.Client.Exceptions;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Sdk.TestKit.Fixtures;

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
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

		await Assert.That(schemaVersion.VersionId).IsNotEqualTo(SchemaVersionId.Unset);
		await Assert.That(schemaVersion.VersionNumber).IsEqualTo(1);

        await KurrentClientTestFixture.WaitForEventualConsistency(ct);

        var version = await Fixture.Client.Registry
            .GetSchemaVersionById(schemaVersion.VersionId, ct);

		await Assert.That(version.VersionId).IsEqualTo(schemaVersion.VersionId);
		await Assert.That(version.VersionNumber).IsEqualTo(1);
		await Assert.That(version.SchemaDefinition).IsEqualTo(schemaDefinition);
		await Assert.That(version.DataFormat).IsEqualTo(SchemaDataFormat.Json);
		await Assert.That(version.RegisteredAt).IsGreaterThan(DateTimeOffset.MinValue);
	}

	[Test]
	public async ValueTask fails_to_create_schema_when_schema_already_exists(CancellationToken ct) {
        var schemaName       = KurrentClientTestFixture.NewSchemaName();
        var schemaDefinition = KurrentClientTestFixture.NewJsonSchemaDefinition().ToJson();

		await Fixture.Client.Registry
			.CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

		await Assert.ThrowsAsync<SchemaAlreadyExistsException>(
			async () => await Fixture.Client.Registry
				.CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct));
	}
}
