using Kurrent.Client.Exceptions;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using NJsonSchema;
using Sdk.TestKit.Fixtures;
using static Kurrent.Client.Registry.CompatibilityMode;

namespace Kurrent.Client.Tests.Registry;

[Category("Registry")]
[Category("Integration")]
public class CheckSchemaCompatibilityTests {
	[ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask check_schema_compatibility_schema_name_not_found(CancellationToken ct) {
		var schemaName       = SchemaName.From(KurrentClientTestFixture.NewSchemaName());
		var schemaDefinition = KurrentClientTestFixture.NewJsonSchemaDefinition().ToJson();

		await Assert.ThrowsAsync<SchemaNotFoundException>(
			async () => await Fixture.Client.Registry
				.CheckSchemaCompatibility(schemaName, schemaDefinition, SchemaDataFormat.Json, ct));
	}

	[Test]
	public async ValueTask check_schema_compatibility_backward_all_is_incompatible(CancellationToken ct) {
		var schemaName = KurrentClientTestFixture.NewSchemaName();

		var v1 = KurrentClientTestFixture
			.NewJsonSchemaDefinition()
			.AddOptional("gender", JsonObjectType.String)
			.AddOptional("email", JsonObjectType.String);

		var v2 = v1
			.AddRequired("age", JsonObjectType.Integer)
			.MakeRequired("email")
			.ChangeType("gender", JsonObjectType.Integer);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, BackwardAll, cancellationToken: ct);

		await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		var ex = await Assert.ThrowsAsync<SchemaIncompatibleException>(
			async () => await Fixture.Client.Registry
				.CheckSchemaCompatibility(SchemaName.From(schemaName), v2.ToJson(), SchemaDataFormat.Json, ct));

		var errors = ex.Errors;
		await Assert.That(errors.Errors.Count).IsEqualTo(3);
		await Assert.That(errors.Errors).Contains(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
		await Assert.That(errors.Errors).Contains(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
		await Assert.That(errors.Errors).Contains(e => e.Kind == SchemaCompatibilityErrorKind.OptionalToRequired);
	}

	[Test]
	public async ValueTask check_schema_compatibility_backward_is_compatible(CancellationToken ct) {
		var schemaName = KurrentClientTestFixture.NewSchemaName();

		var v1 = KurrentClientTestFixture.NewJsonSchemaDefinition();
		var v2 = v1.AddOptional("address", JsonObjectType.String);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, Backward, cancellationToken: ct);

		await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		await Fixture.Client.Registry
			.CheckSchemaCompatibility(SchemaName.From(schemaName), v2.ToJson(), SchemaDataFormat.Json, ct);
	}

	[Test]
	public async ValueTask check_schema_compatibility_backward_is_incompatible(CancellationToken ct) {
		var schemaName = KurrentClientTestFixture.NewSchemaName();

		var v1 = KurrentClientTestFixture.NewJsonSchemaDefinition();
		var v2 = v1.AddRequired("email", JsonObjectType.String);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, Backward, cancellationToken: ct);

		await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		var ex = await Assert.ThrowsAsync<SchemaIncompatibleException>(
			async () => await Fixture.Client.Registry
				.CheckSchemaCompatibility(SchemaName.From(schemaName), v2.ToJson(), SchemaDataFormat.Json, ct));

		var errors = ex.Errors;
		await Assert.That(errors.Errors).Contains(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
	}

	[Test]
	public async ValueTask check_schema_compatibility_forward_is_compatible(CancellationToken ct) {
		var schemaName = KurrentClientTestFixture.NewSchemaName();

		var v1 = KurrentClientTestFixture
			.NewJsonSchemaDefinition()
			.AddOptional("email", JsonObjectType.String)
			.AddOptional("phone", JsonObjectType.String);

		var v2 = v1.Remove("phone");

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, Forward, cancellationToken: ct);

		await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		await Fixture.Client.Registry
			.CheckSchemaCompatibility(SchemaName.From(schemaName), v2.ToJson(), SchemaDataFormat.Json, ct);
	}

	[Test]
	public async ValueTask check_schema_compatibility_forward_is_incompatible(CancellationToken ct) {
		var schemaName = KurrentClientTestFixture.NewSchemaName();

		var v1 = KurrentClientTestFixture.NewJsonSchemaDefinition();
		var v2 = v1.ChangeType("id", JsonObjectType.Integer);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1.ToJson(), SchemaDataFormat.Json, Forward, cancellationToken: ct);

		await KurrentClientTestFixture.WaitForEventualConsistency(ct);

		var ex = await Assert.ThrowsAsync<SchemaIncompatibleException>(
			async () => await Fixture.Client.Registry
				.CheckSchemaCompatibility(SchemaName.From(schemaName), v2.ToJson(), SchemaDataFormat.Json, ct));

		var errors = ex.Errors;
		await Assert.That(errors.Errors).Contains(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
	}
}
