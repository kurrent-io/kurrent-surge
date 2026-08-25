using System.Collections.Frozen;
using System.Text;
using System.Text.Json.Nodes;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;

public static partial class ProtobufJsonSchema {
    /// <summary>Converts a JSON Schema document back to <c>.proto</c> source text.</summary>
    /// <remarks>
    /// A document with no <c>x-protobuf</c> annotations still converts, by inference from the standard
    /// keywords. Only an annotated document is guaranteed to reproduce the schema it was exported from:
    /// a <c>.proto</c> file cannot be written without field numbers, so an unannotated import assigns
    /// them in document order and the result will not match the original on the wire.
    /// </remarks>
    /// <exception cref="ArgumentException">The document is not valid JSON.</exception>
    /// <exception cref="ProtobufJsonSchemaException">The document cannot be expressed as a <c>.proto</c> schema.</exception>
    public static string Import(string jsonSchema) {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonSchema);

        var node = JsonNode.Parse(jsonSchema)
                ?? throw new ArgumentException("The JSON Schema is null.", nameof(jsonSchema));

        return Import(node);
    }

    /// <inheritdoc cref="Import(string)"/>
    public static string Import(JsonNode jsonSchema) =>
        jsonSchema is JsonObject root
            ? new Importer(root).Run()
            : throw new ProtobufJsonSchemaException("#", "The root of a JSON Schema must be an object.");

    sealed class Importer {
        readonly JsonObject                     _root;
        readonly JsonObject?                    _defs;
        readonly string                         _container;
        readonly Dictionary<string, JsonObject> _types = new(StringComparer.Ordinal);
        readonly List<string>                   _order = [];
        readonly SortedSet<string>              _imports = new(StringComparer.Ordinal);

        string _package = "";
        string _syntax  = "proto3";

        /// <remarks>
        /// Either container is accepted. Which one a document uses follows its declared draft, and
        /// Import has no reason to care which draft produced it.
        /// </remarks>
        public Importer(JsonObject root) {
            _root = root;

            (_container, _defs) = root["$defs"] is JsonObject defs
                ? ("$defs", defs)
                : ("definitions", root["definitions"] as JsonObject);
        }

        public string Run() {
            Collect();

            var text = new StringBuilder();

            text.Append("syntax = \"").Append(_syntax).Append("\";\n");

            if (_package.Length > 0) text.Append("\npackage ").Append(_package).Append(";\n");

            if (_imports.Count > 0) {
                text.Append('\n');

                foreach (var import in _imports) text.Append("import \"").Append(import).Append("\";\n");
            }

            // Only the outermost types are written here; a nested one is written inside its parent.
            foreach (var fullName in _order.Where(IsTopLevel)) {
                text.Append('\n');

                Declaration(fullName, text, depth: 0);
            }

            return text.ToString();
        }

        #region ->> Collecting <<-

        /// <remarks>
        /// A named root type is written at the document root and carries its fullname in <c>title</c>,
        /// so there is no container key to read it from.
        /// </remarks>
        void Collect() {
            if (Text(_root, "title") is { } rootName) Add(rootName, _root);

            if (_defs is not null)
                foreach (var (fullName, definition) in _defs)
                    Add(fullName, definition as JsonObject
                                ?? throw new ProtobufJsonSchemaException($"#/{_container}/{fullName}", "A definition must be an object."));

            if (_order.Count == 0)
                throw new ProtobufJsonSchemaException("#", "The document declares no named type. A .proto file needs at least one message.");

            foreach (var fullName in _order) CollectImports(_types[fullName]);
        }

        void Add(string fullName, JsonObject definition) {
            if (!_types.TryAdd(fullName, definition)) return;

            _order.Add(fullName);

            var annotation = definition["x-protobuf"] as JsonObject;

            if (Text(annotation, "syntax") is { } syntax) _syntax = syntax;

            if (Text(annotation, "package") is not { } package) return;

            // One .proto file declares one package. A closure spanning two is not one file, and
            // silently picking either would move types into a package that never declared them.
            if (_package.Length > 0 && _package != package)
                throw new ProtobufJsonSchemaException(
                    $"#/{_container}/{fullName}",
                    $"The closure spans packages '{_package}' and '{package}', which cannot be one .proto file.");

            _package = package;
        }

        void CollectImports(JsonObject definition) {
            if (definition["properties"] is not JsonObject properties) return;

            foreach (var (_, value) in properties) {
                if (Text((value as JsonObject)?["x-protobuf"] as JsonObject, "wellKnown") is not { } wellKnown) continue;

                if (WellKnownImports.TryGetValue(wellKnown, out var file)) _imports.Add(file);
            }
        }

        bool IsTopLevel(string fullName) => !RelativeOf(fullName).Contains('.', StringComparison.Ordinal);

        string RelativeOf(string fullName) =>
            _package.Length > 0 && fullName.StartsWith(_package + ".", StringComparison.Ordinal)
                ? fullName[(_package.Length + 1)..]
                : fullName;

        #endregion // Collecting

        #region ->> Printing <<-

        void Declaration(string fullName, StringBuilder text, int depth) {
            var definition = _types[fullName];
            var annotation = definition["x-protobuf"] as JsonObject;

            Detached(text, annotation, depth);
            Comment(text, Text(definition, "description"), depth);

            if (definition["enum"] is not null || HasEnumBranch(definition))
                EnumDeclaration(fullName, definition, annotation, text, depth);
            else
                MessageDeclaration(fullName, definition, annotation, text, depth);
        }

        void MessageDeclaration(string fullName, JsonObject definition, JsonObject? annotation, StringBuilder text, int depth) {
            var name = NameOf(fullName);

            Indent(text, depth).Append("message ").Append(name).Append(" {\n");

            Reserved(annotation, text, depth + 1);

            var oneofs = new List<string>();

            foreach (var (property, schema) in Ordered(definition, fullName)) {
                if (Text(schema["x-protobuf"] as JsonObject, "oneof") is { } oneof) {
                    if (!oneofs.Contains(oneof)) oneofs.Add(oneof);

                    continue;
                }

                Field(property, schema, definition, text, depth + 1);
            }

            foreach (var oneof in oneofs) Oneof(oneof, definition, text, depth + 1);

            // A nested type is written inside the parent that its fullname names.
            foreach (var nested in _order.Where(candidate => IsNestedIn(candidate, fullName))) {
                text.Append('\n');

                Declaration(nested, text, depth + 1);
            }

            Indent(text, depth).Append('}');

            TrailingComment(text, annotation);
        }

        void EnumDeclaration(string fullName, JsonObject definition, JsonObject? annotation, StringBuilder text, int depth) {
            Indent(text, depth).Append("enum ").Append(NameOf(fullName)).Append(" {\n");

            if (annotation?["allowAlias"] is not null)
                Indent(text, depth + 1).Append("option allow_alias = true;\n");

            // The values array carries the explicit numbers the 'enum' keyword cannot: a protobuf enum
            // value is a number the author chose, not the position of its name in a list.
            if (annotation?["values"] is JsonArray values) {
                foreach (var entry in values.OfType<JsonObject>())
                    Indent(text, depth + 1)
                        .Append(Text(entry, "name"))
                        .Append(" = ")
                        .Append(Int(entry, "number") ?? 0)
                        .Append(";\n");
            }
            else {
                var ordinal = 0;

                foreach (var name in Names(definition))
                    Indent(text, depth + 1).Append(name).Append(" = ").Append(ordinal++).Append(";\n");
            }

            Indent(text, depth).Append('}');

            TrailingComment(text, annotation);
        }

        void Oneof(string name, JsonObject definition, StringBuilder text, int depth) {
            Indent(text, depth).Append("oneof ").Append(name).Append(" {\n");

            foreach (var (property, schema) in Ordered(definition, name)) {
                if (Text(schema["x-protobuf"] as JsonObject, "oneof") != name) continue;

                Field(property, schema, definition, text, depth + 1, inOneof: true);
            }

            Indent(text, depth).Append("}\n");
        }

        void Field(string property, JsonObject schema, JsonObject message, StringBuilder text, int depth, bool inOneof = false) {
            var annotation = schema["x-protobuf"] as JsonObject;
            var name       = Text(annotation, "name") ?? ToSnakeCase(property);
            var number     = Int(annotation, "field") ?? NextNumber(message);
            var presence   = Text(annotation, "presence");

            Detached(text, annotation, depth);
            Comment(text, Text(schema, "description"), depth);

            Indent(text, depth);

            // A oneof member carries no label at all — the grouping supplies its presence.
            if (!inOneof) {
                if (Text(schema, "type") == "array") text.Append("repeated ");
                else if (presence == "required") text.Append("required ");
                else if (presence == "explicit" && _syntax != "proto2") text.Append("optional ");
                else if (presence == "explicit") text.Append("optional ");
            }

            text.Append(TypeOf(schema, annotation)).Append(' ').Append(name).Append(" = ").Append(number);

            // The property key is the JSON name. When the mapping would not derive it from the declared
            // name, json_name is what put it there and nothing else can carry it back.
            if (property != DeriveJsonName(name)) text.Append(" [json_name = \"").Append(property).Append("\"]");

            text.Append(';');

            TrailingComment(text, annotation);
        }

        /// <remarks>
        /// The element schema decides the type: an array's <c>items</c>, a map's
        /// <c>additionalProperties</c>, otherwise the node itself.
        /// </remarks>
        string TypeOf(JsonObject schema, JsonObject? annotation) {
            if (Text(annotation, "map") is { } keyType) {
                var value = schema["additionalProperties"] as JsonObject
                         ?? throw new ProtobufJsonSchemaException("#", "A map requires additionalProperties.");

                return $"map<{keyType}, {Scalar(value, value["x-protobuf"] as JsonObject)}>";
            }

            if (Text(schema, "type") == "array")
                return Scalar(
                    schema["items"] as JsonObject ?? throw new ProtobufJsonSchemaException("#", "An array requires items."),
                    annotation);

            return Scalar(schema, annotation);
        }

        string Scalar(JsonObject schema, JsonObject? annotation) {
            if (Text(annotation, "wellKnown") is { } wellKnown) return wellKnown;

            // A declared type outranks inference: several proto types share one JSON form, and only
            // one of each group is the default the keywords can be read back as.
            if (Text(annotation, "type") is { } declared && declared != "group") return declared;

            if (Text(schema, "$ref") is { } reference) return RelativeOf(Dereference(reference));

            var type = Text(schema, "type");

            return type switch {
                "boolean" => "bool",
                "number"  => "double",
                "integer" => Number(schema, "minimum") == 0 ? "uint32" : "int32",

                // A 64-bit integer is a decimal string, and bytes is base64. Both are strings, and the
                // pattern is what tells them apart.
                "string" when Text(schema, "pattern") == Base64Pattern         => "bytes",
                "string" when Text(schema, "pattern") == SignedDigitsPattern   => "int64",
                "string" when Text(schema, "pattern") == UnsignedDigitsPattern => "uint64",
                "string"                                                       => "string",

                "object" => "google.protobuf.Struct",
                "array"  => "google.protobuf.ListValue",

                // A float or an open enum is a oneOf, so the branches decide.
                null when schema["oneOf"] is JsonArray branches => FromBranches(branches),

                _ => throw new ProtobufJsonSchemaException("#", $"No protobuf type corresponds to type '{type ?? "(none)"}'.")
            };

            string FromBranches(JsonArray branches) {
                foreach (var branch in branches) {
                    if (branch is not JsonObject option) continue;

                    if (option["enum"] is JsonArray) return "int32";
                    if (Text(option, "type") == "number") return "double";
                }

                return "double";
            }
        }

        string Dereference(string reference) {
            var prefix = $"#/{_container}/";

            return reference.StartsWith(prefix, StringComparison.Ordinal)
                ? reference[prefix.Length..].Replace("~1", "/").Replace("~0", "~")
                : Text(_root, "title")
               ?? throw new ProtobufJsonSchemaException("#", $"'{reference}' is not a reference into {_container}.");
        }

        void Reserved(JsonObject? annotation, StringBuilder text, int depth) {
            if (annotation?["reservedRanges"] is JsonArray ranges && ranges.Count > 0) {
                Indent(text, depth).Append("reserved ");

                for (var index = 0; index < ranges.Count; index++) {
                    var range = (JsonObject)ranges[index]!;
                    var from  = range["from"]!.GetValue<int>();
                    var to    = range["to"]!.GetValue<int>();

                    if (index > 0) text.Append(", ");

                    text.Append(from);

                    if (to != from) text.Append(" to ").Append(to);
                }

                text.Append(";\n");
            }

            if (annotation?["reservedNames"] is not JsonArray names || names.Count == 0) return;

            Indent(text, depth).Append("reserved ");

            for (var index = 0; index < names.Count; index++) {
                if (index > 0) text.Append(", ");

                text.Append('"').Append(names[index]!.GetValue<string>()).Append('"');
            }

            text.Append(";\n");
        }

        /// <remarks>
        /// Written on the declaration's own line, because that is the only thing that tells the parser
        /// it trails this declaration rather than leading the next one. A block spanning several lines
        /// cannot be written that way and is not carried.
        /// </remarks>
        static void TrailingComment(StringBuilder text, JsonObject? annotation) {
            if (Text(annotation, "comments") is { } trailing)
                text.Append(" //").Append(trailing.TrimEnd('\n'));

            text.Append('\n');
        }

        /// <remarks>
        /// Each block is followed by the blank line that detaches it. Without it the parser reads the
        /// block as the declaration's leading comment, which is the other thing it could have been.
        /// </remarks>
        static void Detached(StringBuilder text, JsonObject? annotation, int depth) {
            if (annotation?["detached"] is not JsonArray blocks) return;

            foreach (var block in blocks) {
                Comment(text, block!.GetValue<string>(), depth);

                text.Append('\n');
            }
        }

        static void Comment(StringBuilder text, string? description, int depth) {
            if (description is not { Length: > 0 }) return;

            foreach (var line in description.TrimEnd('\n').Split('\n'))
                Indent(text, depth).Append("//").Append(line).Append('\n');
        }

        static StringBuilder Indent(StringBuilder text, int depth) => text.Append(' ', depth * 2);

        #endregion // Printing

        #region ->> Reading <<-

        /// <remarks>
        /// Ordered by field number rather than by the order <c>properties</c> happens to be in. A JSON
        /// object is unordered by definition, so sorting its keys is a legal edit — and one that would
        /// otherwise reshuffle every field of every message it touched.
        /// </remarks>
        List<(string Name, JsonObject Schema)> Ordered(JsonObject definition, string owner) {
            var fields = new List<(string Name, JsonObject Schema)>();

            foreach (var (property, value) in definition["properties"] as JsonObject ?? [])
                fields.Add((property, value as JsonObject
                                   ?? throw new ProtobufJsonSchemaException($"{owner}/{property}", "A property must be an object.")));

            return fields
                .OrderBy(field => Int(field.Schema["x-protobuf"] as JsonObject, "field") ?? int.MaxValue)
                .ToList();
        }

        bool IsNestedIn(string candidate, string parent) {
            if (!candidate.StartsWith(parent + ".", StringComparison.Ordinal)) return false;

            return !candidate[(parent.Length + 1)..].Contains('.', StringComparison.Ordinal);
        }

        static string NameOf(string fullName) {
            var dot = fullName.LastIndexOf('.');

            return dot < 0 ? fullName : fullName[(dot + 1)..];
        }

        static bool HasEnumBranch(JsonObject definition) =>
            definition["oneOf"] is JsonArray branches
         && branches.Any(branch => branch is JsonObject option && option["enum"] is JsonArray);

        static IEnumerable<string> Names(JsonObject definition) {
            var names = definition["enum"] as JsonArray;

            if (names is null && definition["oneOf"] is JsonArray branches)
                names = branches.OfType<JsonObject>().Select(branch => branch["enum"]).OfType<JsonArray>().FirstOrDefault();

            return names?.Select(name => name!.GetValue<string>()) ?? [];
        }

        /// <remarks>
        /// A .proto file cannot be written without field numbers, so an unannotated document gets them
        /// assigned in document order. The result parses and will not match the original on the wire.
        /// </remarks>
        static int NextNumber(JsonObject message) => (message["properties"] as JsonObject)?.Count is { } count ? count + 1 : 1;

        /// <remarks>The inverse of the JSON name transform, which lower-cases and drops the underscores.</remarks>
        static string ToSnakeCase(string jsonName) {
            var name = new StringBuilder(jsonName.Length + 4);

            foreach (var character in jsonName) {
                if (char.IsUpper(character)) {
                    name.Append('_').Append(char.ToLowerInvariant(character));
                    continue;
                }

                name.Append(character);
            }

            return name.ToString();
        }

        static string? Text(JsonObject? node, string key) =>
            node?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

        static int? Int(JsonObject? node, string key) => (int?)Number(node, key);

        /// <remarks>
        /// A JsonValue built in memory is backed by the CLR type it was created from, so an <c>int</c>
        /// node does not answer <c>TryGetValue&lt;long&gt;</c>. One parsed from text is backed by a
        /// JsonElement and answers all of them. Import accepts both, so every width is tried.
        /// </remarks>
        static long? Number(JsonObject? node, string key) {
            if (node?[key] is not JsonValue value) return null;

            if (value.TryGetValue<long>(out var whole)) return whole;
            if (value.TryGetValue<int>(out var small)) return small;
            if (value.TryGetValue<double>(out var real)) return (long)real;
            if (value.TryGetValue<decimal>(out var exact)) return (long)exact;

            return null;
        }

        #endregion // Reading
    }

    /// <remarks>
    /// A well-known type is declared in a file the importing schema has to name, or the descriptor
    /// builder cannot resolve it.
    /// </remarks>
    static readonly FrozenDictionary<string, string> WellKnownImports = new Dictionary<string, string>(StringComparer.Ordinal) {
        ["google.protobuf.Timestamp"] = "google/protobuf/timestamp.proto",
        ["google.protobuf.Duration"]  = "google/protobuf/duration.proto",
        ["google.protobuf.Struct"]    = "google/protobuf/struct.proto",
        ["google.protobuf.Value"]     = "google/protobuf/struct.proto",
        ["google.protobuf.ListValue"] = "google/protobuf/struct.proto",
        ["google.protobuf.Any"]       = "google/protobuf/any.proto",
        ["google.protobuf.Empty"]     = "google/protobuf/empty.proto",
        ["google.protobuf.FieldMask"] = "google/protobuf/field_mask.proto",
        ["google.protobuf.DoubleValue"] = "google/protobuf/wrappers.proto",
        ["google.protobuf.FloatValue"]  = "google/protobuf/wrappers.proto",
        ["google.protobuf.Int64Value"]  = "google/protobuf/wrappers.proto",
        ["google.protobuf.UInt64Value"] = "google/protobuf/wrappers.proto",
        ["google.protobuf.Int32Value"]  = "google/protobuf/wrappers.proto",
        ["google.protobuf.UInt32Value"] = "google/protobuf/wrappers.proto",
        ["google.protobuf.BoolValue"]   = "google/protobuf/wrappers.proto",
        ["google.protobuf.StringValue"] = "google/protobuf/wrappers.proto",
        ["google.protobuf.BytesValue"]  = "google/protobuf/wrappers.proto"
    }.ToFrozenDictionary(StringComparer.Ordinal);
}
