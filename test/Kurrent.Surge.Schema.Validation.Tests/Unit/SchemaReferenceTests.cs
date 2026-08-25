// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Surge.Schema.Validation.Tests;

/// <summary>
/// Reference resolution shapes that the pinned version of JsonSchema.Net was reported to mishandle
/// and later fixed. Neither reproduces here, and these tests hold that line across version changes.
/// </summary>
public class SchemaReferenceTests {
    // Alphabetical key order puts $defs ahead of $id. Both documents name a definition "Item" and
    // constrain it differently, so a registration collision makes one borrow the other's rules.
    const string StringItem = """
    {
      "$defs": { "Item": { "type": "object", "properties": { "v": { "type": "string" } }, "required": [ "v" ] } },
      "$id": "https://kurrent.test/item-string",
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "properties": { "item": { "$ref": "#/$defs/Item" } },
      "type": "object"
    }
    """;

    const string IntegerItem = """
    {
      "$defs": { "Item": { "type": "object", "properties": { "v": { "type": "integer" } }, "required": [ "v" ] } },
      "$id": "https://kurrent.test/item-integer",
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "properties": { "item": { "$ref": "#/$defs/Item" } },
      "type": "object"
    }
    """;

    // A self-referential definition reached through "not". Reported upstream as a cycle-detection
    // gap that overflowed the stack during evaluation.
    const string RecursiveUnderNot = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "$defs": {
        "Node": {
          "type": "object",
          "properties": { "name": { "type": "string" }, "child": { "$ref": "#/$defs/Node" } }
        }
      },
      "type": "object",
      "properties": { "root": { "not": { "$ref": "#/$defs/Node" } } }
    }
    """;

    [Test]
    public async ValueTask two_schemas_naming_the_same_definition_keep_their_own_rules() {
        // Arrange
        var stringItem  = JsonSchemaNetAgent.Instance.ParseSchema(StringItem);
        var integerItem = JsonSchemaNetAgent.Instance.ParseSchema(IntegerItem);

        var text   = """{"item":{"v":"text"}}""";
        var number = """{"item":{"v":42}}""";

        // Act
        var stringAcceptsText    = stringItem.Validate(text).IsValid;
        var stringRejectsNumber  = stringItem.Validate(number).IsValid;
        var integerAcceptsNumber = integerItem.Validate(number).IsValid;
        var integerRejectsText   = integerItem.Validate(text).IsValid;

        // Assert — a collision shows up as one document honouring the other's definition
        await Assert.That(stringAcceptsText).IsTrue();
        await Assert.That(stringRejectsNumber).IsFalse();
        await Assert.That(integerAcceptsNumber).IsTrue();
        await Assert.That(integerRejectsText).IsFalse();
    }

    [Test]
    public async ValueTask a_self_referencing_definition_under_not_reaches_a_verdict() {
        // Arrange — three levels deep, so the recursive reference is followed rather than declared
        var schema  = JsonSchemaNetAgent.Instance.ParseSchema(RecursiveUnderNot);
        var payload = """{"root":{"name":"a","child":{"name":"b","child":{"name":"c"}}}}""";

        // Act
        var validate = () => schema.Validate(payload);

        // Assert — the failure mode is an unbounded walk, so returning at all is the assertion
        await Assert.That(validate).ThrowsNothing();
    }
}
