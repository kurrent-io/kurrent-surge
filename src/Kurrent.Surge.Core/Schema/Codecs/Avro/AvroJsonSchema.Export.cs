using System.Text.Json.Nodes;
using Kurrent.Surge.Codecs.Avro.Generators.Model;
using Kurrent.Surge.Schema;
using AvroJsonKind = Kurrent.Surge.Codecs.Avro.Generators.Json.JsonKind;
using AvroJsonValue = Kurrent.Surge.Codecs.Avro.Generators.Json.JsonValue;
using JsonParser = Kurrent.Surge.Codecs.Avro.Generators.Json.JsonParser;

namespace Kurrent.Surge.Codecs.Avro;

public sealed partial class AvroJsonSchema {
    static JsonNode ExportDocument(string avroSchema, JsonSchemaExportOptions options) {
        var document = Parse(avroSchema, nameof(avroSchema));

        // The model resolves namespaces and named references; the raw tree carries an enum's own default,
        // which the model does not keep. Export reads both.
        if (!JsonParser.TryParse(avroSchema, out var raw, out var error, out var offset))
            throw new ArgumentException($"The Avro schema could not be parsed at offset {offset}: {error}", nameof(avroSchema));

        var exported = new Exporter(document).Run(raw);

        return options.Canonical ? Canonical(exported)! : exported;
    }

    /// <remarks>
    /// Only a leaf is cloned, which is what detaches it from the document it came from. An object or
    /// an array is rebuilt from children that are already detached, so cloning it first would clone
    /// every subtree once per level above it.
    /// <para>
    /// Object keys are sorted; array items never are. oneOf order is the union tag and enum order is
    /// the symbol ordinal, so reordering either would change what the schema says.
    /// </para>
    /// </remarks>
    static JsonNode? Canonical(JsonNode? node) => node switch {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Canonical(p.Value)))),

        JsonArray a => new JsonArray(a.Select(Canonical).ToArray()),

        _ => node?.DeepClone()
    };

    sealed class Exporter(AvroSchemaDocument document) {
        readonly Dictionary<string, JsonObject> _defs  = [];
        readonly List<string>                   _order = [];

        public JsonObject Run(AvroJsonValue raw) {
            var result = new JsonObject { ["$schema"] = Dialect };

            Fill(document.Root, raw, result);

            if (_order.Count == 0) return result;

            var defs = new JsonObject();

            foreach (var name in _order) defs[name] = _defs[name];

            result[Container] = defs;

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
                    FillPrimitive(primitive, target);
                    return;

                case AvroArray array:
                    target["type"]  = "array";
                    target["items"] = Node(array.Items, raw?["items"]);
                    return;

                case AvroMap map:
                    // No propertyNames: a JSON object's keys are strings by definition, so asserting it
                    // asserts nothing.
                    target["type"]                 = "object";
                    target["additionalProperties"] = Node(map.Values, raw?["values"]);
                    return;

                case AvroUnion union:
                    FillUnion(union, raw, target);
                    return;
            }

            void Define(IAvroNamed named, AvroJsonValue? raw) {
                // Reserved before the body is built: a self-referencing record reaches this name again while
                // its own fields are still being walked, and must find a $ref rather than recurse forever.
                if (_defs.ContainsKey(named.FullName)) return;

                var definition = new JsonObject();

                _defs[named.FullName] = definition;
                _order.Add(named.FullName);

                Body(named, raw is { Kind: AvroJsonKind.Object } ? raw : named.Declaration, definition);
            }

            // A logical type does not change the Avro JSON encoding: the value is written as its base type,
            // so a date is a day count and a timestamp is a long. The base type is what the schema asserts.
            static void FillPrimitive(AvroPrimitive primitive, JsonObject target) {
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

                    // JSON Schema has one numeric type, so float and double are both plain numbers.
                    case AvroPrimitiveKind.Float:
                    case AvroPrimitiveKind.Double:
                        target["type"] = "number";
                        break;

                    case AvroPrimitiveKind.Bytes:
                        target["type"]    = "string";
                        target["pattern"] = ByteStringPattern;
                        break;

                    case AvroPrimitiveKind.String:
                        target["type"] = "string";
                        break;
                }
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
                case AvroFixed @fixed:  FillFixed(@fixed, target);               break;
            }

            void FillRecord(AvroRecord record, AvroJsonValue? raw, JsonObject target) {
                var properties = new JsonObject();
                var required   = new JsonArray();
                var fields     = raw?["fields"];

                for (var index = 0; index < record.Fields.Count; index++) {
                    var field    = record.Fields[index];
                    var fieldRaw = fields is { Kind: AvroJsonKind.Array } && index < fields.Items.Count ? fields.Items[index] : null;

                    properties[field.Name] = Property(field, fieldRaw);

                    // Every field is present on the Avro wire, but a field with a default is legitimately
                    // absent from a JSON document.
                    if (field.Default is null) required.Add((JsonNode)field.Name);
                }

                target["type"]       = "object";
                target["properties"] = properties;

                if (required.Count > 0) target["required"] = required;

                target["additionalProperties"] = false;

                Describe(target, record.Doc);

                JsonObject Property(AvroField field, AvroJsonValue? raw) {
                    var property = Node(field.Type, raw?["type"]);

                    Describe(property, field.Doc);

                    // Carried verbatim. Under the Avro JSON encoding the two representations are the same one,
                    // so a bytes default stays the byte string Avro wrote and needs no conversion either way.
                    if (field.Default is not null) property["default"] = ToJsonNode(field.Default);

                    return property;
                }
            }

            static void FillEnum(AvroEnum @enum, AvroJsonValue? raw, JsonObject target) {
                var symbols = new JsonArray();

                foreach (var symbol in @enum.Symbols) symbols.Add((JsonNode)symbol);

                target["type"] = "string";
                target["enum"] = symbols;

                Describe(target, @enum.Doc);

                // An enum's own default is a symbol, so the standard keyword carries it whole. It is read
                // from the raw tree because AvroEnum does not model it.
                if (raw?["default"] is { } @default) target["default"] = ToJsonNode(@default);
            }

            static void FillFixed(AvroFixed @fixed, JsonObject target) {
                // minLength and maxLength recover the byte count exactly, because the encoding is one
                // character per byte.
                target["type"]      = "string";
                target["minLength"] = @fixed.Size;
                target["maxLength"] = @fixed.Size;
                target["pattern"]   = ByteStringPattern;

                Describe(target, @fixed.Doc);
            }
        }

        #endregion // Nodes

        static void Describe(JsonObject target, string? doc) {
            if (!string.IsNullOrEmpty(doc)) target["description"] = doc;
        }

        // RFC 6901: '~' escapes to '~0' and '/' to '~1', and '~' must be replaced first. An Avro fullname
        // can contain neither, so this only guards a hand-edited container key.
        static string Pointer(string fullName) =>
            $"#/{Container}/{fullName.Replace("~", "~0").Replace("/", "~1")}";
    }
}
