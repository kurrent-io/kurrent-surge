// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Surge.Schema.Validation.Tests;

static class Schemas {
    // Declares no $schema and uses only keywords that every draft shares, so both agents parse it
    // and a disagreement is a real difference between the validators.
    public const string DialectNeutral = """
    {
      "type": "object",
      "properties": {
        "id":       { "type": "string" },
        "quantity": { "type": "integer" },
        "tags":     { "type": "array", "items": { "type": "string" } }
      },
      "required": [ "id", "quantity" ]
    }
    """;

    // proto3 JSON writes a double as either a number or one of three names, which is why an exported
    // schema reaches for anyOf. One branch always fails, so the evaluation carries errors even when
    // the payload is valid.
    public const string AnyOfBranches = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "properties": {
        "ratio": {
          "anyOf": [
            { "type": "number" },
            { "type": "string", "enum": [ "NaN", "Infinity", "-Infinity" ] }
          ]
        }
      }
    }
    """;

    // Draft-4 keeps subschemas under "definitions" and writes exclusiveMinimum as a boolean
    // modifier on "minimum". Both are gone from draft 6 onwards.
    public const string Draft04 = """
    {
      "$schema": "http://json-schema.org/draft-04/schema#",
      "type": "object",
      "properties": {
        "id":    { "type": "string" },
        "total": { "$ref": "#/definitions/Money" }
      },
      "required": [ "id" ],
      "definitions": {
        "Money": {
          "type": "object",
          "properties": {
            "amount":   { "type": "integer", "minimum": 0, "exclusiveMinimum": true },
            "currency": { "type": "string" }
          },
          "required": [ "amount" ]
        }
      }
    }
    """;

    // Draft 2020-12 keeps subschemas under "$defs". Node references itself, which is the recursive
    // case that has to terminate in both the validator and the compatibility walk.
    public const string Draft202012 = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "$id": "https://kurrent.test/order",
      "type": "object",
      "properties": {
        "id":    { "type": "string" },
        "total": { "$ref": "#/$defs/Money" },
        "root":  { "$ref": "#/$defs/Node" }
      },
      "required": [ "id", "total" ],
      "$defs": {
        "Money": {
          "type": "object",
          "properties": {
            "amount":   { "type": "integer" },
            "currency": { "type": "string" }
          },
          "required": [ "amount" ]
        },
        "Node": {
          "type": "object",
          "properties": {
            "name":  { "type": "string" },
            "child": { "$ref": "#/$defs/Node" }
          },
          "required": [ "name" ]
        }
      }
    }
    """;
}
