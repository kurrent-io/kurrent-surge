// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Humanizer;

namespace Kurrent.Surge.Schema.Validation;

static class NJsonSchemaMappings {
    public static SchemaValidationError Map(this NJsonSchema.Validation.ValidationError value) =>
        new() {
            ErrorMessage = value.Kind.Humanize(),
            Property     = value.Property,
            Path         = value.Path,
            LineInfo     = value.HasLineInfo
                ? new SchemaValidationErrorLineInfo {
                    LineNumber   = (uint)value.LineNumber,
                    LinePosition = (uint)value.LinePosition
                }
                : null
        };

    public static List<SchemaValidationError> Map(this IEnumerable<NJsonSchema.Validation.ValidationError> values) =>
        values.Select(Map).ToList();

    // Both enums are bit flags, but they assign different bits to the same type:
    // Object is 32 in NJsonSchema.JsonObjectType and 1 in SchemaValueType, so a
    // numeric cast between them silently reports the wrong type. Map bit by bit.
    // JsonObjectType.File has no JSON Schema counterpart (it is a Swagger construct)
    // and stays unmapped.
    public static SchemaValueType MapValueType(NJsonSchema.JsonObjectType value) {
        var mapped = SchemaValueType.None;

        if (value.HasFlag(NJsonSchema.JsonObjectType.Object)) mapped  |= SchemaValueType.Object;
        if (value.HasFlag(NJsonSchema.JsonObjectType.Array)) mapped   |= SchemaValueType.Array;
        if (value.HasFlag(NJsonSchema.JsonObjectType.String)) mapped  |= SchemaValueType.String;
        if (value.HasFlag(NJsonSchema.JsonObjectType.Number)) mapped  |= SchemaValueType.Number;
        if (value.HasFlag(NJsonSchema.JsonObjectType.Integer)) mapped |= SchemaValueType.Integer;
        if (value.HasFlag(NJsonSchema.JsonObjectType.Boolean)) mapped |= SchemaValueType.Boolean;
        if (value.HasFlag(NJsonSchema.JsonObjectType.Null)) mapped    |= SchemaValueType.Null;

        return mapped;
    }
}