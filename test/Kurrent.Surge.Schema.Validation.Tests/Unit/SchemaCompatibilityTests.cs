// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Surge.Schema.Validation.Tests;

public class SchemaCompatibilityTests {
    const string Reference = """
    {"type":"object","properties":{"id":{"type":"string"},"quantity":{"type":"integer"}},"required":["id"]}
    """;

    const string AddedOptional = """
    {"type":"object","properties":{"id":{"type":"string"},"quantity":{"type":"integer"},"note":{"type":"string"}},"required":["id"]}
    """;

    const string AddedRequired = """
    {"type":"object","properties":{"id":{"type":"string"},"quantity":{"type":"integer"},"note":{"type":"string"}},"required":["id","note"]}
    """;

    const string RemovedRequired = """
    {"type":"object","properties":{"quantity":{"type":"integer"}},"required":[]}
    """;

    const string RetypedProperty = """
    {"type":"object","properties":{"id":{"type":"integer"},"quantity":{"type":"integer"}},"required":["id"]}
    """;

    const string PromotedToRequired = """
    {"type":"object","properties":{"id":{"type":"string"},"quantity":{"type":"integer"}},"required":["id","quantity"]}
    """;

    [Test]
    public async ValueTask adding_an_optional_property_stays_backward_compatible() {
        // Arrange
        var expected = SchemaCompatibilityResult.Compatible().IsCompatible;

        // Act
        var result = Check(AddedOptional, Reference, SchemaCompatibilityMode.Backward);

        // Assert
        await Assert.That(result.IsCompatible).IsEqualTo(expected);
        await Assert.That(result.Errors).IsEmpty();
    }

    [Test]
    public async ValueTask adding_a_required_property_breaks_backward_compatibility() {
        // Arrange
        var expectedKind = SchemaCompatibilityErrorKind.NewRequiredProperty;

        // Act
        var result = Check(AddedRequired, Reference, SchemaCompatibilityMode.Backward);

        // Assert
        await Assert.That(result.IsCompatible).IsFalse();
        await Assert.That(result.Errors.Any(error => error.Kind == expectedKind && error.PropertyPath == "#/note")).IsTrue();
    }

    [Test]
    public async ValueTask removing_a_required_property_breaks_backward_compatibility() {
        // Arrange
        var expectedKind = SchemaCompatibilityErrorKind.MissingRequiredProperty;

        // Act
        var result = Check(RemovedRequired, Reference, SchemaCompatibilityMode.Backward);

        // Assert
        await Assert.That(result.IsCompatible).IsFalse();
        await Assert.That(result.Errors.Any(error => error.Kind == expectedKind && error.PropertyPath == "#/id")).IsTrue();
    }

    [Test]
    public async ValueTask promoting_an_optional_property_to_required_breaks_backward_compatibility() {
        // Arrange
        var expectedKind = SchemaCompatibilityErrorKind.OptionalToRequired;

        // Act
        var result = Check(PromotedToRequired, Reference, SchemaCompatibilityMode.Backward);

        // Assert
        await Assert.That(result.IsCompatible).IsFalse();
        await Assert.That(result.Errors.Any(error => error.Kind == expectedKind && error.PropertyPath == "#/quantity")).IsTrue();
    }

    [Test]
    public async ValueTask removing_a_required_property_breaks_forward_compatibility() {
        // Arrange
        var expectedKind = SchemaCompatibilityErrorKind.RemovedProperty;

        // Act
        var result = Check(RemovedRequired, Reference, SchemaCompatibilityMode.Forward);

        // Assert
        await Assert.That(result.IsCompatible).IsFalse();
        await Assert.That(result.Errors.Any(error => error.Kind == expectedKind && error.PropertyPath == "#/id")).IsTrue();
    }

