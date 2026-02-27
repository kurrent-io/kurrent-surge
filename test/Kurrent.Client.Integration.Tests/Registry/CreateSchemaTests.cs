using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Testing.Assertions;

namespace Kurrent.Client.Tests.Registry;

public class CreateSchemaTests : KurrentClientTestFixture {
	const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task registers_initial_version_of_new_schema(CancellationToken ct) {
		var schemaName       = NewSchemaName();
		var schemaDefinition = NewJsonSchemaDefinition().ToJson();

		var schemaVersion = await AutomaticClient.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

		await Assert.That(schemaVersion.VersionId).IsGuid();
		await Assert.That(schemaVersion.VersionNumber).IsEqualTo(1);

        var version = await AutomaticClient.Registry
            .GetSchemaVersionById(schemaVersion.VersionId, ct);

		await Assert.That(version.VersionId).IsEqualTo(schemaVersion.VersionId);
		await Assert.That(version.VersionNumber).IsEqualTo(1);
		await Assert.That(version.SchemaDefinition).IsEqualTo(schemaDefinition);
		await Assert.That(version.DataFormat).IsEqualTo(SchemaDataFormat.Json);
		await Assert.That(version.RegisteredAt).IsGreaterThan(DateTimeOffset.MinValue);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task fails_to_create_schema_when_schema_already_exists(CancellationToken ct) {
        var schemaName       = NewSchemaName();
        var schemaDefinition = NewJsonSchemaDefinition().ToJson();

		await AutomaticClient.Registry
			.CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

		await Assert.That(async () => await AutomaticClient.Registry
			.CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct))
			.ThrowsException();
	}
}
