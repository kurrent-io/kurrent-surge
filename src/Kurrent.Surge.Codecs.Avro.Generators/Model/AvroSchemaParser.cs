using System.Collections.Generic;
using Kurrent.Surge.Codecs.Avro.Generators.Json;

namespace Kurrent.Surge.Codecs.Avro.Generators.Model;

sealed class AvroSchemaDocument(string source, AvroSchema root, IReadOnlyDictionary<string, AvroSchema> namedTypes) {
    /// <summary>The schema file's text, so a declaration can be re-emitted from its own span.</summary>
    public string                                  Source     { get; } = source;
    public AvroSchema                              Root       { get; } = root;
    public IReadOnlyDictionary<string, AvroSchema> NamedTypes { get; } = namedTypes;

    public AvroSchema Resolve(AvroSchema schema) =>
        schema is AvroNamedReference reference && NamedTypes.TryGetValue(reference.FullName, out var resolved)
            ? resolved
            : schema;
}

static class AvroSchemaParser {
    static readonly Dictionary<string, AvroPrimitiveKind> Primitives = new() {
        ["null"]    = AvroPrimitiveKind.Null,
        ["boolean"] = AvroPrimitiveKind.Boolean,
        ["int"]     = AvroPrimitiveKind.Int,
        ["long"]    = AvroPrimitiveKind.Long,
        ["float"]   = AvroPrimitiveKind.Float,
        ["double"]  = AvroPrimitiveKind.Double,
        ["bytes"]   = AvroPrimitiveKind.Bytes,
        ["string"]  = AvroPrimitiveKind.String
    };

    public static bool TryParse(string json, out AvroSchemaDocument document, out string error, out int offset) {
        document = null!;

        if (!JsonParser.TryParse(json, out var root, out error, out offset)) return false;

        var named = new Dictionary<string, AvroSchema>();

        try {
            var schema = Parse(root, enclosingNamespace: "", named);
            document = new AvroSchemaDocument(json, schema, named);
            return true;
        }
        catch (AvroSchemaException exception) {
            // A schema-shape error has no character offset; the diagnostic points at the file itself.
            error  = exception.Message;
            offset = 0;
            return false;
        }
    }

    static AvroSchema Parse(JsonValue value, string enclosingNamespace, Dictionary<string, AvroSchema> named) {
        switch (value.Kind) {
            // "string"  — a primitive, or a reference to a named type declared earlier.
            case JsonKind.String:
                return Primitives.TryGetValue(value.Text, out var primitive)
                    ? new AvroPrimitive(primitive)
                    : new AvroNamedReference(Qualify(value.Text, enclosingNamespace));

            // ["null", "string"] — a union.
            case JsonKind.Array: {
                var branches = new List<AvroSchema>();

                foreach (var item in value.Items) branches.Add(Parse(item, enclosingNamespace, named));

                if (branches.Count == 0) throw new AvroSchemaException("a union must declare at least one branch");

                return new AvroUnion(branches);
            }

            case JsonKind.Object:
                return ParseObject(value, enclosingNamespace, named);

            default:
                throw new AvroSchemaException($"unexpected schema node of kind {value.Kind}");
        }
    }

