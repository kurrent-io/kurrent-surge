using System.Text.Json.Nodes;
using Google.Protobuf.Reflection;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Json;
using Kurrent.Surge.Schema;
using Type = Google.Protobuf.Reflection.FieldType;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;

public static partial class ProtobufJsonSchema {
    static JsonNode ExportDocument(ProtoSchemaSet schema, string messageTypeName, ProtobufJsonSchemaOptions options) {
        var exported = new Exporter(schema, options).Run(schema.FindMessage(messageTypeName));

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

    sealed class Exporter(ProtoSchemaSet schema, ProtobufJsonSchemaOptions options) {
        readonly Dictionary<string, JsonObject> _defs      = [];
        readonly List<string>                   _order     = [];
        readonly string                         _container = ContainerOf(options.Dialect);

        string? _rootName;

        public JsonObject Run(MessageDescriptor root) {
            var result = new JsonObject();

            if (options.DeclareDialect) result["$schema"] = DialectOf(options.Dialect);

            // Under draft 4 the root message is written here rather than hoisted, so a reference back to
            // it is '#'. Both forms are legal in every draft, and this one works around a defect in
            // NJsonSchema, which the schema registry validates with: its resolver cannot follow a root
            // $ref whose target references itself, which every recursive message does.
            if (options.Dialect is JsonSchemaDialect.Draft04) {
                _rootName       = root.FullName;
                result["title"] = root.FullName;

                Body(root, result);
            }
            else {
                Define(root);

                result["$ref"] = Pointer(root.FullName);
            }

            if (_order.Count == 0) return result;

            var defs  = new JsonObject();
            var names = options.Canonical ? _order.OrderBy(name => name, StringComparer.Ordinal) : _order.AsEnumerable();

            foreach (var name in names) defs[name] = _defs[name];

            result[_container] = defs;

            return result;
        }

        #region ->> Messages <<-

        void Define(MessageDescriptor message) {
            // Reserved before the body is built: a self-referencing message reaches this name again
            // while its own fields are still being walked, and must find a $ref rather than recurse.
            if (message.FullName == _rootName || _defs.ContainsKey(message.FullName)) return;

            var definition = new JsonObject();

            _defs[message.FullName] = definition;
            _order.Add(message.FullName);

            Body(message, definition);
        }

        void Body(MessageDescriptor message, JsonObject target) {
            var properties = new JsonObject();
            var required   = new JsonArray();

            foreach (var field in message.Fields.InDeclarationOrder()) {
                properties[NameOf(field)] = Field(field);

                if (IsAlwaysPresent(field)) required.Add((JsonNode)NameOf(field));
            }

            target["type"]       = "object";
            target["properties"] = properties;

            // Draft 4 requires a non-empty array, so a message with nothing mandatory omits the keyword.
            if (required.Count > 0) target["required"] = required;

            // The mapping never writes a field the schema does not declare: an unknown field is skipped
            // rather than emitted, so the document is closed.
            target["additionalProperties"] = false;

            Describe(target, message.Declaration);
            Annotate(target, MessageAnnotation(message));
        }

        /// <remarks>
        /// A proto2 <c>required</c> field is always on the wire. Everything else is emitted only when
        /// present, unless <c>EmitDefaults</c> is on — which turns every field without presence, plus
        /// every repeated field and map, into one that is always written.
        /// </remarks>
        bool IsAlwaysPresent(FieldDescriptor field) =>
            field.IsRequired || (options.Writer.EmitDefaults && !field.HasPresence);

        string NameOf(FieldDescriptor field) => options.Writer.UseProtoFieldNames ? field.Name : field.JsonName;

        /// <remarks>
        /// The package is carried rather than derived: <c>pkg.Outer.Inner</c> splits into a package and
        /// a nesting path in more than one way, and only the declaring file knows which.
        /// </remarks>
        JsonObject MessageAnnotation(MessageDescriptor message) {
            var annotation = new JsonObject { ["syntax"] = message.File.Syntax is Syntax.Proto2 ? "proto2" : "proto3" };

            if (message.File.Package.Length > 0) annotation["package"] = message.File.Package;

            Reserved(message.ToProto(), annotation);
            Trailing(annotation, message.Declaration);

            return annotation;
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
            Annotate(node, FieldAnnotation(field));

            return node;
        }

        /// <remarks>
        /// A map is written as a JSON object, so its keys are strings whatever the declared key type is.
        /// An integral or boolean key is spelled out, and nothing in the document says which it was.
        /// </remarks>
        JsonObject Map(FieldDescriptor field) => new() {
            ["type"]                 = "object",
            ["additionalProperties"] = Value(field.MessageType.FindFieldByNumber(2))
        };

        JsonObject? FieldAnnotation(FieldDescriptor field) {
            // The wire tag. No keyword carries it, and a .proto file cannot be written without one.
            var annotation = new JsonObject { ["field"] = field.FieldNumber };

            // On a repeated field this names the element type, because the array node is where the
            // field's own facts live.
            if (DiscriminatorOf(field) is { } declared) annotation["type"] = declared;

            if (PresenceOf(field) is { } presence) annotation["presence"] = presence;

            // The JSON name is derived by lower-camel-casing unless json_name overrode it, and the
            // transform does not invert reliably — order_id, orderId and orderID all camel-case alike.
            if (!options.Writer.UseProtoFieldNames && field.JsonName != DeriveJsonName(field.Name))
                annotation["name"] = field.Name;

            if (field.ContainingOneof is { IsSynthetic: false } oneof) annotation["oneof"] = oneof.Name;

            if (field.IsMap) annotation["map"] = KeyTypeOf(field);

            if (field.FieldType is Type.Message or Type.Group && ProtoJson.IsWellKnown(field.MessageType))
                annotation["wellKnown"] = field.MessageType.FullName;

            Trailing(annotation, field.Declaration);

            return annotation;

            static string KeyTypeOf(FieldDescriptor field) {
                var key = field.MessageType.FindFieldByNumber(1);

                return DiscriminatorOf(key) ?? key.FieldType.ToString().ToLowerInvariant();
            }
        }

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
                // decimal string. The pattern is the assertion; the range lives in the type name.
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

            // NaN and the two infinities have no JSON number form, so the mapping spells them as strings
            // and a finite value stays a number. Both have to validate.
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
        /// Hoisted and referenced like a message, because an inlined enum leaves its type name nowhere
        /// in the document and <c>Import</c> cannot declare a type it cannot name.
        /// </remarks>
        JsonObject Enum(EnumDescriptor descriptor) {
            DefineEnum(descriptor);

            return new JsonObject { ["$ref"] = Pointer(descriptor.FullName) };
        }

        void DefineEnum(EnumDescriptor descriptor) {
            if (_defs.ContainsKey(descriptor.FullName)) return;

            var definition = new JsonObject();

            _defs[descriptor.FullName] = definition;
            _order.Add(descriptor.FullName);

            EnumBody(descriptor, definition);
        }

        /// <remarks>
        /// An open enum accepts a number the schema has never heard of, and the mapping emits it as a
        /// number because there is no name to emit. Only a proto2 enum is closed.
        /// </remarks>
        void EnumBody(EnumDescriptor descriptor, JsonObject target) {
            var names = new JsonArray();

            // An alias lets two names share a number, and the mapping writes the first declared one.
            foreach (var value in descriptor.Values.DistinctBy(value => value.Number))
                names.Add((JsonNode)value.Name);

            if (options.Writer.EnumsAsIntegers) {
                target["type"] = "integer";
            }
            else if (descriptor.File.Syntax is Syntax.Proto2) {
                target["type"] = "string";
                target["enum"] = names;
            }
            else {
                target["oneOf"] = new JsonArray(
                    (JsonNode)new JsonObject { ["type"] = "string", ["enum"] = names },
                    new JsonObject { ["type"] = "integer" });
            }

            Describe(target, descriptor.Declaration);
            Annotate(target, EnumAnnotation(descriptor));
        }

        /// <remarks>
        /// A protobuf enum value is an explicit integer rather than an ordinal, and <c>allow_alias</c>
        /// lets two names share one. The <c>enum</c> keyword carries neither — it is a list of names,
        /// and it cannot hold a duplicate at all.
        /// </remarks>
        /// <remarks>
        /// An array rather than an object keyed by name, because declaration order is a fact here and
        /// sorting object keys is a legal edit that would destroy it. An array is ordered by definition.
        /// </remarks>
        JsonObject EnumAnnotation(EnumDescriptor descriptor) {
            var values = new JsonArray();

            foreach (var value in descriptor.Values)
                values.Add((JsonNode)new JsonObject { ["name"] = value.Name, ["number"] = value.Number });

            // No 'kind': the enum keyword already names this an enum, and the minimality gate deleted
            // the key that used to say it a second time.
            var annotation = new JsonObject {
                ["syntax"] = descriptor.File.Syntax is Syntax.Proto2 ? "proto2" : "proto3",
                ["values"] = values
            };

            if (descriptor.File.Package.Length > 0) annotation["package"] = descriptor.File.Package;

            if (descriptor.ToProto().Options?.AllowAlias is true) annotation["allowAlias"] = true;

            Trailing(annotation, descriptor.Declaration);

            return annotation;
        }

        /// <remarks>
        /// A well-known type has a JSON form of its own rather than an object of fields, so it is
        /// written inline. Everything else is hoisted and referenced, which is what makes recursion and
        /// reuse expressible at all.
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

        #region ->> Annotation assembly <<-

        void Annotate(JsonObject target, JsonObject? annotation) {
            if (!options.Annotations || annotation is null || annotation.Count == 0) return;

            target["x-protobuf"] = annotation;
        }

        static void Describe(JsonObject target, DescriptorDeclaration? declaration) {
            if (declaration?.LeadingComments is { Length: > 0 } comments) target["description"] = comments;
        }

        /// <remarks>
        /// <c>description</c> carries the leading comment. Nothing standard carries the other two, and
        /// a detached block is often the file's licence header rather than anything about the type.
        /// </remarks>
        static void Trailing(JsonObject annotation, DescriptorDeclaration? declaration) {
            if (declaration is null) return;

            if (declaration.TrailingComments is { Length: > 0 } trailing) annotation["comments"] = trailing;

            if (declaration.LeadingDetachedComments.Count == 0) return;

            var detached = new JsonArray();

            foreach (var comment in declaration.LeadingDetachedComments) detached.Add((JsonNode)comment);

            annotation["detached"] = detached;
        }

        static void Reserved(DescriptorProto message, JsonObject annotation) {
            if (message.ReservedRange.Count > 0) {
                var ranges = new JsonArray();

                // Written as the source declares them — inclusive both ends — where the descriptor's end
                // is exclusive.
                foreach (var range in message.ReservedRange)
                    ranges.Add((JsonNode)new JsonObject { ["from"] = range.Start, ["to"] = range.End - 1 });

                annotation["reservedRanges"] = ranges;
            }

            if (message.ReservedName.Count == 0) return;

            var names = new JsonArray();

            foreach (var name in message.ReservedName) names.Add((JsonNode)name);

            annotation["reservedNames"] = names;
        }

        #endregion // Annotation assembly

        // RFC 6901: '~' escapes to '~0' and '/' to '~1', and '~' must be replaced first. A protobuf
        // fullname can contain neither, so this only guards a hand-edited container key.
        string Pointer(string fullName) =>
            fullName == _rootName
                ? "#"
                : $"#/{_container}/{fullName.Replace("~", "~0").Replace("/", "~1")}";
    }
}
