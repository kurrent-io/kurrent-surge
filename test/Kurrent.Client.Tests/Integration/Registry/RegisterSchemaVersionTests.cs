using Kurrent.Client.Exceptions;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using NJsonSchema;

namespace Kurrent.Client.Tests.Registry;

[Category("Registry")]
[Category("Integration")]
public class RegisterSchemaVersionTests {
	[ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask registers_new_version_of_existing_schema(CancellationToken ct) {
		var schemaName = KurrentDBFixture.NewSchemaName();
		var v1         = KurrentDBFixture.NewJsonSchemaDefinition();
		var v2         = v1.AddOptional("email", JsonObjectType.String);

		var created = await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, ct);

		await Assert.That(created.VersionNumber).IsEqualTo(1);

		var registered = await Fixture.Client.Registry
			.RegisterSchemaVersion(schemaName, v2.ToJson(), ct);

		await Assert.That(registered.VersionId).IsNotEqualTo(SchemaVersionId.Unset);
		await Assert.That(registered.VersionNumber).IsEqualTo(2);
	}

	[Test]
	public async ValueTask registers_multiple_versions_incrementing_version_number(CancellationToken ct) {
		var schemaName = KurrentDBFixture.NewSchemaName();
		var v1         = KurrentDBFixture.NewJsonSchemaDefinition();
		var v2         = v1.AddOptional("email", JsonObjectType.String);
		var v3         = v2.AddOptional("phone", JsonObjectType.String);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, ct);

		var reg2 = await Fixture.Client.Registry
			.RegisterSchemaVersion(schemaName, v2.ToJson(), ct);

		await Assert.That(reg2.VersionNumber).IsEqualTo(2);

		var reg3 = await Fixture.Client.Registry
			.RegisterSchemaVersion(schemaName, v3.ToJson(), ct);

		await Assert.That(reg3.VersionNumber).IsEqualTo(3);
	}

	[Test]
	public async ValueTask registered_version_is_retrievable(CancellationToken ct) {
		var schemaName = KurrentDBFixture.NewSchemaName();
		var v1         = KurrentDBFixture.NewJsonSchemaDefinition();
		var v2         = v1.AddOptional("address", JsonObjectType.String);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, ct);

		var registered = await Fixture.Client.Registry
			.RegisterSchemaVersion(schemaName, v2.ToJson(), ct);

		await Eventually(async () => {
			var version = await Fixture.Client.Registry
				.GetSchemaVersionById(registered.VersionId, ct);

			await Assert.That(version.VersionId).IsEqualTo(registered.VersionId);
			await Assert.That(version.VersionNumber).IsEqualTo(2);
			await Assert.That(version.SchemaDefinition).IsEqualTo(v2.ToJson());
			await Assert.That(version.DataFormat).IsEqualTo(SchemaDataFormat.Json);
			await Assert.That(version.RegisteredAt).IsGreaterThan(DateTimeOffset.MinValue);
		}, ct: ct);
	}

	[Test]
	public async ValueTask fails_when_schema_does_not_exist(CancellationToken ct) {
		var schemaName       = KurrentDBFixture.NewSchemaName();
		var schemaDefinition = KurrentDBFixture.NewJsonSchemaDefinition().ToJson();

		await Assert.ThrowsAsync<SchemaNotFoundException>(
			async () => await Fixture.Client.Registry
				.RegisterSchemaVersion(schemaName, schemaDefinition, ct));
	}

	[Test]
	public async ValueTask fails_when_schema_is_deleted(CancellationToken ct) {
		var schemaName       = KurrentDBFixture.NewSchemaName();
		var schemaDefinition = KurrentDBFixture.NewJsonSchemaDefinition().ToJson();

		await Fixture.Client.Registry
			.CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

		await Fixture.Client.Registry
			.DeleteSchema(schemaName, ct);

		await Assert.ThrowsAsync<SchemaNotFoundException>(
			async () => await Fixture.Client.Registry
				.RegisterSchemaVersion(schemaName, schemaDefinition, ct));
	}

	[Test]
	public async ValueTask updates_schema_latest_version_after_registration(CancellationToken ct) {
		var schemaName = KurrentDBFixture.NewSchemaName();
		var v1         = KurrentDBFixture.NewJsonSchemaDefinition();
		var v2         = v1.AddOptional("age", JsonObjectType.Integer);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, ct);

		await Fixture.Client.Registry
			.RegisterSchemaVersion(schemaName, v2.ToJson(), ct);

		await Eventually(async () => {
			var schema = await Fixture.Client.Registry
				.GetSchema(schemaName, ct);

			await Assert.That(schema.LatestSchemaVersion).IsEqualTo(2);
		}, ct: ct);
	}
}
