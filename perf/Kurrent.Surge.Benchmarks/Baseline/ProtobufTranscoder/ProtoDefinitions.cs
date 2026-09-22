using ProtobufTranscoder.Descriptors;
using ProtobufTranscoder.Parser;
using ProtobufTranscoder.WellKnownTypes;

namespace ProtobufTranscoder;

/// <summary>
/// Resolved set of protobuf descriptors built from parsed .proto files.
/// </summary>
public sealed class ProtoDefinitions
{
    readonly Dictionary<string, MessageDescriptor>                                  _messages   = new();
    readonly Dictionary<string, EnumDescriptor>                                     _enums      = new();
    readonly Dictionary<(string messageFullName, int fieldNumber), FieldDescriptor> _extensions = new();

    ProtoDefinitions() { }

    public MessageDescriptor FindMessageType(string fullName)
    {
        if (_messages.TryGetValue(fullName, out var desc))
            return desc;
        throw new ArgumentException($"Unknown message type: {fullName}");
    }

    public MessageDescriptor? TryFindMessageType(string fullName) =>
        _messages.GetValueOrDefault(fullName);

    public EnumDescriptor FindEnumType(string fullName)
    {
        if (_enums.TryGetValue(fullName, out var desc))
            return desc;
        throw new ArgumentException($"Unknown enum type: {fullName}");
    }

    public EnumDescriptor? TryFindEnumType(string fullName) =>
        _enums.GetValueOrDefault(fullName);

    public IReadOnlyDictionary<string, MessageDescriptor> AllMessages => _messages;

    internal FieldDescriptor? FindExtension(string messageFullName, int fieldNumber) =>
        _extensions.GetValueOrDefault((messageFullName, fieldNumber));

    public static ProtoDefinitions Build(params ProtoSchema[] schemas)
    {
        var resolver = new InlineResolver(schemas);
        var fileNames = schemas.Select(s => s.Name);
        return Build(resolver, [.. fileNames]);
    }

    public static ProtoDefinitions Build(IProtoSchemaResolver resolver, params string[] protoFiles)
    {
        var pool = new ProtoDefinitions();
        var parsed = new Dictionary<string, FileDescriptorProto>();
        var wellKnownResolver = new WellKnownProtoResolver();

        foreach (var file in protoFiles)
            ParseRecursive(file, resolver, wellKnownResolver, parsed);

        foreach (var file in parsed.Values)
            ResolveFile(file, pool);

        pool.LinkFieldTypes(parsed.Values);

        foreach (var file in parsed.Values)
            ResolveExtendBlocks(file, pool);

        foreach (var file in parsed.Values)
            ResolveFeatures(file, pool);

        return pool;
    }

    static void ParseRecursive(
        string fileName,
        IProtoSchemaResolver userResolver,
        WellKnownProtoResolver wellKnownResolver,
        Dictionary<string, FileDescriptorProto> parsed)
    {
        if (parsed.ContainsKey(fileName))
            return;

        var content = wellKnownResolver.Resolve(fileName) ?? userResolver.Resolve(fileName);
        if (content is null)
            throw new ProtoParseException(fileName, 0, 0, $"Cannot resolve import: {fileName}");

        var file = ProtoParser.Parse(fileName, content);
        parsed[fileName] = file;

        foreach (var import in file.Imports)
            ParseRecursive(import, userResolver, wellKnownResolver, parsed);
    }

    static void ResolveFile(FileDescriptorProto file, ProtoDefinitions pool)
    {
        var prefix = string.IsNullOrEmpty(file.Package) ? "" : file.Package + ".";

        foreach (var enumProto in file.Enums)
            ResolveEnum(prefix, enumProto, pool);

        foreach (var msgProto in file.Messages)
            ResolveMessage(prefix, msgProto, pool);
    }

    static void ResolveEnum(string prefix, EnumDescriptorProto enumProto, ProtoDefinitions pool)
    {
        var fullName = prefix + enumProto.Name;
        var values = enumProto.Values
            .Select(v => new EnumValueDescriptor(v.Name, v.Number))
            .ToList();
        pool._enums[fullName] = new EnumDescriptor(fullName, values);
    }

