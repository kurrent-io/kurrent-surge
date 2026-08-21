using System.Text.Json.Nodes;

namespace Kurrent.Surge.Codecs.Avro;

public static partial class AvroJsonSchema {
    static JsonNode ImportDocument(JsonNode jsonSchema) =>
        jsonSchema is JsonObject root
            ? new Importer(root).Run()
            : throw new AvroJsonSchemaException("#", "The root of a JSON Schema must be an object.");

    sealed class Importer(JsonObject root) {
        static readonly string[] Identity = ["type", "namespace", "name", "doc", "aliases"];
        static readonly string[] Body     = ["fields", "symbols", "size", "logicalType", "precision", "scale", "default"];

        readonly JsonObject?                    _defs    = root["$defs"] as JsonObject;
        readonly HashSet<string>                _emitted = new(StringComparer.Ordinal);
        readonly Dictionary<string, JsonObject> _named   = new(StringComparer.Ordinal);

        public JsonNode Run() => Type(root, "#");

        #region ->> Types <<-

        JsonNode Type(JsonObject schema, string pointer) {
            if (Text(schema, "$ref") is { } reference) {
                var fullName = Dereference(reference, pointer);

                // Avro allows one definition per fullname and forbids a forward reference, so first use is
                // the only legal declaration site. Reversing the hoist is therefore not a choice.
                if (!_emitted.Add(fullName)) return JsonValue.Create(fullName)!;

                var definition = _defs?[fullName] as JsonObject
                              ?? throw new AvroJsonSchemaException(pointer, $"'{reference}' does not resolve to a $defs entry.");

                return Named(definition, fullName, $"#/$defs/{fullName}");
            }

            return schema["oneOf"] is JsonArray branches ? Union(branches, pointer) : Inline(schema, pointer);
        }

        JsonObject Named(JsonObject definition, string fullName, string pointer) {
            var (@namespace, name) = Split(fullName);

            var annotation = definition["x-avro"] as JsonObject;
            var declared   = Text(annotation, "type");
            var node       = new JsonObject();

            if (definition["enum"] is JsonArray symbols)
                EnumBody(definition, symbols, node);
            else if (declared == "record" || definition["properties"] is JsonObject || Text(definition, "type") == "object")
                RecordBody(definition, node, pointer);
            else
                FixedBody(definition, annotation, node, pointer);

            if (@namespace.Length > 0) node["namespace"] = @namespace;

            node["name"] = name;

            Document(definition, node);
            Alias(annotation, node);
            Order(node);
            Custom(annotation, node);

            _named[fullName] = node;

            return node;
        }

        JsonArray Union(JsonArray branches, string pointer) {
            var ordered = new List<(int Branch, JsonObject Schema)>();

            for (var index = 0; index < branches.Count; index++) {
                var branch = branches[index] as JsonObject
                          ?? throw new AvroJsonSchemaException($"{pointer}/oneOf/{index}", "A oneOf branch must be an object.");

                ordered.Add((Int(branch["x-avro"] as JsonObject, "branch") ?? index, branch));
            }

            // The declared index is the wire tag. Reordering oneOf is a no-op for JSON Schema and corrupts
            // every tag here, so indices that are not a permutation are a contradiction rather than
            // something to resolve in favour of one order or the other.
            if (!IsPermutation(ordered.Select(entry => entry.Branch)))
                throw new AvroJsonSchemaException($"{pointer}/oneOf", $"x-avro.branch must number the {ordered.Count} branches 0 to {ordered.Count - 1}.");

            var union = new JsonArray();

            foreach (var (branch, schema) in ordered.OrderBy(entry => entry.Branch))
                union.Add(Type(schema, $"{pointer}/oneOf/{branch}"));

            return union;
        }