    static AvroSchema ParseObject(JsonValue value, string enclosingNamespace, Dictionary<string, AvroSchema> named) {
        var type = value.TextOf("type") ?? throw new AvroSchemaException("schema object is missing 'type'");

        // A logical type is an annotation on a primitive: { "type": "long", "logicalType": "timestamp-millis" }.
        if (Primitives.TryGetValue(type, out var primitive))
            return new AvroPrimitive(primitive, ParseLogical(value.TextOf("logicalType"), primitive), (int)(value["scale"]?.Number ?? 0));

        // A named type's own namespace becomes the enclosing namespace for everything nested inside it.
        // A dotted 'name' carries its own namespace and wins over the 'namespace' attribute, so the split
        // has to happen before the nested walk.
        var declaredNamespace = Split(value.TextOf("name"), value.TextOf("namespace") ?? enclosingNamespace).Namespace;

        switch (type) {
            case "record" or "error": {
                var name   = Split(value.TextOf("name") ?? throw new AvroSchemaException("record is missing 'name'"), declaredNamespace).Name;
                var fields = new List<AvroField>();

                ValidateName(name, "record");
                ValidateNamespace(declaredNamespace);

                var record = new AvroRecord(declaredNamespace, name, fields, value.TextOf("doc")) { Declaration = value };

                // Registered BEFORE the fields are parsed so a record can reference itself.
                Declare(named, record.FullName, "record");
                named[record.FullName] = record;

                foreach (var field in value["fields"]?.Items ?? []) {
                    var fieldName = field.TextOf("name") ?? throw new AvroSchemaException($"a field of '{record.FullName}' is missing 'name'");
                    var fieldType = field["type"]       ?? throw new AvroSchemaException($"field '{fieldName}' of '{record.FullName}' is missing 'type'");

                    ValidateName(fieldName, $"field of '{record.FullName}'");

                    fields.Add(new AvroField(
                        fieldName,
                        Parse(fieldType, declaredNamespace, named),
                        field.TextOf("doc"),
                        field["default"]));
                }

                return record;
            }

            case "enum": {
                var name    = Split(value.TextOf("name") ?? throw new AvroSchemaException("enum is missing 'name'"), declaredNamespace).Name;
                var symbols = new List<string>();

                ValidateName(name, "enum");
                ValidateNamespace(declaredNamespace);

                foreach (var symbol in value["symbols"]?.Items ?? []) {
                    ValidateName(symbol.Text, $"symbol of enum '{name}'");
                    symbols.Add(symbol.Text);
                }

                var schema = new AvroEnum(declaredNamespace, name, symbols, value.TextOf("doc")) { Declaration = value };
                Declare(named, schema.FullName, "enum");
                named[schema.FullName] = schema;
                return schema;
            }

            case "fixed": {
                var name = Split(value.TextOf("name") ?? throw new AvroSchemaException("fixed is missing 'name'"), declaredNamespace).Name;
                var size = (int)(value["size"]?.Number ?? throw new AvroSchemaException($"fixed '{name}' is missing 'size'"));

                ValidateName(name, "fixed");
                ValidateNamespace(declaredNamespace);

                // 'uuid' on a fixed(16) is legal since Avro 1.12 but rejected by Apache's own uuid
                // validation, so it only works alongside the replacement logical type the generator
                // registers. 'duration' is modelled only under the NodaTime time model — the type map
                // erases it back to the fixed when no type can carry months plus days plus milliseconds.
                var logical = value.TextOf("logicalType") switch {
                    "decimal"               => AvroLogicalKind.Decimal,
                    "uuid" when size is 16  => AvroLogicalKind.Uuid,
                    "duration" when size is 12 => AvroLogicalKind.Duration,
                    _                       => AvroLogicalKind.None
                };

                var schema = new AvroFixed(
                    declaredNamespace,
                    name,
                    size,
                    value.TextOf("doc"),
                    logical,
                    (int)(value["scale"]?.Number ?? 0)) { Declaration = value };
                Declare(named, schema.FullName, "fixed");
                named[schema.FullName] = schema;
                return schema;
            }

            case "array": {
                var items = value["items"] ?? throw new AvroSchemaException("array is missing 'items'");
                return new AvroArray(Parse(items, declaredNamespace, named));
            }

            case "map": {
                var values = value["values"] ?? throw new AvroSchemaException("map is missing 'values'");
                return new AvroMap(Parse(values, declaredNamespace, named));
            }

            default:
                throw new AvroSchemaException($"unsupported schema type '{type}'");
        }
    }

