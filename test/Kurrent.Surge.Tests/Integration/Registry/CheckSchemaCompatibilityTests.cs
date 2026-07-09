using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Tests.Fixtures;
using Kurrent.Schema;
using NJsonSchema;
using static Kurrent.Surge.Client.Registry.CompatibilityMode;

namespace Kurrent.Surge.Tests.Registry;

[Category("Registry")]
[Category("Integration")]
public class CheckSchemaCompatibilityTests {
	[ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask check_schema_compatibility_schema_name_not_found(CancellationToken ct) {
		var schemaName       = SchemaName.From(KurrentDBFixture.NewSchemaName());
		var schemaDefinition = KurrentDBFixture.NewJsonSchemaDefinition().ToJson();

		await Assert.ThrowsAsync<SchemaNotFoundException>(
			async () => await Fixture.Client.Registry
				.CheckSchemaCompatibility(schemaName, schemaDefinition, SchemaDataFormat.Json, ct));
	}

	[Test]
	public async ValueTask check_schema_compatibility_backward_all_is_incompatible(CancellationToken ct) {
		var schemaName = KurrentDBFixture.NewSchemaName();

		var v1 = KurrentDBFixture
			.NewJsonSchemaDefinition()
			.AddOptional("gender", JsonObjectType.String)
			.AddOptional("email", JsonObjectType.String);

		var v2 = v1
			.AddRequired("age", JsonObjectType.Integer)
			.MakeRequired("email")
			.ChangeType("gender", JsonObjectType.Integer);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, BackwardAll, ct);

		var ex = (await Assert.That(async () => await Assert.ThrowsAsync<SchemaIncompatibleException>(
				async () => await Fixture.Client.Registry
					.CheckSchemaCompatibility(SchemaName.From(schemaName), v2.ToJson(), SchemaDataFormat.Json, ct)))
			.EventuallyPasses(s => s.IsNotNull(), RetryOnTransients(ct)))!;

		var errors = ex.Errors;
		await Assert.That(errors.Errors.Count).IsEqualTo(3);
		await Assert.That(errors.Errors).Contains(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
		await Assert.That(errors.Errors).Contains(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
		await Assert.That(errors.Errors).Contains(e => e.Kind == SchemaCompatibilityErrorKind.OptionalToRequired);
	}

	[Test]
	public async ValueTask check_schema_compatibility_backward_is_compatible(CancellationToken ct) {
		var schemaName = KurrentDBFixture.NewSchemaName();

		var v1 = KurrentDBFixture.NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("address", JsonObjectType.String);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, Backward, ct);

		await Assert.That(async () => { await Fixture.Client.Registry
				.CheckSchemaCompatibility(SchemaName.From(schemaName), v2.ToJson(), SchemaDataFormat.Json, ct); })
			.EventuallyPasses(RetryOnTransients(ct));
	}

	[Test]
	public async ValueTask check_schema_compatibility_backward_is_incompatible(CancellationToken ct) {
		var schemaName = KurrentDBFixture.NewSchemaName();

		var v1 = KurrentDBFixture.NewJsonSchemaDefinition();
		var v2 = v1.AddRequired("email", JsonObjectType.String);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, Backward, ct);

		var ex = (await Assert.That(async () => await Assert.ThrowsAsync<SchemaIncompatibleException>(
				async () => await Fixture.Client.Registry
					.CheckSchemaCompatibility(SchemaName.From(schemaName), v2.ToJson(), SchemaDataFormat.Json, ct)))
			.EventuallyPasses(s => s.IsNotNull(), RetryOnTransients(ct)))!;

		var errors = ex.Errors;
		await Assert.That(errors.Errors).Contains(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
	}

	[Test]
	public async ValueTask check_schema_compatibility_forward_is_compatible(CancellationToken ct) {
		var schemaName = KurrentDBFixture.NewSchemaName();

		var v1 = KurrentDBFixture
			.NewJsonSchemaDefinition()
			.AddOptional("email", JsonObjectType.String)
			.AddOptional("phone", JsonObjectType.String);

		var v2 = v1.Remove("phone");

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, Forward, ct);

		await Assert.That(async () => { await Fixture.Client.Registry
				.CheckSchemaCompatibility(SchemaName.From(schemaName), v2.ToJson(), SchemaDataFormat.Json, ct); })
			.EventuallyPasses(RetryOnTransients(ct));
	}

	[Test]
	public async ValueTask check_schema_compatibility_forward_is_incompatible(CancellationToken ct) {
		var schemaName = KurrentDBFixture.NewSchemaName();

		var v1 = KurrentDBFixture.NewJsonSchemaDefinition();
		var v2 = v1.ChangeType("id", JsonObjectType.Integer);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, Forward, ct);

		var ex = (await Assert.That(async () => await Assert.ThrowsAsync<SchemaIncompatibleException>(
				async () => await Fixture.Client.Registry
					.CheckSchemaCompatibility(SchemaName.From(schemaName), v2.ToJson(), SchemaDataFormat.Json, ct)))
			.EventuallyPasses(s => s.IsNotNull(), RetryOnTransients(ct)))!;

		var errors = ex.Errors;
		await Assert.That(errors.Errors).Contains(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
	}
}
