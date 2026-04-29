using Kurrent.Client.Exceptions;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Sdk.Testkit.Fixtures;

namespace Kurrent.Client.Tests.Registry;

[Category("Registry")]
[Category("Integration")]
public class DeleteSchemaTests {
	[ClassDataSource<KurrentDBTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentDBTestFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask deletes_schema_throws_not_found_when_schema_does_not_exist(CancellationToken ct) {
		var schemaName = KurrentDBTestFixture.NewSchemaName();

		await Assert.ThrowsAsync<SchemaNotFoundException>(
			async () => await Fixture.Client.Registry.DeleteSchema(schemaName, ct));
	}

	[Test]
	public async ValueTask deletes_existing_schema(CancellationToken ct) {
		var schemaName       = KurrentDBTestFixture.NewSchemaName();
		var schemaDefinition = KurrentDBTestFixture.NewJsonSchemaDefinition().ToJson();

		await Fixture.Client.Registry
			.CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

		await Fixture.Client.Registry
			.DeleteSchema(schemaName, ct);
	}
}
