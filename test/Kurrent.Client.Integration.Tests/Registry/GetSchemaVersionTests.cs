using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Testing.Assertions;

namespace Kurrent.Client.Tests.Registry;

public class GetSchemaVersionTests : KurrentClientTestFixture  {
	const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_latest_schema_by_name(CancellationToken ct) {
		var schemaName       = NewSchemaName();
        var schemaDefinition = NewJsonSchemaDefinition().ToJson();

        await AutomaticClient.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

		var version = await AutomaticClient.Registry
			.GetSchemaVersion(schemaName, cancellationToken: ct);

		await Assert.That(version.VersionId).IsGuid();
		await Assert.That(version.VersionNumber).IsEqualTo(1);
		await Assert.That(version.SchemaDefinition).IsEqualTo(schemaDefinition);
		await Assert.That(version.DataFormat).IsEqualTo(SchemaDataFormat.Json);
		await Assert.That(version.RegisteredAt).IsGreaterThan(DateTimeOffset.MinValue);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema_by_name_and_version(CancellationToken ct) {
        var schemaName       = NewSchemaName();
        var schemaDefinition = NewJsonSchemaDefinition().ToJson();

		var schemaVersion = await AutomaticClient.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

		var version = await AutomaticClient.Registry
			.GetSchemaVersion(schemaName, schemaVersion.VersionNumber, cancellationToken: ct);

		await Assert.That(version.VersionId).IsGuid();
		await Assert.That(version.VersionNumber).IsEqualTo(1);
		await Assert.That(version.SchemaDefinition).IsEqualTo(schemaDefinition);
		await Assert.That(version.DataFormat).IsEqualTo(SchemaDataFormat.Json);
		await Assert.That(version.RegisteredAt).IsGreaterThan(DateTimeOffset.MinValue);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema_by_id(CancellationToken ct) {
		// Arrange
		var schemaName       = NewSchemaName();
        var schemaDefinition = NewJsonSchemaDefinition().ToJson();

		var schemaVersion = await AutomaticClient.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

		// Act & Assert
		var version = await AutomaticClient.Registry
			.GetSchemaVersionById(schemaVersion.VersionId, cancellationToken: ct);

		await Assert.That(version.VersionId).IsGuid();
		await Assert.That(version.VersionNumber).IsEqualTo(1);
		await Assert.That(version.SchemaDefinition).IsEqualTo(schemaDefinition);
		await Assert.That(version.DataFormat).IsEqualTo(SchemaDataFormat.Json);
		await Assert.That(version.RegisteredAt).IsGreaterThan(DateTimeOffset.MinValue);
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema_by_name_fails_on_unknown_name(CancellationToken ct) {
		// Arrange
		var nonExistentSchemaName = NewSchemaName();

		// Act & Assert
		await Assert.That(async () => await AutomaticClient.Registry
			.GetSchemaVersion(nonExistentSchemaName, cancellationToken: ct))
			.ThrowsException();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema_by_name_fails_on_invalid_version(CancellationToken ct) {
		// Arrange
		var schemaName       = NewSchemaName();
        var schemaDefinition = NewJsonSchemaDefinition().ToJson();

        await AutomaticClient.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

		const int nonExistentVersionNumber = 999;

		await Assert.That(async () => await AutomaticClient.Registry
			.GetSchemaVersion(schemaName, nonExistentVersionNumber, ct))
			.ThrowsException();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task get_schema_by_id_fails_on_unknown_id(CancellationToken ct) {
		var nonExistentVersionId = SchemaVersionId.From(Guid.NewGuid());

		await Assert.That(async () => await AutomaticClient.Registry
			.GetSchemaVersionById(nonExistentVersionId, cancellationToken: ct))
			.ThrowsException();
	}
}