    /// <summary>
    /// Reads a <c>logicalType</c> annotation, keeping it only when the primitive underneath it is the one
    /// the logical type is defined for.
    /// </summary>
    /// <remarks>
    /// A mismatch is erased rather than reported, per the Avro specification: "If a logical type is
    /// invalid ... implementations should ignore the logical type and use the underlying Avro type."
    /// Keeping it would emit a <c>DateTime</c> property for an <c>int</c> field.
    /// <para>
    /// The base types mirror each <c>ValidateSchema</c> in <c>Avro.Util</c> at release-1.12.1.
    /// </para>
    /// </remarks>
    static AvroLogicalKind ParseLogical(string? logicalType, AvroPrimitiveKind kind) => logicalType switch {
        "uuid"                   when kind is AvroPrimitiveKind.String => AvroLogicalKind.Uuid,
        "decimal"                when kind is AvroPrimitiveKind.Bytes  => AvroLogicalKind.Decimal,
        "date"                   when kind is AvroPrimitiveKind.Int    => AvroLogicalKind.Date,
        "time-millis"            when kind is AvroPrimitiveKind.Int    => AvroLogicalKind.TimeMillis,
        "time-micros"            when kind is AvroPrimitiveKind.Long   => AvroLogicalKind.TimeMicros,
        "timestamp-millis"       when kind is AvroPrimitiveKind.Long   => AvroLogicalKind.TimestampMillis,
        "timestamp-micros"       when kind is AvroPrimitiveKind.Long   => AvroLogicalKind.TimestampMicros,
        "local-timestamp-millis" when kind is AvroPrimitiveKind.Long   => AvroLogicalKind.LocalTimestampMillis,
        "local-timestamp-micros" when kind is AvroPrimitiveKind.Long   => AvroLogicalKind.LocalTimestampMicros,
        "timestamp-nanos"        when kind is AvroPrimitiveKind.Long   => AvroLogicalKind.TimestampNanos,

        // 'local-timestamp-nanos' is deliberately left erased. Apache implements neither nanos variant,
        // so there is no reference behaviour to match, and defining local semantics here would pre-empt
        // the open question about how local and absolute timestamps should differ.
        _ => AvroLogicalKind.None
    };

    /// <summary>
    /// Rejects a fullname the document already declares.
    /// </summary>
    /// <remarks>
    /// The specification allows one definition per fullname, and Apache enforces it at
    /// <c>Schema.Parse</c>. Left unchecked the two halves of the generator disagree about which
    /// declaration is real — the type emitter walks the named-type table and takes the last, the codec
    /// emitter keeps the first — so the codec reads a member the emitted type does not have. Referring to
    /// a type declared earlier is a name reference, not a redeclaration, and does not reach this.
    /// </remarks>
    static void Declare(Dictionary<string, AvroSchema> named, string fullName, string kind) {
        if (named.ContainsKey(fullName))
            throw new AvroSchemaException($"{kind} '{fullName}' is declared more than once; a name may have only one definition. Declare it once and refer to it by name afterwards");
    }

    /// <summary>
    /// Rejects a declared name the Avro specification does not allow.
    /// </summary>
    /// <remarks>
    /// Without this check an illegal name reaches the compiler as a malformed C# identifier, inside
    /// generated code the consumer cannot edit. Apache enforces the same rule at <c>Schema.Parse</c> from
    /// 1.12.2, so an unchecked name also produces a schema the emitted <c>Schema</c> property cannot parse.
    /// See the Avro specification, "Names".
    /// </remarks>
    static void ValidateName(string name, string role) {
        if (!IsName(name))
            throw new AvroSchemaException($"'{name}' is not a valid Avro name ({role}): a name must match [A-Za-z_][A-Za-z0-9_]*");
    }

    /// <summary>Validates each part of a namespace. An empty namespace is the null namespace, and legal.</summary>
    static void ValidateNamespace(string declaredNamespace) {
        if (declaredNamespace.Length == 0) return;

        foreach (var part in declaredNamespace.Split('.')) ValidateName(part, "namespace part");
    }

    // The rule is ASCII. char.IsLetter and char.IsDigit span the whole Unicode letter and digit
    // categories, which C# accepts in an identifier and Avro does not, so neither can be used here.
    static bool IsName(string value) {
        if (value.Length == 0 || !IsNameStart(value[0])) return false;

        for (var index = 1; index < value.Length; index++)
            if (!IsNameStart(value[index]) && value[index] is not (>= '0' and <= '9'))
                return false;

        return true;
    }

    static bool IsNameStart(char character) => character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_';

    static string Qualify(string name, string enclosingNamespace) =>
        name.Contains(".") || string.IsNullOrEmpty(enclosingNamespace) ? name : $"{enclosingNamespace}.{name}";

    /// <summary>
    /// Splits a declared Avro name into namespace and simple name.
    /// </summary>
    /// <remarks>
    /// A dotted name is a fullname: everything before the last dot is the namespace, and it overrides the
    /// declaration's own <c>namespace</c> attribute. See the Avro specification, "Names".
    /// </remarks>
    static (string Namespace, string Name) Split(string? name, string declaredNamespace) {
        if (name is null) return (declaredNamespace, "");

        var lastDot = name.LastIndexOf('.');

        return lastDot < 0
            ? (declaredNamespace, name)
            : (name.Substring(0, lastDot), name.Substring(lastDot + 1));
    }
}

sealed class AvroSchemaException(string message) : System.Exception(message);
