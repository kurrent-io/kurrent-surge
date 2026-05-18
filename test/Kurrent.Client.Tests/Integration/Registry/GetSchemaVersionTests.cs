using Kurrent.Client.Exceptions;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Registry;

[Category("Registry")]
[Category("Integration")]
public class GetSchemaVersionTests {
	[ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask get_latest_schema_by_name(CancellationToken ct) {
		var schemaName       = KurrentDBFixture.NewSchemaName();
        var schemaDefinition = KurrentDBFixture.NewJsonSchemaDefinition().ToJson();

        await Fixture.Client.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

        await KurrentDBFixture.WaitForEventualConsistency(ct);

		var version = await Fixture.Client.Registry
			.GetSchemaVersion(schemaName, ct);

		await Assert.That(version.VersionId).IsNotEqualTo(SchemaVersionId.Unset);
		await Assert.That(version.VersionNumber).IsEqualTo(1);
		await Assert.That(version.SchemaDefinition).IsEqualTo(schemaDefinition);
		await Assert.That(version.DataFormat).IsEqualTo(SchemaDataFormat.Json);
		await Assert.That(version.RegisteredAt).IsGreaterThan(DateTimeOffset.MinValue);
	}

	[Test]
	public async ValueTask get_schema_by_name_and_version(CancellationToken ct) {
        var schemaName       = KurrentDBFixture.NewSchemaName();
        var schemaDefinition = KurrentDBFixture.NewJsonSchemaDefinition().ToJson();

		var schemaVersion = await Fixture.Client.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

        await KurrentDBFixture.WaitForEventualConsistency(ct);

		var version = await Fixture.Client.Registry
			.GetSchemaVersion(schemaName, schemaVersion.VersionNumber, ct);

		await Assert.That(version.VersionId).IsNotEqualTo(SchemaVersionId.Unset);
		await Assert.That(version.VersionNumber).IsEqualTo(1);
		await Assert.That(version.SchemaDefinition).IsEqualTo(schemaDefinition);
		await Assert.That(version.DataFormat).IsEqualTo(SchemaDataFormat.Json);
		await Assert.That(version.RegisteredAt).IsGreaterThan(DateTimeOffset.MinValue);
	}

	[Test]
	public async ValueTask get_schema_by_id(CancellationToken ct) {
		var schemaName       = KurrentDBFixture.NewSchemaName();
        var schemaDefinition = KurrentDBFixture.NewJsonSchemaDefinition().ToJson();

		var schemaVersion = await Fixture.Client.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

        await KurrentDBFixture.WaitForEventualConsistency(ct);

		var version = await Fixture.Client.Registry
			.GetSchemaVersionById(schemaVersion.VersionId, ct);

		await Assert.That(version.VersionId).IsNotEqualTo(SchemaVersionId.Unset);
		await Assert.That(version.VersionNumber).IsEqualTo(1);
		await Assert.That(version.SchemaDefinition).IsEqualTo(schemaDefinition);
		await Assert.That(version.DataFormat).IsEqualTo(SchemaDataFormat.Json);
		await Assert.That(version.RegisteredAt).IsGreaterThan(DateTimeOffset.MinValue);
	}

	[Test]
	public async ValueTask get_schema_by_name_fails_on_unknown_name(CancellationToken ct) {
		var nonExistentSchemaName = KurrentDBFixture.NewSchemaName();

		await Assert.ThrowsAsync<SchemaNotFoundException>(
			async () => await Fixture.Client.Registry
				.GetSchemaVersion(nonExistentSchemaName, ct));
	}

	[Test]
	public async ValueTask get_schema_by_name_fails_on_invalid_version(CancellationToken ct) {
		var schemaName       = KurrentDBFixture.NewSchemaName();
        var schemaDefinition = KurrentDBFixture.NewJsonSchemaDefinition().ToJson();

        await Fixture.Client.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

        await KurrentDBFixture.WaitForEventualConsistency(ct);

		const int nonExistentVersionNumber = 999;

		await Assert.ThrowsAsync<SchemaNotFoundException>(
			async () => await Fixture.Client.Registry
				.GetSchemaVersion(schemaName, nonExistentVersionNumber, ct));
	}

	[Test]
	public async ValueTask get_schema_by_id_fails_on_unknown_id(CancellationToken ct) {
		var nonExistentVersionId = SchemaVersionId.From(Guid.NewGuid());

		await Assert.ThrowsAsync<SchemaNotFoundException>(
			async () => await Fixture.Client.Registry
				.GetSchemaVersionById(nonExistentVersionId, ct));
	}
}
