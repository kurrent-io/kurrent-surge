// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Surge.Schema.Validation.Tests;

/// A branching keyword lets one branch fail while the payload is still valid, so the verdict cannot
/// be read off the error list. Both agents have to agree on that.
public class BranchingSchemaTests {
    [Test]
    public async ValueTask a_payload_matching_the_first_branch_is_valid() {
        // Arrange — matches { "type": "number" }, so the string branch fails and reports errors
        var payload = """{"ratio":0.125}""";

        // Act
        var result = JsonSchemaNetAgent.Instance.ParseSchema(Schemas.AnyOfBranches).Validate(payload);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Errors).IsEmpty();
    }

    [Test]
    public async ValueTask a_payload_matching_the_second_branch_is_valid() {
        // Arrange — matches the string branch, so the number branch fails and reports errors
        var payload = """{"ratio":"NaN"}""";

        // Act
        var result = JsonSchemaNetAgent.Instance.ParseSchema(Schemas.AnyOfBranches).Validate(payload);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Errors).IsEmpty();
    }

    [Test]
    public async ValueTask a_payload_matching_no_branch_is_rejected() {
        // Arrange — a boolean satisfies neither branch
        var payload = """{"ratio":true}""";

        // Act
        var result = JsonSchemaNetAgent.Instance.ParseSchema(Schemas.AnyOfBranches).Validate(payload);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors).IsNotEmpty();
    }

    [Test]
    public async ValueTask both_agents_agree_on_a_branching_schema() {
        // Arrange
        var payload = """{"ratio":0.125}""";

        // Act
        var nJsonSchema   = NJsonSchemaAgent.Instance.ParseSchema(Schemas.AnyOfBranches).Validate(payload);
        var jsonSchemaNet = JsonSchemaNetAgent.Instance.ParseSchema(Schemas.AnyOfBranches).Validate(payload);

        // Assert
        await Assert.That(jsonSchemaNet.IsValid).IsEqualTo(nJsonSchema.IsValid);
    }
}
