using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testing.Shouldly;

namespace Kurrent.Client.Tests.Registry;

[Category("Registry")]
[Category("Integration")]
public class DeleteSchemaTests {
	[ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask deletes_schema_throws_not_found_when_schema_does_not_exist(CancellationToken ct) {
		var schemaName = KurrentClientTestFixture.NewSchemaName();

		await Fixture.Client.Registry
			.DeleteSchema(schemaName, ct)
            .ShouldFailAsync(failure =>
                failure.Case.ShouldBe(DeleteSchemaError.DeleteSchemaErrorCase.SchemaNotFoundException));
	}

	[Test]
	public async ValueTask deletes_existing_schema(CancellationToken ct) {
		var schemaName       = KurrentClientTestFixture.NewSchemaName();
		var schemaDefinition = KurrentClientTestFixture.NewJsonSchemaDefinition().ToJson();

		await Fixture.Client.Registry
			.CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct)
			.ShouldNotThrowOrFailAsync();

		await Fixture.Client.Registry
			.DeleteSchema(schemaName, ct)
			.ShouldNotThrowOrFailAsync();
	}
}
