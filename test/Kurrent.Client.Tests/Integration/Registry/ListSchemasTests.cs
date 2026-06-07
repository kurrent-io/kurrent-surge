using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Registry;

[Category("Registry")]
[Category("Integration")]
public class ListSchemasTests {
	[ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask lists_schemas_returns_created_schemas(CancellationToken ct) {
		var prefix           = $"list-test-{Guid.NewGuid():N}";
		var schemaName1      = $"{prefix}-schema-1";
		var schemaName2      = $"{prefix}-schema-2";
		var schemaDefinition = KurrentDBFixture.NewJsonSchemaDefinition().ToJson();

		await Fixture.Client.Registry
			.CreateSchema(schemaName1, schemaDefinition, SchemaDataFormat.Json, ct);

		await Fixture.Client.Registry
			.CreateSchema(schemaName2, schemaDefinition, SchemaDataFormat.Json, ct);

		await Assert.That(async () => {
			var schemas = await Fixture.Client.Registry
				.ListSchemas(prefix, ct);

			await Assert.That(schemas.Count).IsGreaterThanOrEqualTo(2);
			await Assert.That(schemas).Contains(s => s.SchemaName.Value == schemaName1);
			await Assert.That(schemas).Contains(s => s.SchemaName.Value == schemaName2);
		}).EventuallyPasses(RetryOnTransients(ct));
	}

	[Test]
	public async ValueTask lists_schemas_with_no_filter_returns_results(CancellationToken ct) {
		var schemaName       = KurrentDBFixture.NewSchemaName();
		var schemaDefinition = KurrentDBFixture.NewJsonSchemaDefinition().ToJson();

		await Fixture.Client.Registry
			.CreateSchema(schemaName, schemaDefinition, SchemaDataFormat.Json, ct);

		await Assert.That(async () => {
			var schemas = await Fixture.Client.Registry
				.ListSchemas(ct);

			await Assert.That(schemas.Count).IsGreaterThanOrEqualTo(1);
		}).EventuallyPasses(RetryOnTransients(ct));
	}

	[Test]
	public async ValueTask lists_schemas_filtered_by_prefix_excludes_non_matching(CancellationToken ct) {
		var prefix           = $"prefix-filter-{Guid.NewGuid():N}";
		var matchingName     = $"{prefix}-matching";
		var nonMatchingName  = KurrentDBFixture.NewSchemaName();
		var schemaDefinition = KurrentDBFixture.NewJsonSchemaDefinition().ToJson();

		await Fixture.Client.Registry
			.CreateSchema(matchingName, schemaDefinition, SchemaDataFormat.Json, ct);

		await Fixture.Client.Registry
			.CreateSchema(nonMatchingName, schemaDefinition, SchemaDataFormat.Json, ct);

		await Assert.That(async () => {
			var schemas = await Fixture.Client.Registry
				.ListSchemas(prefix, ct);

			await Assert.That(schemas).Contains(s => s.SchemaName.Value == matchingName);
			await Assert.That(schemas).DoesNotContain(s => s.SchemaName.Value == nonMatchingName);
		}).EventuallyPasses(RetryOnTransients(ct));
	}

	[Test]
	public async ValueTask lists_schemas_filtered_by_tags(CancellationToken ct) {
		var prefix           = $"tag-filter-{Guid.NewGuid():N}";
		var tagKey           = $"env-{Guid.NewGuid():N}";
		var tagValue         = "production";
		var tags             = new Dictionary<string, string> { [tagKey] = tagValue };
		var schemaDefinition = KurrentDBFixture.NewJsonSchemaDefinition().ToJson();

		var taggedName   = $"{prefix}-tagged";
		var untaggedName = $"{prefix}-untagged";

		await Fixture.Client.Registry
			.CreateSchema(taggedName, schemaDefinition, SchemaDataFormat.Json, tags, ct);

		await Fixture.Client.Registry
			.CreateSchema(untaggedName, schemaDefinition, SchemaDataFormat.Json, ct);

		await Assert.That(async () => {
			var schemas = await Fixture.Client.Registry
				.ListSchemas(prefix, tags, ct);

			await Assert.That(schemas).Contains(s => s.SchemaName.Value == taggedName);
			await Assert.That(schemas).DoesNotContain(s => s.SchemaName.Value == untaggedName);
		}).EventuallyPasses(RetryOnTransients(ct));
	}

	[Test]
	public async ValueTask lists_schemas_with_non_matching_prefix_returns_empty(CancellationToken ct) {
		var nonExistentPrefix = $"no-match-{Guid.NewGuid():N}";

		var schemas = await Fixture.Client.Registry
			.ListSchemas(nonExistentPrefix, ct);

		await Assert.That(schemas.Count).IsEqualTo(0);
	}
}