    static void ResolveMessage(string prefix, MessageDescriptorProto msgProto, ProtoDefinitions pool)
    {
        var fullName = prefix + msgProto.Name;
        var nestedPrefix = fullName + ".";

        foreach (var e in msgProto.Enums)
            ResolveEnum(nestedPrefix, e, pool);

        var nestedMessages = new List<MessageDescriptor>();
        foreach (var nested in msgProto.NestedMessages)
        {
            ResolveMessage(nestedPrefix, nested, pool);
            nestedMessages.Add(pool._messages[nestedPrefix + nested.Name]);
        }

        var oneofs = msgProto.Oneofs.Select(o => new OneofDescriptor(o)).ToList();

        var fields = new List<FieldDescriptor>();
        foreach (var f in msgProto.Fields)
        {
            var jsonName = f.JsonName ?? FieldDescriptor.ToJsonName(f.Name);
            var field = new FieldDescriptor(f.Name, jsonName, f.Number, f.Type, f.Label)
            {
                IsPacked = f.IsPacked,
                IsMap = f.IsMap,
                IsGroupEncoded = f.IsGroupEncoded,
                HasExplicitDefault = f.DefaultValue is not null,
            };

            if (f.OneofIndex >= 0 && f.OneofIndex < oneofs.Count)
            {
                field.ContainingOneof = oneofs[f.OneofIndex];
                oneofs[f.OneofIndex].AddField(field);
            }

            fields.Add(field);
        }

        var extensionRanges = msgProto.ExtensionRanges
            .Select(r => new ExtensionRange(r.Start, r.End))
            .ToList();

        var enumDescs = msgProto.Enums
            .Select(e => pool._enums[nestedPrefix + e.Name])
            .ToList();

        var descriptor = new MessageDescriptor(fullName, fields, oneofs, nestedMessages, enumDescs, extensionRanges)
        {
            IsMapEntry = msgProto.IsMapEntry,
            Pool = pool,
        };
        pool._messages[fullName] = descriptor;

        foreach (var extendBlock in msgProto.ExtendBlocks)
            ResolveExtendBlockInner(extendBlock, nestedPrefix, pool);
    }

    static void ResolveExtendBlocks(FileDescriptorProto file, ProtoDefinitions pool)
    {
        var prefix = string.IsNullOrEmpty(file.Package) ? "" : file.Package + ".";

        foreach (var extendBlock in file.ExtendBlocks)
            ResolveExtendBlockInner(extendBlock, prefix, pool);

        foreach (var msg in file.Messages)
            ResolveExtendBlocksInMessage(msg, prefix + msg.Name + ".", pool);
    }

    static void ResolveExtendBlocksInMessage(MessageDescriptorProto msgProto, string prefix, ProtoDefinitions pool)
    {
        foreach (var extendBlock in msgProto.ExtendBlocks)
            ResolveExtendBlockInner(extendBlock, prefix, pool);

        foreach (var nested in msgProto.NestedMessages)
            ResolveExtendBlocksInMessage(nested, prefix + nested.Name + ".", pool);
    }

    static void ResolveExtendBlockInner(ExtendBlockProto extendBlock, string contextPrefix, ProtoDefinitions pool)
    {
        var extendeeName = pool.ResolveTypeName(extendBlock.ExtendeeTypeName, contextPrefix.TrimEnd('.'));
        var extendee = pool.TryFindMessageType(extendeeName);
        if (extendee is null) return;

        foreach (var f in extendBlock.Fields)
        {
            var jsonName = f.JsonName ?? FieldDescriptor.ToJsonName(f.Name);
            var field = new FieldDescriptor(f.Name, jsonName, f.Number, f.Type, f.Label)
            {
                IsPacked = f.IsPacked,
                HasExplicitDefault = f.DefaultValue is not null,
            };

            if (!string.IsNullOrEmpty(f.TypeName))
            {
                var typeName = pool.ResolveTypeName(f.TypeName, contextPrefix.TrimEnd('.'));
                if (field.Type is FieldType.Message or FieldType.Group)
                {
                    var enumDesc = pool.TryFindEnumType(typeName);
                    if (enumDesc is not null)
                    {
                        field.Type = FieldType.Enum;
                        field.EnumType = enumDesc;
                    }
                    else
                    {
                        field.MessageType = pool.TryFindMessageType(typeName);
                        if (field.MessageType is { IsMapEntry: true })
                            field.IsMap = true;
                    }
                }
                else if (field.Type == FieldType.Enum)
                {
                    field.EnumType = pool.TryFindEnumType(typeName);
                }
            }

            pool._extensions[(extendeeName, f.Number)] = field;
        }
    }

    static void ResolveFeatures(FileDescriptorProto file, ProtoDefinitions pool)
    {
        var fileFeatures = FeatureSet.GetDefault(file.Syntax);
        fileFeatures = FeatureSet.Resolve(fileFeatures, file.Features);

        var prefix = string.IsNullOrEmpty(file.Package) ? "" : file.Package + ".";

        foreach (var enumProto in file.Enums)
        {
            var fullName = prefix + enumProto.Name;
            if (pool._enums.TryGetValue(fullName, out var enumDesc))
            {
                var enumFeatures = FeatureSet.Resolve(fileFeatures, enumProto.Features);
                enumDesc.IsClosed = enumFeatures.EnumType == Descriptors.EnumType.Closed;
            }
        }

        foreach (var msgProto in file.Messages)
            ResolveFeaturesForMessage(msgProto, prefix, fileFeatures, file, pool);
    }

