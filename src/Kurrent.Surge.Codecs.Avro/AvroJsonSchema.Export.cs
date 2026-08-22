using System.Collections.Frozen;
using System.Text.Json.Nodes;
using Kurrent.Surge.Codecs.Avro.Generators.Model;
using AvroJsonKind = Kurrent.Surge.Codecs.Avro.Generators.Json.JsonKind;
using AvroJsonValue = Kurrent.Surge.Codecs.Avro.Generators.Json.JsonValue;
using JsonParser = Kurrent.Surge.Codecs.Avro.Generators.Json.JsonParser;

namespace Kurrent.Surge.Codecs.Avro;

public static partial class AvroJsonSchema {
    static JsonNode ExportDocument(string avroSchema, AvroJsonSchemaOptions options) {
        var document = Parse(avroSchema, nameof(avroSchema));

        // The model resolves namespaces, logical kinds and named references; the raw tree carries the
        // attributes the model does not keep — precision, aliases, order, custom props — and the logical
        // types it deliberately erases. Export reads both, so the parser's erasure costs no fidelity.
        if (!JsonParser.TryParse(avroSchema, out var raw, out var error, out var offset))
            throw new ArgumentException($"The Avro schema could not be parsed at offset {offset}: {error}", nameof(avroSchema));

        var exported = new Exporter(document, options).Run(raw);

        return options.Canonical ? Canonical(exported)! : exported;
    }

