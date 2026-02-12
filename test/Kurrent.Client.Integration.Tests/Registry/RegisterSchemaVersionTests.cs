using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testing.Shouldly;
using NJsonSchema;

namespace Kurrent.Client.Tests.Registry;

[Category("Registry")]
[Category("Integration")]
public class RegisterSchemaVersionTests {
	[ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask registers_new_version_of_existing_schema(CancellationToken ct) {
		var schemaName = KurrentClientTestFixture.NewSchemaName();
		var v1         = KurrentClientTestFixture.NewJsonSchemaDefinition();
		var v2         = v1.AddOptional("email", JsonObjectType.String);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, ct)
			.ShouldNotThrowOrFailAsync(version => version.VersionNumber.ShouldBe(1));

		await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		await Fixture.Client.Registry
			.RegisterSchemaVersion(schemaName, v2.ToJson(), ct)
			.ShouldNotThrowOrFailAsync(version => {
				version.VersionId.ShouldBeGuid();
				version.VersionNumber.ShouldBe(2);
			});
	}

	[Test]
	public async ValueTask registers_multiple_versions_incrementing_version_number(CancellationToken ct) {
		var schemaName = KurrentClientTestFixture.NewSchemaName();
		var v1         = KurrentClientTestFixture.NewJsonSchemaDefinition();
		var v2         = v1.AddOptional("email", JsonObjectType.String);
		var v3         = v2.AddOptional("phone", JsonObjectType.String);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, ct)
			.ShouldNotThrowOrFailAsync();

		await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		await Fixture.Client.Registry
			.RegisterSchemaVersion(schemaName, v2.ToJson(), ct)
			.ShouldNotThrowOrFailAsync(version => version.VersionNumber.ShouldBe(2));

		await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		await Fixture.Client.Registry
			.RegisterSchemaVersion(schemaName, v3.ToJson(), ct)
			.ShouldNotThrowOrFailAsync(version => version.VersionNumber.ShouldBe(3));
	}

	[Test]
	public async ValueTask registered_version_is_retrievable(CancellationToken ct) {
		var schemaName = KurrentClientTestFixture.NewSchemaName();
		var v1         = KurrentClientTestFixture.NewJsonSchemaDefinition();
		var v2         = v1.AddOptional("address", JsonObjectType.String);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, ct)
			.ShouldNotThrowOrFailAsync();

		await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		var registered = await Fixture.Client.Registry
			.RegisterSchemaVersion(schemaName, v2.ToJson(), ct)
			.ShouldNotThrowOrFailAsync();

		await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		await Fixture.Client.Registry
			.GetSchemaVersionById(registered.VersionId, ct)
			.ShouldNotThrowOrFailAsync(version => {
				version.VersionId.ShouldBe(registered.VersionId);
				version.VersionNumber.ShouldBe(2);
				version.SchemaDefinition.ShouldBe(v2.ToJson());
				version.DataFormat.ShouldBe(SchemaDataFormat.Json);
				version.RegisteredAt.ShouldBeGreaterThan(DateTimeOffset.MinValue);
			});
	}

	[Test]
	public async ValueTask fails_when_schema_does_not_exist(CancellationToken ct) {
		var schemaName       = KurrentClientTestFixture.NewSchemaName();
		var schemaDefinition = KurrentClientTestFixture.NewJsonSchemaDefinition().ToJson();

		await Fixture.Client.Registry
			.RegisterSchemaVersion(schemaName, schemaDefinition, ct)
			.ShouldFailAsync(failure =>
				failure.Case.ShouldBe(RegisterSchemaVersionError.RegisterSchemaVersionErrorCase.SchemaNotFoundException));
	}

	[Test]
	public async ValueTask fails_when_schema_is_deleted(CancellationToken ct) {
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
			.RegisterSchemaVersion(schemaName, schemaDefinition, ct)
			.ShouldFailAsync(failure =>
				failure.Case.ShouldBe(RegisterSchemaVersionError.RegisterSchemaVersionErrorCase.SchemaNotFoundException));
	}

	[Test]
	public async ValueTask updates_schema_latest_version_after_registration(CancellationToken ct) {
		var schemaName = KurrentClientTestFixture.NewSchemaName();
		var v1         = KurrentClientTestFixture.NewJsonSchemaDefinition();
		var v2         = v1.AddOptional("age", JsonObjectType.Integer);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, ct)
			.ShouldNotThrowOrFailAsync();

		await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		await Fixture.Client.Registry
			.RegisterSchemaVersion(schemaName, v2.ToJson(), ct)
			.ShouldNotThrowOrFailAsync();

		await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		await Fixture.Client.Registry
			.GetSchema(schemaName, ct)
			.ShouldNotThrowOrFailAsync(schema =>
				schema.LatestSchemaVersion.ShouldBe(2));
	}
}
