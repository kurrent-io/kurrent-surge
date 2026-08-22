using System.Text.Json.Nodes;

namespace Kurrent.Surge.Codecs.Avro;

public static partial class AvroJsonSchema {
    static JsonNode ImportDocument(JsonNode jsonSchema) =>
        jsonSchema is JsonObject root
            ? new Importer(root).Run()
            : throw new AvroJsonSchemaException("#", "The root of a JSON Schema must be an object.");

    sealed class Importer {
        static readonly string[] Identity = ["type", "namespace", "name", "doc", "aliases"];
        static readonly string[] Body     = ["fields", "symbols", "size", "logicalType", "precision", "scale", "default"];

        readonly JsonObject                     _root;
        readonly JsonObject?                    _defs;
        readonly string                         _container;
        readonly HashSet<string>                _emitted = new(StringComparer.Ordinal);
        readonly Dictionary<string, JsonObject> _named   = new(StringComparer.Ordinal);

        string? _rootName;

        /// <remarks>
        /// Either container is accepted on the way in. Which one a document uses follows its declared
        /// draft, and Import has no reason to care which draft produced it.
        /// </remarks>
        public Importer(JsonObject root) {
            _root = root;

            (_container, _defs) = root["$defs"] is JsonObject defs
                ? ("$defs", defs)
                : ("definitions", root["definitions"] as JsonObject);
        }

        /// <remarks>
        /// A named root type is written at the document root and carries its Avro fullname in
        /// <c>title</c>, so there is no container key to read it from and no <c>$ref</c> to follow.
        /// </remarks>
        public JsonNode Run() {
            if (_root["$ref"] is null && Text(_root, "title") is { } rootName) {
                _rootName = rootName;
                _emitted.Add(rootName);

                return Named(_root, rootName, "#");
            }

            return Type(_root, "#");
        }

        #region ->> Types <<-

        JsonNode Type(JsonObject schema, string pointer) {
            if (Text(schema, "$ref") is { } reference) {
                // '#' is the document root, which is where a named root type is written.
                var fullName = reference == "#"
                    ? _rootName ?? throw new AvroJsonSchemaException(pointer, "'#' refers to the root, which declares no named type.")
                    : Dereference(reference, pointer);

                // Avro allows one definition per fullname and forbids a forward reference, so first use is
                // the only legal declaration site. Reversing the hoist is therefore not a choice.
                if (!_emitted.Add(fullName))
                    return JsonValue.Create(fullName)!;

                var definition = _defs?[fullName] as JsonObject
                              ?? throw new AvroJsonSchemaException(pointer, $"'{reference}' does not resolve to a {_container} entry.");

                return Named(definition, fullName, $"#/{_container}/{fullName}");
            }

            return schema["oneOf"] is JsonArray branches ? Union(branches, pointer) : Inline(schema, pointer);

            string Dereference(string reference, string pointer) {
                var prefix = $"#/{_container}/";

                return reference.StartsWith(prefix, StringComparison.Ordinal)
                    ? reference[prefix.Length..].Replace("~1", "/").Replace("~0", "~")
                    : throw new AvroJsonSchemaException(pointer, $"'{reference}' is not a reference into {_container}.");
            }

            // The exported union describes the Avro JSON encoding, so every branch but null is an object with
            // one property named for the branch's Avro type. The branch schema is that property's value.
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

                foreach (var (branch, schema) in ordered.OrderBy(entry => entry.Branch)) {
                    var branchPointer = $"{pointer}/oneOf/{branch}";

                    // JsonArray.Add<T> is generic and carries RequiresDynamicCode, and JsonValue.Create returns
                    // a JsonValue, so only an explicit JsonNode picks the AOT-safe overload.
                    if (Text(schema, "type") == "null") {
                        union.Add((JsonNode)"null");
                        continue;
                    }

                    if (schema["properties"] is not JsonObject wrapper || wrapper.Count != 1)
                        throw new AvroJsonSchemaException(branchPointer, "A non-null union branch must be an object with exactly one property, named for the branch's Avro type.");

                    var (_, value) = wrapper.First();

                    union.Add(
                        Type(
                            value as JsonObject ?? throw new AvroJsonSchemaException(branchPointer, "A union branch schema must be an object."),
                            branchPointer));
                }

                return union;
            }