    static void ResolveFeaturesForMessage(
        MessageDescriptorProto msgProto, string prefix, FeatureSet parentFeatures,
        FileDescriptorProto file, ProtoDefinitions pool)
    {
        var fullName = prefix + msgProto.Name;
        var nestedPrefix = fullName + ".";
        var msgFeatures = FeatureSet.Resolve(parentFeatures, msgProto.Features);

        if (!pool._messages.TryGetValue(fullName, out var msgDesc))
            return;

        foreach (var enumProto in msgProto.Enums)
        {
            var enumFullName = nestedPrefix + enumProto.Name;
            if (pool._enums.TryGetValue(enumFullName, out var enumDesc))
            {
                var enumFeatures = FeatureSet.Resolve(msgFeatures, enumProto.Features);
                enumDesc.IsClosed = enumFeatures.EnumType == Descriptors.EnumType.Closed;
            }
        }

        for (var i = 0; i < msgProto.Fields.Count && i < msgDesc.Fields.Count; i++)
        {
            var fieldProto = msgProto.Fields[i];
            var fieldDesc = msgDesc.Fields[i];
            var fieldFeatures = FeatureSet.Resolve(msgFeatures, fieldProto.Features);

            ResolveFieldPresenceAndDefault(fieldDesc, fieldProto, fieldFeatures, file.Syntax, pool);

            if (file.Syntax == "editions" && fieldDesc.IsRepeated && IsPackableType(fieldDesc.Type))
                fieldDesc.IsPacked = fieldFeatures.RepeatedFieldEncoding == RepeatedFieldEncoding.Packed;

            if (file.Syntax == "editions" && fieldDesc.Type == FieldType.Message &&
                fieldFeatures.MessageEncoding == Descriptors.MessageEncoding.Delimited)
                fieldDesc.IsGroupEncoded = true;
        }

        // Resolve nested messages
        foreach (var nested in msgProto.NestedMessages)
            ResolveFeaturesForMessage(nested, nestedPrefix, msgFeatures, file, pool);
    }

    static void ResolveFieldPresenceAndDefault(
        FieldDescriptor fieldDesc, FieldDescriptorProto fieldProto,
        FeatureSet resolvedFeatures, string syntax, ProtoDefinitions pool)
    {
        if (fieldDesc.IsRepeated)
            fieldDesc.Presence = FieldPresence.Implicit;
        else if (fieldDesc.ContainingOneof is not null)
            fieldDesc.Presence = FieldPresence.Explicit;
        else if (fieldDesc.Type is FieldType.Message or FieldType.Group)
            fieldDesc.Presence = FieldPresence.Explicit;
        else if (syntax == "proto2")
            fieldDesc.Presence = fieldProto.Label == FieldLabel.Required
                ? FieldPresence.Required
                : FieldPresence.Explicit;
        else if (syntax == "proto3")
        {
            // All singular scalar fields are implicit. We do not track proto3 optional
            // (synthetic oneof), so presence is always implicit in proto3.
            fieldDesc.Presence = FieldPresence.Implicit;
        }
        else
            fieldDesc.Presence = resolvedFeatures.FieldPresence; // editions

        if (fieldProto.DefaultValue is not null)
        {
            fieldDesc.DefaultValue = ParseDefaultValue(fieldProto.DefaultValue, fieldDesc.Type, fieldDesc.EnumType);
            fieldDesc.HasExplicitDefault = true;
        }
        else if (!fieldDesc.IsRepeated)
        {
            fieldDesc.DefaultValue = FieldDescriptor.GetZeroDefault(fieldDesc.Type, fieldDesc.EnumType);
        }
    }

