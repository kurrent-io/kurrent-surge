using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Streams;
using Kurrent.Schema;
using Kurrent.Surge.Tests.Fixtures;
using static Kurrent.Surge.Client.Registry.CompatibilityMode;

// Deliberately outside the Kurrent.Surge.Tests.Registry namespace: that whole namespace is
// blanket-skipped in TestkitBootstrap.cs ("Flaky tests"). This is a proof harness that must
// actually run against the live server.
namespace Kurrent.Surge.Tests.SchemaExportProof;

/// <summary>
/// Proves the in-box <see cref="SystemJsonSchemaExporter"/> (the AOT-safe candidate replacing NJsonSchema)
/// produces schemas the KurrentDB registry accepts and compat-checks correctly, and contrasts the
/// two `required` models the exporter supports:
///   - lenient (default): nullable members are optional  — matches the registry's NJsonSchema semantics
///   - strict: a member is required unless it has a default value — STJ's native behaviour
/// </summary>
[Category("Registry")]
[Category("Integration")]
public class SystemJsonSchemaExporterCompatibilityTests {
	[ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

	static readonly ISchemaExporter Lenient    = new SystemJsonSchemaExporter(nullableIsOptional: true);
	static readonly ISchemaExporter Strict     = new SystemJsonSchemaExporter(nullableIsOptional: false);
	static readonly ISchemaExporter OneOfEnums = new SystemJsonSchemaExporter(enumAsOneOf: true);

	static string Export<T>(ISchemaExporter exporter) => exporter.Export(typeof(T), SchemaDataFormat.Json);

	[Test]
	public async ValueTask server_accepts_stj_generated_schema(CancellationToken ct) {
		// Arrange
		var schemaName = KurrentDBFixture.NewSchemaName();
		var definition = Export<PersonV1>(Lenient);

		// Act
		var created = await Fixture.Client.Registry
			.CreateSchema(schemaName, definition, SchemaDataFormat.Json, ct);

		// Assert — server parsed and registered the STJ-generated schema
		await Assert.That(created.VersionNumber).IsEqualTo(1);
	}

	[Test]
	public async ValueTask backward_adding_field_with_default_is_compatible(CancellationToken ct) {
		// Arrange — Email has a default, so it is optional under BOTH models
		var schemaName = KurrentDBFixture.NewSchemaName();
		var v1         = Export<PersonV1>(Lenient);
		var v2         = Export<PersonWithDefaultedEmail>(Lenient);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1, SchemaDataFormat.Json, Backward, ct);

		// Act + Assert — optional addition is backward-compatible (must not throw)
		await Assert.That(async () => { await Fixture.Client.Registry
				.CheckSchemaCompatibility(SchemaName.From(schemaName), v2, SchemaDataFormat.Json, ct); })
			.EventuallyPasses(RetryOnTransients(ct));
	}

	[Test]
	public async ValueTask backward_adding_required_field_is_incompatible(CancellationToken ct) {
		// Arrange — proves the server reads STJ's `required` array
		var schemaName        = KurrentDBFixture.NewSchemaName();
		var v1                = Export<PersonV1>(Lenient);
		var v2                = Export<PersonWithRequiredEmail>(Lenient); // non-nullable, no default -> required
		var expectedErrorKind = SchemaCompatibilityErrorKind.NewRequiredProperty;

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1, SchemaDataFormat.Json, Backward, ct);

		// Act
		var ex = (await Assert.That(async () => await Assert.ThrowsAsync<SchemaIncompatibleException>(
				async () => await Fixture.Client.Registry
					.CheckSchemaCompatibility(SchemaName.From(schemaName), v2, SchemaDataFormat.Json, ct)))
			.EventuallyPasses(s => s.IsNotNull(), RetryOnTransients(ct)))!;

