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
        // types it deliberately erases. Export reads both, so the parser's erasure policy costs no fidelity.
        if (!JsonParser.TryParse(avroSchema, out var raw, out var error, out var offset))
            throw new ArgumentException($"The Avro schema could not be parsed at offset {offset}: {error}", nameof(avroSchema));

        var exporter = new Exporter(document, options);
        var exported = exporter.Run(raw);

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
        readonly Dictionary<string, JsonObject> _defs  = [];
        readonly List<string>                   _order = [];

        public JsonObject Run(AvroJsonValue raw) {
            var result = new JsonObject();

            if (options.DeclareDialect) result["$schema"] = Dialect;

            Fill(document.Root, raw, result);

            if (_order.Count == 0) return result;

            var defs  = new JsonObject();
            var names = options.Canonical ? _order.OrderBy(name => name, StringComparer.Ordinal) : _order.AsEnumerable();

            foreach (var name in names)
                defs[name] = _defs[name];

            result["$defs"] = defs;

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
                    target["type"]                 = "object";
                    target["additionalProperties"] = Node(map.Values, raw?["values"]);
                    target["propertyNames"]        = new JsonObject { ["type"] = "string" };
                    Annotate(target, Props(raw, MapKeys));
                    return;

                case AvroUnion union:
                    var branches = new JsonArray();

                    for (var index = 0; index < union.Branches.Count; index++) {
                        var branch = Node(union.Branches[index], raw is { Kind: AvroJsonKind.Array } ? raw.Items[index] : null);

                        Annotate(branch, new JsonObject { ["branch"] = index });

                        // JsonArray.Add<T> is generic and carries RequiresDynamicCode. The JsonNode
                        // overload is the AOT-safe one, and only an explicit type picks it.
                        branches.Add((JsonNode)branch);
                    }

                    target["oneOf"] = branches;
                    return;
            }
        }

        void Define(IAvroNamed named, AvroJsonValue? raw) {
            // Reserved before the body is built: a self-referencing record reaches this name again while
            // its own fields are still being walked, and must find a $ref rather than recurse forever.
            if (_defs.ContainsKey(named.FullName)) return;

            var definition = new JsonObject();

            _defs[named.FullName] = definition;
            _order.Add(named.FullName);

            var declaration = raw is { Kind: AvroJsonKind.Object } ? raw : named.Declaration;

            switch (named) {
                case AvroRecord record: FillRecord(record, declaration, definition); break;
                case AvroEnum @enum:    FillEnum(@enum, declaration, definition);    break;
                case AvroFixed @fixed:  FillFixed(@fixed, declaration, definition);  break;
            }
        }

        #endregion // Nodes

        #region ->> Named types <<-

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

            if (required.Count > 0) target["required"] = required;

            target["additionalProperties"] = false;

            Describe(target, record.Doc);
            Annotate(target, Merge(Aliases(raw), Props(raw, RecordKeys)));
        }

        JsonObject Property(AvroField field, AvroJsonValue? raw, int ordinal) {
            var property = Node(field.Type, raw?["type"]);

            Describe(property, field.Doc);

            // The standard keyword carries the default on its own: bytes and fixed are the only types whose
            // two representations differ, and base64 converts back to the byte string mechanically. The
            // minimality gate deleted the annotation key that used to state it a second time.
            if (field.Default is not null)
                property["default"] = ToJsonSchemaDefault(field.Default, document.Resolve(field.Type));

            var annotation = new JsonObject { ["ordinal"] = ordinal };

            if (raw?.TextOf("order") is { } order && order != "ascending") annotation["order"] = order;

            Annotate(property, Merge(annotation, Aliases(raw), Props(raw, FieldKeys)));

            return property;
        }

        void FillEnum(AvroEnum @enum, AvroJsonValue? raw, JsonObject target) {
            var symbols = new JsonArray();

            foreach (var symbol in @enum.Symbols) symbols.Add((JsonNode)symbol);

            target["type"] = "string";
            target["enum"] = symbols;

            Describe(target, @enum.Doc);

            var annotation = new JsonObject();

            // An enum's own default is a symbol, so the standard keyword carries it whole.
            if (raw?["default"] is { } @default) target["default"] = ToJsonNode(@default);

            Annotate(target, Merge(annotation, Aliases(raw), Props(raw, EnumKeys)));
        }

        void FillFixed(AvroFixed @fixed, AvroJsonValue? raw, JsonObject target) {
            var annotation = new JsonObject { ["type"] = "fixed", ["size"] = @fixed.Size };
            var logical    = LogicalOf(raw, @fixed);

            switch (logical) {
                case "decimal":
                    FillDecimal(raw, target, annotation);
                    break;

                case "uuid":
                    target["type"]    = "string";
                    target["format"]  = "uuid";
                    target["pattern"] = UuidPattern;
                    break;

                case "duration":
                    target["type"]            = "string";
                    target["format"]          = "duration";
                    target["pattern"]         = DurationPattern;
                    annotation["logicalType"] = logical;
                    break;

                default:
                    // The encoded length is exact, and it is all that is exact: base64 maps three byte
                    // sizes onto every length, so 16, 17 and 18 all pin to 24 and the size cannot come
                    // back out of it. That is what x-avro.size is for.
                    target["type"]            = "string";
                    target["contentEncoding"] = "base64";
                    target["minLength"]       = Base64Length(@fixed.Size);
                    target["maxLength"]       = Base64Length(@fixed.Size);
                    target["pattern"]         = Base64Pattern;

                    if (logical is not null) annotation["logicalType"] = logical;

                    break;
            }

            Describe(target, @fixed.Doc);
            Annotate(target, Merge(annotation, Aliases(raw), Props(raw, FixedKeys)));
        }

        #endregion // Named types

        #region ->> Primitives and logical types <<-

        void FillPrimitive(AvroPrimitive primitive, AvroJsonValue? raw, JsonObject target) {
            var annotation = new JsonObject();
            var logical    = LogicalOf(raw, primitive);

            switch (logical) {
                case "decimal":
                    annotation["type"] = "bytes";
                    FillDecimal(raw, target, annotation);
                    break;

                case "uuid":
                    target["type"]    = "string";
                    target["format"]  = "uuid";
                    target["pattern"] = UuidPattern;
                    break;

                case "date":
                    target["type"]    = "string";
                    target["format"]  = "date";
                    target["pattern"] = DatePattern;
                    break;

                case "time-millis" or "time-micros":
                    target["type"]            = "string";
                    target["format"]          = "time";
                    target["pattern"]         = TimePattern;
                    annotation["logicalType"] = logical;
                    break;

                case "timestamp-millis" or "timestamp-micros" or "timestamp-nanos"
                  or "local-timestamp-millis" or "local-timestamp-micros" or "local-timestamp-nanos":
                    target["type"]            = "string";
                    target["format"]          = "date-time";
                    target["pattern"]         = DateTimePattern;
                    annotation["logicalType"] = logical;
                    break;

                default:
                    FillBase(primitive.Kind, target, annotation);

                    // Unknown and custom logical types are carried rather than dropped: the Avro
                    // specification says an implementation that does not recognise one must ignore it.
                    if (logical is not null) annotation["logicalType"] = logical;

                    break;
            }

            Annotate(target, Merge(annotation, Props(raw, PrimitiveKeys)));
        }

        static void FillBase(AvroPrimitiveKind kind, JsonObject target, JsonObject annotation) {
            switch (kind) {
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

                // JSON Schema has one numeric type, so double is the documented default and only float
                // is marked. Marking both would state the default twice.
                case AvroPrimitiveKind.Float:
                    target["type"]     = "number";
                    annotation["type"] = "float";
                    break;

                case AvroPrimitiveKind.Double:
                    target["type"] = "number";
                    break;

                case AvroPrimitiveKind.Bytes:
                    target["type"]            = "string";
                    target["contentEncoding"] = "base64";
                    target["pattern"]         = Base64Pattern;
                    break;

                case AvroPrimitiveKind.String:
                    target["type"] = "string";
                    break;
            }
        }

        static void FillDecimal(AvroJsonValue? raw, JsonObject target, JsonObject annotation) {
            var precision = Integer(raw, "precision");
            var scale     = Integer(raw, "scale") ?? 0;

            target["type"]    = "string";
            target["pattern"] = DecimalPattern(precision, scale);

            annotation["logicalType"] = "decimal";

            // The pattern pins the scale, so Import reads it back out of the regex and no key is needed.
            // Nothing bounds total digits, which is why precision is.
            if (precision is not null) annotation["precision"] = precision;
        }

        #endregion // Primitives and logical types

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

        /// <summary>Rewrites an Avro default in the JSON Schema representation of the same value.</summary>
        /// <remarks>The two differ wherever a <c>bytes</c> or a <c>fixed</c> is reached.</remarks>
        JsonNode? ToJsonSchemaDefault(AvroJsonValue value, AvroSchema schema) {
            switch (document.Resolve(schema)) {
                // An Avro union default is a value of the union's first branch.
                case AvroUnion union when union.Branches.Count > 0:
                    return ToJsonSchemaDefault(value, union.Branches[0]);

                case AvroPrimitive { Kind: AvroPrimitiveKind.Bytes } or AvroFixed when value.Kind is AvroJsonKind.String:
                    return ToBase64(value.Text);

                case AvroRecord record when value.Kind is AvroJsonKind.Object: {
                    var members = new JsonObject();

                    foreach (var field in record.Fields)
                        if (value[field.Name] is { } member)
                            members[field.Name] = ToJsonSchemaDefault(member, field.Type);

                    return members;
                }

                case AvroArray array when value.Kind is AvroJsonKind.Array:
                    return new JsonArray(value.Items.Select(item => ToJsonSchemaDefault(item, array.Items)).ToArray());

                case AvroMap map when value.Kind is AvroJsonKind.Object: {
                    var entries = new JsonObject();

                    foreach (var (key, item) in value.Members.OrderBy(member => member.Key, StringComparer.Ordinal))
                        entries[key] = ToJsonSchemaDefault(item, map.Values);

                    return entries;
                }

                default:
                    return ToJsonNode(value);
            }
        }

        // RFC 6901: '~' escapes to '~0' and '/' to '~1', and '~' must be replaced first. An Avro fullname
        // can contain neither, so this only guards a hand-edited $defs key.
        static string Pointer(string fullName) => $"#/$defs/{fullName.Replace("~", "~0").Replace("/", "~1")}";
    }
}