    [Test]
    public async ValueTask a_type_change_reports_both_types_with_the_owned_enum() {
        // Arrange — "id" goes from string to integer. The two libraries assign different bit values
        // to the same type name, so a numeric cast anywhere in the mapping would surface here.
        var expected = new SchemaCompatibilityError {
            Kind         = SchemaCompatibilityErrorKind.IncompatibleTypeChange,
            PropertyPath = "#/id",
            Details      = "Property has incompatible type change",
            OriginalType = SchemaValueType.String,
            NewType      = SchemaValueType.Integer
        };

        // Act
        var result = Check(RetypedProperty, Reference, SchemaCompatibilityMode.Backward);

        // Assert
        await Assert.That(result.IsCompatible).IsFalse();
        await Assert.That(result.Errors).Contains(expected);
    }

    [Test]
    public async ValueTask the_njsonschema_agent_reports_the_same_type_change_with_the_owned_enum() {
        // Arrange
        var reference   = NJsonSchemaAgent.Instance.ParseSchema(Reference);
        var uncheckedOne = NJsonSchemaAgent.Instance.ParseSchema(RetypedProperty);

        var expectedOriginalType = SchemaValueType.String;
        var expectedNewType      = SchemaValueType.Integer;

        // Act
        var result = uncheckedOne.IsCompatible(reference, SchemaCompatibilityMode.Backward);

        // Assert
        await Assert.That(result.IsCompatible).IsFalse();

        var typeChange = result.Errors.FirstOrDefault(error => error.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);

        await Assert.That(typeChange).IsNotNull();
        await Assert.That(typeChange!.OriginalType).IsEqualTo(expectedOriginalType);
        await Assert.That(typeChange.NewType).IsEqualTo(expectedNewType);
    }

    [Test]
    public async ValueTask compatibility_checking_is_skipped_in_none_mode() {
        // Arrange
        var breakingChange = RemovedRequired;

        // Act
        var result = Check(breakingChange, Reference, SchemaCompatibilityMode.None);

        // Assert
        await Assert.That(result.IsCompatible).IsTrue();
    }

    [Test]
    public async ValueTask a_self_referencing_schema_terminates_and_stays_compatible_with_itself() {
        // Arrange — Node.child points back at Node, so the walk has to detect the cycle
        var schema = JsonSchemaNetAgent.Instance.ParseSchema(Schemas.Draft202012);

        // Act
        var result = schema.IsCompatible(schema, SchemaCompatibilityMode.Full);

        // Assert
        await Assert.That(result.IsCompatible).IsTrue();
    }

    [Test]
    public async ValueTask both_agents_agree_that_an_added_required_property_breaks_backward_compatibility() {
        // Arrange
        var nJsonSchemaReference = NJsonSchemaAgent.Instance.ParseSchema(Reference);
        var nJsonSchemaCandidate = NJsonSchemaAgent.Instance.ParseSchema(AddedRequired);

        var jsonSchemaNetReference = JsonSchemaNetAgent.Instance.ParseSchema(Reference);
        var jsonSchemaNetCandidate = JsonSchemaNetAgent.Instance.ParseSchema(AddedRequired);

        // Act
        var nJsonSchema   = nJsonSchemaCandidate.IsCompatible(nJsonSchemaReference, SchemaCompatibilityMode.Backward);
        var jsonSchemaNet = jsonSchemaNetCandidate.IsCompatible(jsonSchemaNetReference, SchemaCompatibilityMode.Backward);

        // Assert
        await Assert.That(nJsonSchema.IsCompatible).IsFalse();
        await Assert.That(jsonSchemaNet.IsCompatible).IsFalse();

        var nJsonSchemaKinds   = nJsonSchema.Errors.Select(error => error.Kind).Distinct().Order().ToArray();
        var jsonSchemaNetKinds = jsonSchemaNet.Errors.Select(error => error.Kind).Distinct().Order().ToArray();

        await Assert.That(jsonSchemaNetKinds).IsEquivalentTo(nJsonSchemaKinds);
    }

    static SchemaCompatibilityResult Check(string uncheckedSchema, string referenceSchema, SchemaCompatibilityMode mode) =>
        JsonSchemaNetAgent.Instance.ParseSchema(uncheckedSchema)
            .IsCompatible(JsonSchemaNetAgent.Instance.ParseSchema(referenceSchema), mode);
}