            JsonNode Inline(JsonObject schema, string pointer) {
                var annotation = schema["x-avro"] as JsonObject;
                var declared   = Text(annotation, "type");
                var logical    = Text(annotation, "logicalType");
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
                        return new JsonObject { ["type"] = "map", ["values"] = Type(values, $"{pointer}/additionalProperties") };

                    case "object": throw new AvroJsonSchemaException(pointer, "An inline record has no Avro name. Hoist it into the definitions container.");
                }

                Contradiction(type, declared, pointer);

                var name = declared ?? Infer(
                    schema, type, logical,
                    pointer);

                if (name is "fixed" or "record" or "enum")
                    throw new AvroJsonSchemaException(pointer, $"A '{name}' has no Avro name. Hoist it into the definitions container.");

                if (logical is null && annotation?["props"] is not JsonObject)
                    return JsonValue.Create(name)!;

                var node = new JsonObject { ["type"] = name };

                if (logical is not null) {
                    node["logicalType"] = logical;

                    DecimalFacts(annotation, node);
                }

                Custom(annotation, node);

                return node;

                // An x-avro.type that disagrees with the standard keyword beside it throws rather than
                // resolving in favour of either — a silent preference decodes payloads wrongly instead of failing.
                static void Contradiction(string? type, string? declared, string pointer) {
                    if (declared is null || type is null)
                        return;

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

                // The documented default for each ambiguity. An inferred import produces a valid Avro schema; only
                // an annotated one is guaranteed to reproduce the schema it was exported from.
                static string Infer(JsonObject schema, string? type, string? logical, string pointer) =>
                    type switch {
                        "null"    => "null",
                        "boolean" => "boolean",

                        // The wider type never loses a value, so it is the fallback both times.
                        "number"  => "double",
                        "integer" => Number(schema, "maximum") == int.MaxValue ? "int" : "long",

                        // A fixed needs a size, so a bare string is bytes when it carries the byte-string pattern.
                        "string" when logical                 == "decimal"         => "bytes",
                        "string" when Text(schema, "pattern") == ByteStringPattern => "bytes",
                        "string"                                                   => "string",

                        _ => throw new AvroJsonSchemaException(pointer, $"No Avro type corresponds to type '{type ?? "(none)"}'.")
                    };
            }
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
                FixedBody(
                    definition, annotation, node,
                    pointer);

            if (@namespace.Length > 0)
                node["namespace"] = @namespace;

            node["name"] = name;

            Document(definition, node);
            Alias(annotation, node);
            Order(node);
            Custom(annotation, node);

            _named[fullName] = node;

            return node;

            static (string Namespace, string Name) Split(string fullName) {
                // Avro names match [A-Za-z_][A-Za-z0-9_]* and cannot contain a dot, so the last dot splits a
                // fullname unambiguously.
                var dot = fullName.LastIndexOf('.');

                return dot < 0 ? ("", fullName) : (fullName[..dot], fullName[(dot + 1)..]);
            }

            static void EnumBody(JsonObject definition, JsonArray symbols, JsonObject node) {
                node["type"]    = "enum";
                node["symbols"] = symbols.DeepClone();

                if (definition.ContainsKey("default"))
                    node["default"] = definition["default"]?.DeepClone();
            }

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

