using System.Text.Json.Nodes;
using Google.Protobuf.Reflection;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Json;
using Kurrent.Surge.Schema;
using Type = Google.Protobuf.Reflection.FieldType;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;

public sealed partial class ProtobufJsonSchema {
    static JsonNode ExportDocument(ProtoSchemaSet schema, string messageTypeName, JsonSchemaExportOptions options) {
        var exported = new Exporter(options).Run(schema.FindMessage(messageTypeName));

        return options.Canonical ? Canonical(exported)! : exported;
    }

    /// <remarks>
    /// Only a leaf is cloned, which is what detaches it from the document it came from. An object or
    /// an array is rebuilt from children that are already detached, so cloning it first would clone
    /// every subtree once per level above it — measured at 3.3x the allocation of a plain export.
    /// <para>
    /// Object keys are sorted; array items never are. oneOf order and enum order both carry meaning,
    /// so reordering either would change what the schema says.
    /// </para>
    /// </remarks>
    static JsonNode? Canonical(JsonNode? node) => node switch {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Canonical(p.Value)))),

        JsonArray a => new JsonArray(a.Select(Canonical).ToArray()),

        _ => node?.DeepClone()
    };

    sealed class Exporter(JsonSchemaExportOptions options) {
        readonly Dictionary<string, JsonObject> _defs  = [];
        readonly List<string>                   _order = [];

        public JsonObject Run(MessageDescriptor root) {
            var result = new JsonObject { ["$schema"] = Dialect };

            Define(root);

            result["$ref"] = Pointer(root.FullName);

            var defs  = new JsonObject();
            var names = options.Canonical ? _order.OrderBy(name => name, StringComparer.Ordinal) : _order.AsEnumerable();

            foreach (var name in names) defs[name] = _defs[name];

            result[Container] = defs;

            return result;
        }

        #region ->> Messages <<-

        void Define(MessageDescriptor message) {
            // Reserved before the body is built: a self-referencing message reaches this name again
            // while its own fields are still being walked, and must find a $ref rather than recurse.
            if (!_defs.TryAdd(message.FullName, new JsonObject())) return;

            _order.Add(message.FullName);

            Body(message, _defs[message.FullName]);
        }

        void Body(MessageDescriptor message, JsonObject target) {
            var properties = new JsonObject();
            var required   = new JsonArray();

            foreach (var field in message.Fields.InDeclarationOrder()) {
                properties[field.JsonName] = Field(field);

                // Only a proto2 required field is always on the wire. Everything else is written when
                // present, so its absence is legal and the schema must not demand it.
                if (field.IsRequired) required.Add((JsonNode)field.JsonName);
            }

            target["type"]       = "object";
            target["properties"] = properties;

            if (required.Count > 0) target["required"] = required;

            // The mapping never writes a field the schema does not declare: an unknown field is skipped
            // rather than emitted, so the document is closed.
            target["additionalProperties"] = false;

            Describe(target, message.Declaration);
        }

        #endregion // Messages

        #region ->> Fields <<-

        JsonObject Field(FieldDescriptor field) {
            var node = field.IsMap
                ? Map(field)
                : field.IsRepeated
                    ? new JsonObject { ["type"] = "array", ["items"] = Value(field) }
                    : Value(field);

            Describe(node, field.Declaration);

            return node;
        }

        /// <remarks>
        /// A map is written as a JSON object, so its keys are strings whatever the declared key type
        /// is. An integral or boolean key is spelled out, which no keyword can express.
        /// </remarks>
        JsonObject Map(FieldDescriptor field) => new() {
            ["type"]                 = "object",
            ["additionalProperties"] = Value(field.MessageType.FindFieldByNumber(2))
        };

        #endregion // Fields

        #region ->> Values <<-

        JsonObject Value(FieldDescriptor field) {
            switch (field.FieldType) {
                case Type.Bool:
                    return new JsonObject { ["type"] = "boolean" };

                case Type.Int32 or Type.SInt32 or Type.SFixed32:
                    return new JsonObject { ["type"] = "integer", ["minimum"] = int.MinValue, ["maximum"] = int.MaxValue };

                case Type.UInt32 or Type.Fixed32:
                    return new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = uint.MaxValue };

                // A JSON number cannot hold the full 64-bit range without loss, so the mapping writes a
                // decimal string. The pattern is what asserts it.
                case Type.Int64 or Type.SInt64 or Type.SFixed64:
                    return new JsonObject { ["type"] = "string", ["pattern"] = SignedDigitsPattern };

                case Type.UInt64 or Type.Fixed64:
                    return new JsonObject { ["type"] = "string", ["pattern"] = UnsignedDigitsPattern };

                case Type.Float or Type.Double:
                    return NonFinite();

                case Type.String:
                    return new JsonObject { ["type"] = "string" };

                case Type.Bytes:
                    return new JsonObject { ["type"] = "string", ["pattern"] = Base64Pattern };

                case Type.Enum:
                    return Enum(field.EnumType);

                default:
                    return Message(field.MessageType);
            }

            // NaN and the two infinities have no JSON number form, so the mapping spells them as
            // strings and a finite value stays a number. Both have to validate.
            static JsonObject NonFinite() {
                var names = new JsonArray();

                foreach (var value in NonFiniteValues) names.Add((JsonNode)value);

                return new JsonObject {
                    ["oneOf"] = new JsonArray(
                        (JsonNode)new JsonObject { ["type"] = "number" },
                        new JsonObject { ["type"] = "string", ["enum"] = names })
                };
            }
        }

        /// <remarks>
        /// Openness is read from the schema rather than chosen: a proto2 enum is closed, and a proto3
        /// or editions enum accepts a number it has never heard of, which the mapping emits as a bare
        /// number because there is no name to emit.
        /// </remarks>
        JsonObject Enum(EnumDescriptor descriptor) {
            var names = new JsonArray();

            // An alias lets two names share a number, and the mapping writes the first declared one.
            foreach (var value in descriptor.Values.DistinctBy(value => value.Number))
                names.Add((JsonNode)value.Name);

            var named = new JsonObject { ["type"] = "string", ["enum"] = names };

            Describe(named, descriptor.Declaration);

            if (descriptor.File.Syntax is Syntax.Proto2) return named;

            return new JsonObject {
                ["oneOf"] = new JsonArray((JsonNode)named, new JsonObject { ["type"] = "integer" })
            };
        }

        /// <remarks>
        /// A well-known type has a JSON form of its own rather than an object of fields, so it is
        /// written inline. Everything else is hoisted and referenced, which is what makes recursion
        /// and reuse expressible at all.
        /// </remarks>
        JsonObject Message(MessageDescriptor descriptor) {
            if (ProtoJson.IsWellKnown(descriptor)) return WellKnown(descriptor);

            Define(descriptor);

            return new JsonObject { ["$ref"] = Pointer(descriptor.FullName) };
        }

        JsonObject WellKnown(MessageDescriptor descriptor) {
            switch (descriptor.FullName) {
                // RFC 3339 text, a duration ending in 's', and a comma-joined path list. All three are
                // strings on the wire; no pattern is asserted, because a schema that rejects its own
                // transcoder's output is wrong rather than strict.
                case "google.protobuf.Timestamp":
                case "google.protobuf.Duration":
                case "google.protobuf.FieldMask":
                    return new JsonObject { ["type"] = "string" };

                case "google.protobuf.Struct":
                    return new JsonObject { ["type"] = "object" };

                case "google.protobuf.ListValue":
                    return new JsonObject { ["type"] = "array" };

                // Value is any JSON at all, so it asserts nothing.
                case "google.protobuf.Value":
                    return new JsonObject();

                case "google.protobuf.Empty":
                    return new JsonObject { ["type"] = "object", ["additionalProperties"] = false };

                // The payload's type is named at run time by @type, so its shape is not knowable here.
                // A resolvable payload flattens its fields beside @type and an unresolvable one nests
                // raw bytes under 'value', and both have to validate.
                case "google.protobuf.Any":
                    return new JsonObject {
                        ["type"]     = "object",
                        ["required"] = new JsonArray((JsonNode)"@type")
                    };

                // A wrapper writes the value it wraps, with no object around it.
                default:
                    return Value(descriptor.FindFieldByNumber(1));
            }
        }

        #endregion // Values

        static void Describe(JsonObject target, DescriptorDeclaration? declaration) {
            if (declaration?.LeadingComments is { Length: > 0 } comments) target["description"] = comments;
        }

        // RFC 6901: '~' escapes to '~0' and '/' to '~1', and '~' must be replaced first. A protobuf
        // fullname can contain neither, so this only guards a hand-edited container key.
        static string Pointer(string fullName) =>
            $"#/{Container}/{fullName.Replace("~", "~0").Replace("/", "~1")}";
    }
}
