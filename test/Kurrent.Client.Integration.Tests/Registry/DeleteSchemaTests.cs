using Kurrent.Client.Registry;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Registry;

public class DeleteSchemaTests : KurrentClientTestFixture {
	const int TestTimeoutMs = 20_000;

	[Test, Timeout(TestTimeoutMs)]
	public async Task deletes_schema_throws_not_found_when_schema_does_not_exist(CancellationToken ct) {
		var schemaName = NewSchemaName();

		await Assert.That(async () => await AutomaticClient.Registry
			.DeleteSchema(schemaName, ct))
			.ThrowsException();
	}

	[Test, Timeout(TestTimeoutMs)]
	public async Task deletes_existing_schema(CancellationToken ct) {
		var schemaName       = NewSchemaName();
		var schemaDefinition = NewJsonSchemaDefinition().ToJson();

		await AutomaticClient.Registry
			.CreateSchema(schemaName,schemaDefinition, SchemaDataFormat.Json, ct);

		await AutomaticClient.Registry
			.DeleteSchema(schemaName, ct);
	}
}
