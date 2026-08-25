// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;

namespace Kurrent.Surge.Schema.Validation;

/// <summary>
/// Reads the structural keywords of a JSON Schema document straight from the parsed JSON.
/// </summary>
static class JsonSchemaStructure {
    // A $ref may point at another $ref. Cap the chain so a pair of definitions that point at each
    // other cannot spin here; a real schema never nests references anywhere near this deep.
    const int MaxReferenceHops = 64;

    public static bool TryGetKeyword(JsonElement schema, string keyword, out JsonElement value) {
        value = default;

        return schema.ValueKind is JsonValueKind.Object && schema.TryGetProperty(keyword, out value);
    }

    public static bool TryGetSchemaProperty(JsonElement schema, string name, out JsonElement value) {
        value = default;

        return TryGetKeyword(schema, "properties", out var properties)
            && properties.ValueKind is JsonValueKind.Object
            && properties.TryGetProperty(name, out value);
    }

    public static IEnumerable<(string Name, JsonElement Schema)> Properties(JsonElement schema) {
        if (!TryGetKeyword(schema, "properties", out var properties) || properties.ValueKind is not JsonValueKind.Object)
            yield break;

        foreach (var property in properties.EnumerateObject())
            yield return (property.Name, property.Value);
    }

    public static HashSet<string> Required(JsonElement schema) {
        var required = new HashSet<string>(StringComparer.Ordinal);

        if (!TryGetKeyword(schema, "required", out var value) || value.ValueKind is not JsonValueKind.Array)
            return required;

        foreach (var item in value.EnumerateArray())
            if (item.ValueKind is JsonValueKind.String)
                required.Add(item.GetString()!);

        return required;
    }

    public static SchemaValueType TypeOf(JsonElement schema) {
        if (!TryGetKeyword(schema, "type", out var type))
            return SchemaValueType.None;

        // The type keyword is either a name or an array of names: "string" or ["string","null"].
        return type.ValueKind switch {
            JsonValueKind.String => ParseTypeName(type.GetString()),
            JsonValueKind.Array  => type.EnumerateArray()
                .Aggregate(SchemaValueType.None, (combined, name) => combined | ParseTypeName(name.GetString())),
            _ => SchemaValueType.None
        };
    }

    /// <summary>
    /// Follows local <c>$ref</c> pointers until the schema no longer references another, returning
    /// an unresolvable reference as it stands.
    /// </summary>
    public static JsonElement Resolve(JsonElement schema, JsonElement root) {
        for (var hop = 0; hop < MaxReferenceHops; hop++) {
            if (ReferencePointer(schema) is not { } pointer || !TryResolvePointer(root, pointer, out var target))
                return schema;

            schema = target;
        }

        return schema;
    }

    /// <summary>
    /// Resolves both sides of a comparison and reports false once a pair of references has already
    /// been walked.
    /// </summary>
    public static bool Enter(
        ref JsonElement referenceSchema, ref JsonElement uncheckedSchema,
        JsonElement referenceRoot, JsonElement uncheckedRoot, HashSet<(string, string)> visited
    ) {
        var referencePointer = ReferencePointer(referenceSchema);
        var uncheckedPointer = ReferencePointer(uncheckedSchema);

        referenceSchema = Resolve(referenceSchema, referenceRoot);
        uncheckedSchema = Resolve(uncheckedSchema, uncheckedRoot);

        // A recursive type reaches itself only by way of a $ref, so recording the reference pair is
        // enough to stop the walk. Inline subschemas carry no pointer and never need the guard.
        if (referencePointer is null && uncheckedPointer is null)
            return true;

        return visited.Add((referencePointer ?? "", uncheckedPointer ?? ""));
    }

    static string? ReferencePointer(JsonElement schema) =>
        TryGetKeyword(schema, "$ref", out var reference) && reference.ValueKind is JsonValueKind.String
            ? reference.GetString()
            : null;

    // Only same-document pointers resolve here, which is what an exported schema produces:
    //   "#"                -> the document root
    //   "#/$defs/Money"    -> a named definition
    //   "#/properties/id"  -> any node addressed by pointer
    // A segment escapes '/' as "~1" and '~' as "~0", and "~1" must be decoded first so a literal
    // "~01" survives. Plain-name anchors ("#Money") and external references are left unresolved.
    // See https://datatracker.ietf.org/doc/html/rfc6901#section-3
    static bool TryResolvePointer(JsonElement root, string pointer, out JsonElement target) {
        target = root;

        if (pointer.Length == 0 || pointer[0] is not '#')
            return false;

        if (pointer.Length == 1)
            return true;

        if (pointer[1] is not '/')
            return false;

        foreach (var rawSegment in pointer[2..].Split('/')) {
            var segment = rawSegment.Replace("~1", "/").Replace("~0", "~");

            if (target.ValueKind is not JsonValueKind.Object || !target.TryGetProperty(segment, out var child))
                return false;

            target = child;
        }

        return true;
    }

    static SchemaValueType ParseTypeName(string? name) => name switch {
        "object"  => SchemaValueType.Object,
        "array"   => SchemaValueType.Array,
        "string"  => SchemaValueType.String,
        "number"  => SchemaValueType.Number,
        "integer" => SchemaValueType.Integer,
        "boolean" => SchemaValueType.Boolean,
        "null"    => SchemaValueType.Null,
        _         => SchemaValueType.None
    };
}