    static JsonNode? Canonical(JsonNode? node) => node switch {
        // Object keys are sorted; array items never are. oneOf order is the union tag and enum order is
        // the symbol ordinal, so reordering either would change what the schema means.
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Canonical(p.Value?.DeepClone())))),

        JsonArray a => new JsonArray(a.Select(i => Canonical(i?.DeepClone())).ToArray()),

        _ => node?.DeepClone()
    };

    sealed class Exporter(AvroSchemaDocument document, AvroJsonSchemaOptions options) {
        readonly Dictionary<string, JsonObject> _defs      = [];
        readonly List<string>                   _order     = [];
        readonly string                         _container = ContainerOf(options.Dialect);

        string? _rootName;

        public JsonObject Run(AvroJsonValue raw) {
            var result = new JsonObject();

            if (options.DeclareDialect) result["$schema"] = DialectOf(options.Dialect);

            // Under draft 4 the root type is written here rather than hoisted, so a reference back to it is
            // '#' rather than a pointer into the container. Both forms are legal in every draft, and this
            // one works around a defect in NJsonSchema, which the schema registry validates with: its
            // resolver cannot follow a root $ref whose target references itself, which every recursive
            // record does. Scoped to draft 4 because that is the dialect that consumer reads; 2020-12 keeps
            // the canonical hoisted shape.
            if (document.Root is IAvroNamed named && options.Dialect is JsonSchemaDialect.Draft04) {
                _rootName       = named.FullName;
                result["title"] = named.FullName;

                Body(named, raw is { Kind: AvroJsonKind.Object } ? raw : named.Declaration, result);
            }
            else {
                Fill(document.Root, raw, result);
            }

            if (_order.Count == 0) return result;

            var defs  = new JsonObject();
            var names = options.Canonical ? _order.OrderBy(name => name, StringComparer.Ordinal) : _order.AsEnumerable();

            foreach (var name in names) defs[name] = _defs[name];

            result[_container] = defs;

            return result;
        }

        #region ->> Nodes <<-

        JsonObject Node(AvroSchema schema, AvroJsonValue? raw) {
            var target = new JsonObject();

            Fill(schema, raw, target);

            return target;
        }

        void Fill(AvroSchema schema, AvroJsonValue? raw, JsonObject target) {
            switch (schema) {
                case AvroNamedReference reference:
                    target["$ref"] = Pointer(reference.FullName);
                    return;

                case IAvroNamed named:
                    Define(named, raw);
                    target["$ref"] = Pointer(named.FullName);
                    return;

                case AvroPrimitive primitive:
                    FillPrimitive(primitive, raw, target);
                    return;

                case AvroArray array:
                    target["type"]  = "array";
                    target["items"] = Node(array.Items, raw?["items"]);
                    Annotate(target, Props(raw, ArrayKeys));
                    return;

                case AvroMap map:
                    // No propertyNames: a JSON object's keys are strings by definition, so asserting it
                    // asserts nothing, and the keyword is not in draft 4 at all.
                    target["type"]                 = "object";
                    target["additionalProperties"] = Node(map.Values, raw?["values"]);
                    Annotate(target, Props(raw, MapKeys));
                    return;

                case AvroUnion union:
                    FillUnion(union, raw, target);
                    return;
            }

            void Define(IAvroNamed named, AvroJsonValue? raw) {
                // Reserved before the body is built: a self-referencing record reaches this name again while
                // its own fields are still being walked, and must find a $ref rather than recurse forever.
                // The root type is never hoisted — it is written at the root and referenced as '#'.
                if (named.FullName == _rootName || _defs.ContainsKey(named.FullName)) return;

                var definition = new JsonObject();

                _defs[named.FullName] = definition;
                _order.Add(named.FullName);

                Body(named, raw is { Kind: AvroJsonKind.Object } ? raw : named.Declaration, definition);
            }

            // A logical type does not change the Avro JSON encoding: the value is written as its base type,
            // so a date is a day count and a timestamp is a long. The base type is what the schema asserts and
            // the annotation is what names the logical type.
            void FillPrimitive(AvroPrimitive primitive, AvroJsonValue? raw, JsonObject target) {
                var annotation = new JsonObject();

                switch (primitive.Kind) {
                    case AvroPrimitiveKind.Null:
                        target["type"] = "null";
                        break;

                    case AvroPrimitiveKind.Boolean:
                        target["type"] = "boolean";
                        break;

                    case AvroPrimitiveKind.Int:
                        target["type"]    = "integer";
                        target["minimum"] = int.MinValue;
                        target["maximum"] = int.MaxValue;
                        break;

                    case AvroPrimitiveKind.Long:
                        target["type"]    = "integer";
                        target["minimum"] = long.MinValue;
                        target["maximum"] = long.MaxValue;
                        break;

                    // JSON Schema has one numeric type, so double is the documented default and only float is
                    // marked. Marking both would state the default twice.
                    case AvroPrimitiveKind.Float:
                        target["type"]     = "number";
                        annotation["type"] = "float";
                        break;

                    case AvroPrimitiveKind.Double:
                        target["type"] = "number";
                        break;

                    case AvroPrimitiveKind.Bytes:
                        target["type"]     = "string";
                        target["pattern"]  = ByteStringPattern;
                        annotation["type"] = "bytes";
                        break;

                    case AvroPrimitiveKind.String:
                        target["type"] = "string";
                        break;
                }

                Logical(raw, primitive, annotation);
                Annotate(target, Merge(annotation, Props(raw, PrimitiveKeys)));
            }

            // The Avro JSON encoding writes the null branch bare and wraps every other branch in an object
            // keyed by the branch's type name, so that is what the schema describes. Describing the branch
            // values directly would produce a schema that rejects the payload the transcoder emits.
            void FillUnion(AvroUnion union, AvroJsonValue? raw, JsonObject target) {
                var branches = new JsonArray();

                for (var index = 0; index < union.Branches.Count; index++) {
                    var branch    = union.Branches[index];
                    var branchRaw = raw is { Kind: AvroJsonKind.Array } ? raw.Items[index] : null;
                    var wrapper   = new JsonObject();

                    if (document.Resolve(branch) is AvroPrimitive { Kind: AvroPrimitiveKind.Null }) {
                        wrapper["type"] = "null";
                    }
                    else {
                        var tag = AvroJsonWriter.BranchName(branch);

                        wrapper["type"]                 = "object";
                        wrapper["properties"]           = new JsonObject { [tag] = Node(branch, branchRaw) };
                        wrapper["required"]             = new JsonArray((JsonNode)tag);
                        wrapper["additionalProperties"] = false;
                    }

                    Annotate(wrapper, new JsonObject { ["branch"] = index });

                    // JsonArray.Add<T> is generic and carries RequiresDynamicCode. The JsonNode overload is
                    // the AOT-safe one, and only an explicit type picks it.
                    branches.Add((JsonNode)wrapper);
                }

                target["oneOf"] = branches;
            }
        }

        void Body(IAvroNamed named, AvroJsonValue? declaration, JsonObject target) {
            switch (named) {
                case AvroRecord record: FillRecord(record, declaration, target); break;
                case AvroEnum @enum:    FillEnum(@enum, declaration, target);    break;
                case AvroFixed @fixed:  FillFixed(@fixed, declaration, target);  break;
            }

            void FillRecord(AvroRecord record, AvroJsonValue? raw, JsonObject target) {
                var properties = new JsonObject();
                var required   = new JsonArray();
                var fields     = raw?["fields"];

                for (var index = 0; index < record.Fields.Count; index++) {
                    var field    = record.Fields[index];
                    var fieldRaw = fields is { Kind: AvroJsonKind.Array } && index < fields.Items.Count ? fields.Items[index] : null;

                    properties[field.Name] = Property(field, fieldRaw, index);

                    // Every field is present on the Avro wire, but a field with a default is legitimately
                    // absent from a JSON document.
                    if (field.Default is null) required.Add((JsonNode)field.Name);
                }

                target["type"]       = "object";
                target["properties"] = properties;

                // Draft 4 requires a non-empty array, so an all-defaulted record omits the keyword entirely.
                if (required.Count > 0) target["required"] = required;

                target["additionalProperties"] = false;

                Describe(target, record.Doc);
                Annotate(target, Merge(Aliases(raw), Props(raw, RecordKeys)));

                JsonObject Property(AvroField field, AvroJsonValue? raw, int ordinal) {
                    var property = Node(field.Type, raw?["type"]);

                    Describe(property, field.Doc);

                    // Carried verbatim. Under the Avro JSON encoding the two representations are the same one,
                    // so a bytes default stays the byte string Avro wrote and needs no conversion either way.
                    if (field.Default is not null) property["default"] = ToJsonNode(field.Default);

                    var annotation = new JsonObject { ["ordinal"] = ordinal };

                    if (raw?.TextOf("order") is { } order && order != "ascending") annotation["order"] = order;

                    Annotate(property, Merge(annotation, Aliases(raw), Props(raw, FieldKeys)));

                    return property;
                }
            }

            void FillEnum(AvroEnum @enum, AvroJsonValue? raw, JsonObject target) {
                var symbols = new JsonArray();

                foreach (var symbol in @enum.Symbols) symbols.Add((JsonNode)symbol);

                target["type"] = "string";
                target["enum"] = symbols;

                Describe(target, @enum.Doc);

                // An enum's own default is a symbol, so the standard keyword carries it whole.
                if (raw?["default"] is { } @default) target["default"] = ToJsonNode(@default);

                Annotate(target, Merge(Aliases(raw), Props(raw, EnumKeys)));
            }

            void FillFixed(AvroFixed @fixed, AvroJsonValue? raw, JsonObject target) {
                // No size key: maxLength recovers the byte count exactly, because the encoding is one character
                // per byte. Base64 would have been three-to-one and unrecoverable; this is not, and the
                // minimality gate deleted the key that used to state it a second time.
                var annotation = new JsonObject { ["type"] = "fixed" };
                target["type"]      = "string";
                target["minLength"] = @fixed.Size;
                target["maxLength"] = @fixed.Size;
                target["pattern"]   = ByteStringPattern;

                Logical(raw, @fixed, annotation);
                Describe(target, @fixed.Doc);
                Annotate(target, Merge(annotation, Aliases(raw), Props(raw, FixedKeys)));
            }
        }

        #endregion // Nodes

        #region ->> Primitives <<-

        /// <remarks>
        /// With no <c>format</c> to infer from, every logical type is named in the annotation, and a
        /// decimal carries both its precision and its scale because no emitted keyword pins either.
        /// </remarks>
        static void Logical(AvroJsonValue? raw, AvroSchema baseSchema, JsonObject annotation) {
            if (LogicalOf(raw, baseSchema) is not { } logical) return;

            annotation["logicalType"] = logical;

            if (logical != "decimal") return;

            if (Integer(raw, "precision") is { } precision) annotation["precision"] = precision;
            if (Integer(raw, "scale") is { } scale) annotation["scale"] = scale;
        }

        #endregion // Primitives

        #region ->> Annotation assembly <<-

        void Annotate(JsonObject target, JsonObject? annotation) {
            if (!options.Annotations || annotation is null || annotation.Count == 0) return;

            if (target["x-avro"] is JsonObject existing) {
                foreach (var (key, value) in annotation.ToList()) {
                    annotation.Remove(key);
                    existing[key] = value;
                }

                return;
            }

            target["x-avro"] = annotation;
        }

        static JsonObject? Merge(params JsonObject?[] parts) {
            var merged = new JsonObject();

            foreach (var part in parts) {
                if (part is null) continue;

                foreach (var (key, value) in part.ToList()) {
                    part.Remove(key);
                    merged[key] = value;
                }
            }

            return merged.Count > 0 ? merged : null;
        }

        static void Describe(JsonObject target, string? doc) {
            if (!string.IsNullOrEmpty(doc)) target["description"] = doc;
        }

        static JsonObject? Aliases(AvroJsonValue? raw) =>
            raw?["aliases"] is { Kind: AvroJsonKind.Array } aliases
                ? new JsonObject { ["aliases"] = ToJsonNode(aliases) }
                : null;

        static JsonObject? Props(AvroJsonValue? raw, FrozenSet<string> recognised) {
            if (raw is not { Kind: AvroJsonKind.Object }) return null;

            var extra = raw.Members
                .Where(member => !recognised.Contains(member.Key))
                .OrderBy(member => member.Key, StringComparer.Ordinal)
                .ToList();

            if (extra.Count == 0) return null;

            var props = new JsonObject();

            foreach (var (key, value) in extra) props[key] = ToJsonNode(value);

            return new JsonObject { ["props"] = props };
        }

        #endregion // Annotation assembly

        // RFC 6901: '~' escapes to '~0' and '/' to '~1', and '~' must be replaced first. An Avro fullname
        // can contain neither, so this only guards a hand-edited container key.
        string Pointer(string fullName) =>
            fullName == _rootName
                ? "#"
                : $"#/{_container}/{fullName.Replace("~", "~0").Replace("/", "~1")}";
    }
}