		// Assert
		await Assert.That(ex.Errors.Errors).Contains(e => e.Kind == expectedErrorKind);
	}

	[Test]
	public async ValueTask forward_incompatible_type_change_is_detected(CancellationToken ct) {
		// Arrange — proves the server reads STJ's property `type`
		var schemaName        = KurrentDBFixture.NewSchemaName();
		var v1                = Export<PersonV1>(Lenient);      // Id is string
		var v2                = Export<PersonWithIntId>(Lenient); // Id is int
		var expectedErrorKind = SchemaCompatibilityErrorKind.IncompatibleTypeChange;

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1, SchemaDataFormat.Json, Forward, ct);

		// Act
		var ex = (await Assert.That(async () => await Assert.ThrowsAsync<SchemaIncompatibleException>(
				async () => await Fixture.Client.Registry
					.CheckSchemaCompatibility(SchemaName.From(schemaName), v2, SchemaDataFormat.Json, ct)))
			.EventuallyPasses(s => s.IsNotNull(), RetryOnTransients(ct)))!;

		// Assert
		await Assert.That(ex.Errors.Errors).Contains(e => e.Kind == expectedErrorKind);
	}

	#region ->> Both models: nullable member WITHOUT a default <<-

	[Test]
	public async ValueTask backward_adding_nullable_field_is_compatible_under_lenient_model(CancellationToken ct) {
		// Arrange — string? Email with NO default. Lenient model treats it as optional.
		var schemaName = KurrentDBFixture.NewSchemaName();
		var v1         = Export<PersonV1>(Lenient);
		var v2         = Export<PersonWithNullableEmail>(Lenient);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1, SchemaDataFormat.Json, Backward, ct);

		// Act + Assert — under the lenient model this is backward-compatible (must not throw)
		await Assert.That(async () => { await Fixture.Client.Registry
				.CheckSchemaCompatibility(SchemaName.From(schemaName), v2, SchemaDataFormat.Json, ct); })
			.EventuallyPasses(RetryOnTransients(ct));
	}

	[Test]
	public async ValueTask backward_adding_nullable_field_is_incompatible_under_strict_model(CancellationToken ct) {
		// Arrange — same type, but the strict model marks string? (no default) as required,
		// so the server reports it as a new required property.
		var schemaName        = KurrentDBFixture.NewSchemaName();
		var v1                = Export<PersonV1>(Strict);
		var v2                = Export<PersonWithNullableEmail>(Strict);
		var expectedErrorKind = SchemaCompatibilityErrorKind.NewRequiredProperty;

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1, SchemaDataFormat.Json, Backward, ct);

		// Act
		var ex = (await Assert.That(async () => await Assert.ThrowsAsync<SchemaIncompatibleException>(
				async () => await Fixture.Client.Registry
					.CheckSchemaCompatibility(SchemaName.From(schemaName), v2, SchemaDataFormat.Json, ct)))
			.EventuallyPasses(s => s.IsNotNull(), RetryOnTransients(ct)))!;

		// Assert
		await Assert.That(ex.Errors.Errors).Contains(e => e.Kind == expectedErrorKind);
	}

	#endregion

	#region ->> Spec-idiomatic oneOf enums against the server <<-

	[Test]
	public async ValueTask server_accepts_oneof_enum_schema(CancellationToken ct) {
		// Arrange — enum represented as oneOf const/title branches (spec-idiomatic labelled form)
		var schemaName = KurrentDBFixture.NewSchemaName();
		var definition = Export<OrderV1>(OneOfEnums);

		// Act
		var created = await Fixture.Client.Registry
			.CreateSchema(schemaName, definition, SchemaDataFormat.Json, ct);

		// Assert
		await Assert.That(created.VersionNumber).IsEqualTo(1);
	}

	[Test]
	public async ValueTask backward_adding_enum_member_is_compatible_with_oneof_enums(CancellationToken ct) {
		// Arrange — v2 widens the value set ({0..4} -> {0..7}); new schema can still read old data
		var schemaName = KurrentDBFixture.NewSchemaName();
		var v1         = Export<OrderV1>(OneOfEnums);
		var v2         = Export<OrderV2>(OneOfEnums);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1, SchemaDataFormat.Json, Backward, ct);

		// Act + Assert — must not throw
		await Assert.That(async () => { await Fixture.Client.Registry
				.CheckSchemaCompatibility(SchemaName.From(schemaName), v2, SchemaDataFormat.Json, ct); })
			.EventuallyPasses(RetryOnTransients(ct));
	}

	[Test, Skip("Server compat checker ignores enum value sets — unskip when DEV-1759 ships")]
	public async ValueTask backward_removing_enum_member_is_incompatible_with_oneof_enums(CancellationToken ct) {
		// Arrange — v2 narrows the value set ({0..7} -> {0..4}); old data may hold a removed value.
		// This probes whether the server's checker actually inspects oneOf const branches.
		var schemaName = KurrentDBFixture.NewSchemaName();
		var v1         = Export<OrderV2>(OneOfEnums);
		var v2         = Export<OrderV1>(OneOfEnums);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1, SchemaDataFormat.Json, Backward, ct);

		// Act + Assert
		await Assert.That(async () => { await Assert.ThrowsAsync<SchemaIncompatibleException>(
				async () => await Fixture.Client.Registry
					.CheckSchemaCompatibility(SchemaName.From(schemaName), v2, SchemaDataFormat.Json, ct)); })
			.EventuallyPasses(RetryOnTransients(ct));
	}

	[Test]
	public async ValueTask server_accepts_default_enum_schema(CancellationToken ct) {
		// Arrange — default representation: enum:[0..4] + x-enumNames
		var schemaName = KurrentDBFixture.NewSchemaName();
		var definition = Export<OrderV1>(Lenient);

		// Act
		var created = await Fixture.Client.Registry
			.CreateSchema(schemaName, definition, SchemaDataFormat.Json, ct);

		// Assert
		await Assert.That(created.VersionNumber).IsEqualTo(1);
	}

	[Test]
	public async ValueTask backward_adding_enum_member_is_compatible_with_default_enums(CancellationToken ct) {
		// Arrange — v2 widens the value set ({0..4} -> {0..7}); new schema can still read old data
		var schemaName = KurrentDBFixture.NewSchemaName();
		var v1         = Export<OrderV1>(Lenient);
		var v2         = Export<OrderV2>(Lenient);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1, SchemaDataFormat.Json, Backward, ct);

		// Act + Assert — must not throw
		await Assert.That(async () => { await Fixture.Client.Registry
				.CheckSchemaCompatibility(SchemaName.From(schemaName), v2, SchemaDataFormat.Json, ct); })
			.EventuallyPasses(RetryOnTransients(ct));
	}

	[Test, Skip("Server compat checker ignores enum value sets — unskip when DEV-1759 ships")]
	public async ValueTask backward_removing_enum_member_is_incompatible_with_default_enums(CancellationToken ct) {
		// Arrange — same narrowing evolution, but under the default `enum:[0,1,2]` representation.
		// Contrast probe: does the server inspect the `enum` keyword where it ignored oneOf consts?
		var schemaName = KurrentDBFixture.NewSchemaName();
		var v1         = Export<OrderV2>(Lenient);
		var v2         = Export<OrderV1>(Lenient);

		await Fixture.Client.Registry
			.CreateSchema(schemaName, v1, SchemaDataFormat.Json, Backward, ct);

		// Act + Assert
		await Assert.That(async () => { await Assert.ThrowsAsync<SchemaIncompatibleException>(
				async () => await Fixture.Client.Registry
					.CheckSchemaCompatibility(SchemaName.From(schemaName), v2, SchemaDataFormat.Json, ct)); })
			.EventuallyPasses(RetryOnTransients(ct));
	}

	#endregion

	// Evolving shapes of the same logical message.
	record PersonV1(string Id, string Name);
	record PersonWithDefaultedEmail(string Id, string Name, string? Email = null); // optional under both models
	record PersonWithNullableEmail(string Id, string Name, string? Email);          // optional only under lenient
	record PersonWithRequiredEmail(string Id, string Name, string Email);           // required under both models
	record PersonWithIntId(int Id, string Name);

	// Same logical message, evolving enum value sets — built from REAL SDK enums whose numeric
	// ranges form a genuine subset: SchemaDataFormat = {0..4} ⊂ CompatibilityMode = {0..7}.
	// The names differ, but names are annotations (x-enumNames / title); the value sets are
	// what compatibility is about.
	record OrderV1(string Id, SchemaDataFormat Value);
	record OrderV2(string Id, CompatibilityMode Value);
}