    static object? ParseDefaultValue(string rawDefault, FieldType type, EnumDescriptor? enumType)
    {
        return type switch
        {
            FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 =>
                int.TryParse(rawDefault, out var i32) ? i32 : 0,
            FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 =>
                long.TryParse(rawDefault, out var i64) ? i64 : 0L,
            FieldType.UInt32 or FieldType.Fixed32 =>
                uint.TryParse(rawDefault, out var u32) ? u32 : 0u,
            FieldType.UInt64 or FieldType.Fixed64 =>
                ulong.TryParse(rawDefault, out var u64) ? u64 : 0uL,
            FieldType.Float => rawDefault switch
            {
                "inf" or "infinity" => float.PositiveInfinity,
                "-inf" or "-infinity" => float.NegativeInfinity,
                "nan" => float.NaN,
                _ => float.TryParse(rawDefault, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0.0f,
            },
            FieldType.Double => rawDefault switch
            {
                "inf" or "infinity" => double.PositiveInfinity,
                "-inf" or "-infinity" => double.NegativeInfinity,
                "nan" => double.NaN,
                _ => double.TryParse(rawDefault, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0.0,
            },
            FieldType.Bool => rawDefault == "true",
            FieldType.String => rawDefault.Length >= 2 && rawDefault[0] == '"' && rawDefault[^1] == '"'
                ? rawDefault[1..^1]
                : rawDefault,
            FieldType.Bytes => ParseBytesDefault(rawDefault),
            FieldType.Enum => ResolveEnumDefault(rawDefault, enumType),
            _ => null,
        };
    }

    static byte[] ParseBytesDefault(string rawDefault)
    {
        // Strip quotes if present
        var s = rawDefault;
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            s = s[1..^1];

        // Handle escape sequences
        var bytes = new List<byte>();
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i++;
                if (s[i] == 'x' && i + 2 < s.Length)
                {
                    bytes.Add(Convert.ToByte(s.Substring(i + 1, 2), 16));
                    i += 2;
                }
                else
                {
                    bytes.Add((byte)s[i]);
                }
            }
            else
            {
                bytes.Add((byte)s[i]);
            }
        }
        return bytes.ToArray();
    }

    static object ResolveEnumDefault(string rawDefault, EnumDescriptor? enumType)
    {
        if (enumType is null)
            return 0;

        foreach (var v in enumType.Values)
        {
            if (v.Name == rawDefault)
                return v;
        }

        // Try parsing as integer
        if (int.TryParse(rawDefault, out var num))
            return enumType.FindValueByNumber(num) ?? (object)num;

        return enumType.Values.Count > 0 ? enumType.Values[0] : (object)0;
    }

    static bool IsPackableType(FieldType type) =>
        type is not (FieldType.String or FieldType.Bytes or FieldType.Message or FieldType.Group);

    internal void LinkFieldTypes(IEnumerable<FileDescriptorProto> files)
    {
        foreach (var file in files)
        {
            var prefix = string.IsNullOrEmpty(file.Package) ? "" : file.Package + ".";
            LinkFieldsInMessages(file.Messages, prefix);
        }
    }

    void LinkFieldsInMessages(IReadOnlyList<MessageDescriptorProto> messages, string prefix)
    {
        foreach (var msgProto in messages)
        {
            var fullName = prefix + msgProto.Name;
            var nestedPrefix = fullName + ".";

            if (!_messages.TryGetValue(fullName, out var msgDescriptor))
                continue;

            for (var i = 0; i < msgProto.Fields.Count; i++)
            {
                var fieldProto = msgProto.Fields[i];
                var fieldDesc = msgDescriptor.Fields[i];

                if (!string.IsNullOrEmpty(fieldProto.TypeName))
                {
                    var typeName = ResolveTypeName(fieldProto.TypeName, fullName);

                    if (fieldDesc.Type is FieldType.Message or FieldType.Group)
                    {
                        // Check if this is actually an enum (parser can't tell)
                        var enumDesc = TryFindEnumType(typeName);
                        if (enumDesc is not null)
                        {
                            fieldDesc.Type = FieldType.Enum;
                            fieldDesc.EnumType = enumDesc;
                        }
                        else
                        {
                            fieldDesc.MessageType = TryFindMessageType(typeName);
                            if (fieldDesc.MessageType is { IsMapEntry: true })
                                fieldDesc.IsMap = true;
                        }
                    }
                    else if (fieldDesc.Type == FieldType.Enum)
                    {
                        fieldDesc.EnumType = TryFindEnumType(typeName);
                    }
                }
            }

            LinkFieldsInMessages(msgProto.NestedMessages, nestedPrefix);
        }
    }

    string ResolveTypeName(string typeName, string contextFullName)
    {
        if (typeName.StartsWith('.'))
            return typeName[1..];

        var parts = contextFullName.Split('.');
        for (var i = parts.Length; i >= 0; i--)
        {
            var candidate = i > 0
                ? string.Join('.', parts.Take(i)) + "." + typeName
                : typeName;

            if (_messages.ContainsKey(candidate) || _enums.ContainsKey(candidate))
                return candidate;
        }

        return typeName;
    }

    sealed class InlineResolver(ProtoSchema[] files) : IProtoSchemaResolver
    {
        readonly Dictionary<string, string> _files = files.ToDictionary(f => f.Name, f => f.Content);

        public string? Resolve(string importPath) => _files.GetValueOrDefault(importPath);
    }
}