        JsonNode Inline(JsonObject schema, string pointer) {
            var annotation = schema["x-avro"] as JsonObject;
            var declared   = Text(annotation, "type");
            var logical    = Text(annotation, "logicalType");
            var format     = Text(schema, "format");
            var type       = Text(schema, "type");

            switch (type) {
                case "array":
                    return new JsonObject {
                        ["type"] = "array",
                        ["items"] = Type(
                            schema["items"] as JsonObject ?? throw new AvroJsonSchemaException(pointer, "An array requires 'items'."),
                            $"{pointer}/items")
                    };

                case "object" when schema["additionalProperties"] is JsonObject values:
                    return new JsonObject {
                        ["type"]   = "map",
                        ["values"] = Type(values, $"{pointer}/additionalProperties")
                    };

                case "object":
                    throw new AvroJsonSchemaException(pointer, "An inline record has no Avro name. Hoist it into $defs.");
            }

            Contradiction(type, declared, logical, pointer);

            var name     = declared ?? Infer(schema, type, format, logical, pointer);
            var resolved = logical ?? Implied(format);

            if (name is "fixed" or "record" or "enum")
                throw new AvroJsonSchemaException(pointer, $"A '{name}' has no Avro name. Hoist it into $defs.");

            if (resolved is null && annotation?["props"] is not JsonObject) return JsonValue.Create(name)!;

            var node = new JsonObject { ["type"] = name };

            if (resolved is not null) {
                node["logicalType"] = resolved;

                if (resolved == "decimal") DecimalFacts(schema, annotation, node);
            }

            Custom(annotation, node);

            return node;
        }

        #endregion // Types

        #region ->> Named bodies <<-

        void RecordBody(JsonObject definition, JsonObject node, string pointer) {
            var properties = definition["properties"] as JsonObject ?? new JsonObject();
            var entries    = new List<(int Ordinal, string Name, JsonObject Schema)>();
            var position   = 0;

            foreach (var (propertyName, value) in properties) {
                var schema = value as JsonObject
                          ?? throw new AvroJsonSchemaException($"{pointer}/properties/{propertyName}", "A property must be an object.");

                entries.Add((Int(schema["x-avro"] as JsonObject, "ordinal") ?? position, propertyName, schema));
                position++;
            }

            // Nothing standard carries field position: `properties` is a JSON object, which JSON Schema
            // treats as unordered, and the Avro wire is positional.
            if (!IsPermutation(entries.Select(entry => entry.Ordinal)))
                throw new AvroJsonSchemaException($"{pointer}/properties", $"x-avro.ordinal must number the {entries.Count} fields 0 to {entries.Count - 1}.");

            var fields = new JsonArray();

            // JsonArray.Add<T> is generic and carries RequiresDynamicCode. The JsonNode overload is the
            // AOT-safe one, and only an explicit type picks it.
            foreach (var (_, fieldName, schema) in entries.OrderBy(entry => entry.Ordinal))
                fields.Add((JsonNode)Field(fieldName, schema, $"{pointer}/properties/{fieldName}"));

            node["type"]   = "record";
            node["fields"] = fields;
        }

        JsonObject Field(string name, JsonObject schema, string pointer) {
            var annotation = schema["x-avro"] as JsonObject;
            var type       = Type(schema, pointer);
            var field      = new JsonObject { ["name"] = name, ["type"] = type };

            Document(schema, field);

            // Presence is tested rather than the value, because a JsonObject indexer answers null for a
            // JSON null. `"default": null` is a declared default on a union whose first branch is null,
            // and it is not the same fact as having no default at all.
            if (schema.ContainsKey("default")) field["default"] = ToAvroDefault(schema["default"], type);

            if (Text(annotation, "order") is { } order) field["order"] = order;

            Alias(annotation, field);
            Custom(annotation, field);

            return field;
        }

        static void EnumBody(JsonObject definition, JsonArray symbols, JsonObject node) {
            node["type"]    = "enum";
            node["symbols"] = symbols.DeepClone();

            if (definition.ContainsKey("default")) node["default"] = definition["default"]?.DeepClone();
        }

        static void FixedBody(JsonObject definition, JsonObject? annotation, JsonObject node, string pointer) {
            // Base64 encodes n bytes to 4*ceil(n/3) characters, which collapses three sizes onto every
            // length, so maxLength cannot give the size back. x-avro.size is the only source.
            var size = Int(annotation, "size")
                    ?? throw new AvroJsonSchemaException(pointer, "A fixed requires x-avro.size; base64 length does not recover it.");

            node["type"] = "fixed";
            node["size"] = size;

            if ((Text(annotation, "logicalType") ?? Implied(Text(definition, "format"))) is not { } logical) return;

            node["logicalType"] = logical;

            if (logical == "decimal") DecimalFacts(definition, annotation, node);
        }

