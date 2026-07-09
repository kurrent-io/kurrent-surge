using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Tests.Fixtures;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Tests.Registry;

[Category("Registry")]
[Category("Integration")]
public class DeleteSchemaTests {
	[ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask deletes_schema_throws_not_found_when_schema_does_not_exist(CancellationToken ct) {
		var schemaName = KurrentDBFixture.NewSchemaName();

		await Assert.ThrowsAsync<SchemaNotFoundException>(
			async () => await Fixture.Client.Registry.DeleteSchema(schemaName, ct));
	}

	[Test]
	public async ValueTask deletes_existing_schema(CancellationToken ct) {
		var schemaName       = KurrentDBFixture.NewSchemaName();
		var schemaDefinition = KurrentDBFixture.NewJsonSchemaDefinition().ToJson();

		await Fixture.Client.Registry
			.CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

		await Fixture.Client.Registry
			.DeleteSchema(schemaName, ct);
	}
}