                JsonObject Field(string name, JsonObject schema, string pointer) {
                    var annotation = schema["x-avro"] as JsonObject;
                    var field      = new JsonObject { ["name"] = name, ["type"] = Type(schema, pointer) };

                    Document(schema, field);

                    // Presence is tested rather than the value, because a JsonObject indexer answers null for a
                    // JSON null. `"default": null` is a declared default on a union whose first branch is null,
                    // and it is not the same fact as having no default at all. The value needs no conversion:
                    // under the Avro JSON encoding the two representations are the same one.
                    if (schema.ContainsKey("default"))
                        field["default"] = schema["default"]?.DeepClone();

                    if (Text(annotation, "order") is { } order)
                        field["order"] = order;

                    Alias(annotation, field);
                    Custom(annotation, field);

                    return field;
                }
            }

            static void FixedBody(JsonObject definition, JsonObject? annotation, JsonObject node, string pointer) {
                // One character per byte, so maxLength is the byte count. x-avro.size is still accepted for a
                // hand-written schema that states it, but the exporter no longer emits it.
                var size = Int(definition, "maxLength")
                        ?? Int(annotation, "size")
                        ?? throw new AvroJsonSchemaException(pointer, "A fixed requires maxLength.");

                node["type"] = "fixed";
                node["size"] = size;

                if (Text(annotation, "logicalType") is not { } logical)
                    return;

                node["logicalType"] = logical;

                DecimalFacts(annotation, node);
            }

            // Puts identity first and the type-specific body after it, which is the readable order.
            static void Order(JsonObject node) {
                foreach (var key in Identity.Concat(Body)) {
                    if (!node.ContainsKey(key))
                        continue;

                    var value = node[key];

                    // Removing detaches the child, so it can be re-added at the end without a parent conflict.
                    node.Remove(key);
                    node[key] = value;
                }
            }
        }

        #endregion // Types

        #region ->> Named bodies <<-

        /// <remarks>
        /// A decimal is a byte string on the wire, so no emitted keyword pins either number and both ride
        /// the annotation.
        /// </remarks>
        static void DecimalFacts(JsonObject? annotation, JsonObject node) {
            if (Text(annotation, "logicalType") != "decimal")
                return;

            if (Int(annotation, "precision") is { } precision)
                node["precision"] = precision;

            if (Int(annotation, "scale") is { } scale)
                node["scale"] = scale;
        }

        #endregion // Named bodies

        #region ->> Inference <<-

        static bool IsPermutation(IEnumerable<int> values) {
            var sorted = values.OrderBy(value => value).ToList();

            return sorted.SequenceEqual(Enumerable.Range(0, sorted.Count));
        }

        #endregion // Inference

        #region ->> Shared attributes <<-

        static void Document(JsonObject schema, JsonObject node) {
            if (Text(schema, "description") is { } description)
                node["doc"] = description;
        }

        static void Alias(JsonObject? annotation, JsonObject node) {
            if (annotation?["aliases"] is JsonArray aliases)
                node["aliases"] = aliases.DeepClone();
        }

        static void Custom(JsonObject? annotation, JsonObject node) {
            if (annotation?["props"] is not JsonObject props)
                return;

            foreach (var (key, value) in props)
                node[key] = value?.DeepClone();
        }

        static string? Text(JsonObject? node, string key) =>
            node?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

        static int? Int(JsonObject? node, string key) =>
            (int?)Number(node, key);

        /// <remarks>
        /// A JsonValue built in memory is backed by the CLR type it was created from, so an <c>int</c> node
        /// does not answer <c>TryGetValue&lt;long&gt;</c>. One parsed from text is backed by a JsonElement
        /// and answers all of them. Import accepts both, so every width is tried.
        /// </remarks>
        static long? Number(JsonObject? node, string key) {
            if (node?[key] is not JsonValue value)
                return null;

            if (value.TryGetValue<long>(out var whole))
                return whole;

            if (value.TryGetValue<int>(out var small))
                return small;

            if (value.TryGetValue<double>(out var real))
                return (long)real;

            if (value.TryGetValue<decimal>(out var exact))
                return (long)exact;

            return null;
        }

        #endregion // Shared attributes
    }
}