        static void DecimalFacts(JsonObject schema, JsonObject? annotation, JsonObject node) {
            // The emitted pattern pins the scale, so it comes back out of the regex and needs no key.
            // Nothing bounds total digits, which is why precision has one.
            if (Int(annotation, "precision") is { } precision) node["precision"] = precision;

            var scale = ScaleOf(Text(schema, "pattern"));

            if (scale > 0) node["scale"] = scale;
        }

        #endregion // Named bodies

        #region ->> Inference <<-

        /// <remarks>
        /// The documented default for each ambiguity. An inferred import produces a valid Avro schema; only
        /// an annotated one is guaranteed to reproduce the schema it was exported from.
        /// </remarks>
        static string Infer(JsonObject schema, string? type, string? format, string? logical, string pointer) => (type, format) switch {
            ("null", _)    => "null",
            ("boolean", _) => "boolean",

            // The wider type never loses a value, so it is the fallback both times.
            ("number", _)  => "double",
            ("integer", _) => Number(schema, "maximum") == int.MaxValue ? "int" : "long",

            (_, "uuid")      => "string",
            (_, "date")      => "int",
            (_, "time")      => logical == "time-micros" ? "long" : "int",
            (_, "date-time") => "long",
            (_, "duration")  => throw new AvroJsonSchemaException(pointer, "A duration is a fixed(12) and requires x-avro.size."),

            // A fixed needs a size, which only the annotation carries, so base64 alone means bytes.
            ("string", _) when Text(schema, "contentEncoding") == "base64" => "bytes",
            ("string", _) when logical == "decimal"                        => "bytes",
            ("string", _)                                                  => "string",

            _ => throw new AvroJsonSchemaException(pointer, $"No Avro type corresponds to type '{type ?? "(none)"}'.")
        };

        /// <summary>The logical type a format implies on its own, where exactly one does.</summary>
        /// <remarks>
        /// The coarser resolution is the safe assumption where several share a format, so an unannotated
        /// <c>date-time</c> imports as <c>timestamp-millis</c> rather than <c>-micros</c>.
        /// </remarks>
        static string? Implied(string? format) => format switch {
            "uuid"      => "uuid",
            "date"      => "date",
            "time"      => "time-millis",
            "date-time" => "timestamp-millis",
            _           => null
        };

        /// <remarks>
        /// An <c>x-avro.type</c> that disagrees with the standard keyword beside it throws rather than
        /// resolving in favour of either — a silent preference decodes payloads wrongly instead of failing.
        /// A logical type changes the JSON type by construction, so the check applies only without one.
        /// </remarks>
        static void Contradiction(string? type, string? declared, string? logical, string pointer) {
            if (declared is null || logical is not null || type is null) return;

            var expected = declared switch {
                "null"                                   => "null",
                "boolean"                                => "boolean",
                "int" or "long"                          => "integer",
                "float" or "double"                      => "number",
                "bytes" or "string" or "fixed" or "enum" => "string",
                "record" or "map"                        => "object",
                "array"                                  => "array",
                _                                        => null
            };

            if (expected is not null && expected != type)
                throw new AvroJsonSchemaException(pointer, $"x-avro.type '{declared}' requires type '{expected}', but the schema declares '{type}'.");
        }

        static int ScaleOf(string? pattern) {
            const string Marker = @"(\.\d{";

            if (pattern is null) return 0;

            var start = pattern.IndexOf(Marker, StringComparison.Ordinal);

            if (start < 0) return 0;

            start += Marker.Length;

            var end = pattern.IndexOf('}', start);

            return end > start && int.TryParse(pattern.AsSpan(start, end - start), out var scale) ? scale : 0;
        }

        static bool IsPermutation(IEnumerable<int> values) {
            var sorted = values.OrderBy(value => value).ToList();

            return sorted.SequenceEqual(Enumerable.Range(0, sorted.Count));
        }

        #endregion // Inference

        #region ->> Defaults <<-

