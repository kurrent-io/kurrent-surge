// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Json.Schema;

namespace Kurrent.Surge.Schema.Validation.Tests;

/// Each agent covers a range of drafts the other does not. These tests pin both halves: the draft
/// each agent handles, and the draft it refuses.
public class SchemaDialectTests {
    // Held apart from the interpolated payloads below. A raw string closes an interpolation hole
    // with as many braces as it opens it, so a deeply nested tail cannot sit inside one.
    const string NestedNodes = """{"name":"a","child":{"name":"b","child":{"child":{}}}}""";

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    #region ->> Draft-4 <<-

    [Test]
    public async ValueTask njsonschema_validates_a_draft04_schema() {
        // Arrange
        var payload = ValidOrder(Faker.Random.Guid().ToString(), Faker.Random.Int(1, 100));

        // Act
        var result = NJsonSchemaAgent.Instance.ParseSchema(Schemas.Draft04).Validate(payload);

        // Assert
        await Assert.That(result.IsValid).IsTrue().Because(Errors(result));
    }

    [Test]
    public async ValueTask njsonschema_rejects_a_draft04_payload_that_breaks_the_schema() {
        // Arrange — "amount" is declared integer and required inside Money
        var payload = OrderWithoutAmount(Faker.Random.Guid().ToString());

        // Act
        var result = NJsonSchemaAgent.Instance.ParseSchema(Schemas.Draft04).Validate(payload);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask jsonschemanet_refuses_a_draft04_schema() {
        // Arrange
        var parse = () => JsonSchemaNetAgent.Instance.ParseSchema(Schemas.Draft04);

        // Act & Assert — the draft-4 dialect is not registered, so parsing never reaches a payload
        await Assert.That(parse).Throws<JsonSchemaException>();
    }

    #endregion // Draft-4

    #region ->> Draft 2020-12 <<-

    [Test]
    public async ValueTask jsonschemanet_resolves_defs_references() {
        // Arrange
        var payload = ValidOrder(Faker.Random.Guid().ToString(), Faker.Random.Int(1, 100));

        // Act
        var result = JsonSchemaNetAgent.Instance.ParseSchema(Schemas.Draft202012).Validate(payload);

        // Assert
        await Assert.That(result.IsValid).IsTrue().Because(Errors(result));
    }

    [Test]
    public async ValueTask jsonschemanet_reports_a_failure_inside_a_defs_reference() {
        // Arrange — Money requires "amount", and the reference has to resolve for that to be caught
        var payload = OrderWithoutAmount(Faker.Random.Guid().ToString());

        // Act
        var result = JsonSchemaNetAgent.Instance.ParseSchema(Schemas.Draft202012).Validate(payload);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error => error.Path == "/total")).IsTrue();
    }

    [Test]
    public async ValueTask jsonschemanet_validates_a_self_referencing_type_to_full_depth() {
        // Arrange — Node.child points back at Node, and the innermost node omits the required "name"
        var payload = $$"""{"id":"{{Faker.Random.Guid()}}","total":{"amount":1},"root":{{NestedNodes}}}""";

        // Act
        var result = JsonSchemaNetAgent.Instance.ParseSchema(Schemas.Draft202012).Validate(payload);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error => error.Path == "/root/child/child")).IsTrue();
    }

    [Test]
    public async ValueTask njsonschema_reads_a_2020_12_schema_the_same_way() {
        // Arrange — both agents see the same document, so a disagreement here is a validator
        // difference rather than a payload difference
        var payload = ValidOrder(Faker.Random.Guid().ToString(), Faker.Random.Int(1, 100));

        // Act
        var nJsonSchema   = NJsonSchemaAgent.Instance.ParseSchema(Schemas.Draft202012).Validate(payload);
        var jsonSchemaNet = JsonSchemaNetAgent.Instance.ParseSchema(Schemas.Draft202012).Validate(payload);

        // Assert
        await Assert.That(nJsonSchema.IsValid).IsEqualTo(jsonSchemaNet.IsValid).Because(Errors(nJsonSchema));
    }

    #endregion // Draft 2020-12

    static string ValidOrder(string id, int amount) =>
        $$$"""{"id":"{{{id}}}","total":{"amount":{{{amount}}},"currency":"EUR"},"root":{"name":"n"}}""";

    static string OrderWithoutAmount(string id) =>
        $$$"""{"id":"{{{id}}}","total":{"currency":"EUR"}}""";

    static string Errors(SchemaValidationResult result) =>
        string.Join("; ", result.Errors.Select(error => error.ToString()));
}
