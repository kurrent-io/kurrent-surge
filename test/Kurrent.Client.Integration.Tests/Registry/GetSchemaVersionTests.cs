using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testing.Shouldly;

namespace Kurrent.Client.Tests.Registry;

[Category("Registry")]
[Category("Integration")]
public class GetSchemaVersionTests {
	[ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask get_latest_schema_by_name(CancellationToken ct) {
		var schemaName       = KurrentClientTestFixture.NewSchemaName();
        var schemaDefinition = KurrentClientTestFixture.NewJsonSchemaDefinition().ToJson();

        await Fixture.Client.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct)
            .ShouldNotThrowOrFailAsync();

        await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		await Fixture.Client.Registry
			.GetSchemaVersion(schemaName, cancellationToken: ct)
			.ShouldNotThrowOrFailAsync(version => {
				version.VersionId.ShouldBeGuid();
				version.VersionNumber.ShouldBe(1);
				version.SchemaDefinition.ShouldBe(schemaDefinition);
				version.DataFormat.ShouldBe(SchemaDataFormat.Json);
				version.RegisteredAt.ShouldBeGreaterThan(DateTimeOffset.MinValue);
			});
	}

	[Test]
	public async ValueTask get_schema_by_name_and_version(CancellationToken ct) {
        var schemaName       = KurrentClientTestFixture.NewSchemaName();
        var schemaDefinition = KurrentClientTestFixture.NewJsonSchemaDefinition().ToJson();

		var schemaVersion = await Fixture.Client.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct)
            .ShouldNotThrowOrFailAsync();

        await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		await Fixture.Client.Registry
			.GetSchemaVersion(schemaName, schemaVersion.VersionNumber, ct)
			.ShouldNotThrowOrFailAsync(version => {
				version.VersionId.ShouldBeGuid();
				version.VersionNumber.ShouldBe(1);
				version.SchemaDefinition.ShouldBe(schemaDefinition);
				version.DataFormat.ShouldBe(SchemaDataFormat.Json);
				version.RegisteredAt.ShouldBeGreaterThan(DateTimeOffset.MinValue);
			});
	}

	[Test]
	public async ValueTask get_schema_by_id(CancellationToken ct) {
		var schemaName       = KurrentClientTestFixture.NewSchemaName();
        var schemaDefinition = KurrentClientTestFixture.NewJsonSchemaDefinition().ToJson();

		var schemaVersion = await Fixture.Client.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct)
            .ShouldNotThrowOrFailAsync();

        await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		await Fixture.Client.Registry
			.GetSchemaVersionById(schemaVersion.VersionId, ct)
            .ShouldNotThrowOrFailAsync(version => {
                version.VersionId.ShouldBeGuid();
                version.VersionNumber.ShouldBe(1);
                version.SchemaDefinition.ShouldBe(schemaDefinition);
                version.DataFormat.ShouldBe(SchemaDataFormat.Json);
                version.RegisteredAt.ShouldBeGreaterThan(DateTimeOffset.MinValue);
            });
	}

	[Test]
	public async ValueTask get_schema_by_name_fails_on_unknown_name(CancellationToken ct) {
		var nonExistentSchemaName = KurrentClientTestFixture.NewSchemaName();

		await Fixture.Client.Registry
			.GetSchemaVersion(nonExistentSchemaName, cancellationToken: ct)
			.ShouldFailAsync(failure =>
                failure.Case.ShouldBe(GetSchemaVersionError.GetSchemaVersionErrorCase.SchemaNotFoundException));
	}

	[Test]
	public async ValueTask get_schema_by_name_fails_on_invalid_version(CancellationToken ct) {
		var schemaName       = KurrentClientTestFixture.NewSchemaName();
        var schemaDefinition = KurrentClientTestFixture.NewJsonSchemaDefinition().ToJson();

        await Fixture.Client.Registry
            .CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct)
            .ShouldNotThrowOrFailAsync();

        await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		const int nonExistentVersionNumber = 999;

		await Fixture.Client.Registry
			.GetSchemaVersion(schemaName, nonExistentVersionNumber, ct)
            .ShouldFailAsync(failure =>
                failure.Case.ShouldBe(GetSchemaVersionError.GetSchemaVersionErrorCase.SchemaNotFoundException));
	}

	[Test]
	public async ValueTask get_schema_by_id_fails_on_unknown_id(CancellationToken ct) {
		var nonExistentVersionId = SchemaVersionId.From(Guid.NewGuid());

		await Fixture.Client.Registry
			.GetSchemaVersionById(nonExistentVersionId, ct)
			.ShouldFailAsync(failure =>
                failure.Case.ShouldBe(GetSchemaVersionError.GetSchemaVersionErrorCase.SchemaNotFoundException));
	}
}