        /// <summary>Rewrites a JSON Schema default in the Avro representation of the same value.</summary>
        /// <remarks>The two differ wherever a <c>bytes</c> or a <c>fixed</c> is reached.</remarks>
        JsonNode? ToAvroDefault(JsonNode? value, JsonNode avroType) {
            switch (Dereference(avroType)) {
                // An Avro union default is a value of the union's first branch.
                case JsonArray union when union.Count > 0:
                    return ToAvroDefault(value, union[0]!);

                case JsonObject node when Kind(node) is "bytes" or "fixed":
                    return value is JsonValue encoded && encoded.TryGetValue<string>(out var base64)
                        ? JsonValue.Create(ToByteString(base64))
                        : value?.DeepClone();

                case JsonObject node when Kind(node) == "record" && value is JsonObject members: {
                    var converted = new JsonObject();

                    foreach (var field in node["fields"] as JsonArray ?? new JsonArray()) {
                        if (Text(field as JsonObject, "name") is not { } fieldName) continue;

                        if (members[fieldName] is { } member) converted[fieldName] = ToAvroDefault(member, field!["type"]!);
                    }

                    return converted;
                }

                case JsonObject node when Kind(node) == "array" && value is JsonArray items:
                    return new JsonArray(items.Select(item => ToAvroDefault(item, node["items"]!)).ToArray());

                case JsonObject node when Kind(node) == "map" && value is JsonObject entries: {
                    var converted = new JsonObject();

                    foreach (var (key, item) in entries) converted[key] = ToAvroDefault(item, node["values"]!);

                    return converted;
                }

                default:
                    return value?.DeepClone();
            }
        }

        /// <summary>Follows a bare fullname back to the definition it names.</summary>
        JsonNode Dereference(JsonNode avroType) =>
            avroType is JsonValue name && name.TryGetValue<string>(out var fullName) && _named.TryGetValue(fullName, out var resolved)
                ? resolved
                : avroType;

        static string? Kind(JsonObject node) => Text(node, "type");

        #endregion // Defaults

        #region ->> Shared attributes <<-

        static void Document(JsonObject schema, JsonObject node) {
            if (Text(schema, "description") is { } description) node["doc"] = description;
        }

        static void Alias(JsonObject? annotation, JsonObject node) {
            if (annotation?["aliases"] is JsonArray aliases) node["aliases"] = aliases.DeepClone();
        }

        static void Custom(JsonObject? annotation, JsonObject node) {
            if (annotation?["props"] is not JsonObject props) return;

            foreach (var (key, value) in props) node[key] = value?.DeepClone();
        }

        /// <summary>Puts identity first and the type-specific body after it, which is the readable order.</summary>
        static void Order(JsonObject node) {
            foreach (var key in Identity.Concat(Body)) {
                if (!node.ContainsKey(key)) continue;

                var value = node[key];

                // Removing detaches the child, so it can be re-added at the end without a parent conflict.
                node.Remove(key);
                node[key] = value;
            }
        }

        static (string Namespace, string Name) Split(string fullName) {
            // Avro names match [A-Za-z_][A-Za-z0-9_]* and cannot contain a dot, so the last dot splits a
            // fullname unambiguously.
            var dot = fullName.LastIndexOf('.');

            return dot < 0 ? ("", fullName) : (fullName[..dot], fullName[(dot + 1)..]);
        }

        static string Dereference(string reference, string pointer) =>
            reference.StartsWith("#/$defs/", StringComparison.Ordinal)
                ? reference["#/$defs/".Length..].Replace("~1", "/").Replace("~0", "~")
                : throw new AvroJsonSchemaException(pointer, $"'{reference}' is not a reference into $defs.");

        static string? Text(JsonObject? node, string key) =>
            node?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

        static int? Int(JsonObject? node, string key) => (int?)Number(node, key);

        /// <remarks>
        /// A JsonValue built in memory is backed by the CLR type it was created from, so an <c>int</c> node
        /// does not answer <c>TryGetValue&lt;long&gt;</c>. One parsed from text is backed by a JsonElement
        /// and answers all of them. Import accepts both, so every width is tried.
        /// </remarks>
        static long? Number(JsonObject? node, string key) {
            if (node?[key] is not JsonValue value) return null;

            if (value.TryGetValue<long>(out var whole)) return whole;
            if (value.TryGetValue<int>(out var small)) return small;
            if (value.TryGetValue<double>(out var real)) return (long)real;
            if (value.TryGetValue<decimal>(out var exact)) return (long)exact;

            return null;
        }

        #endregion // Shared attributes
    }
}
