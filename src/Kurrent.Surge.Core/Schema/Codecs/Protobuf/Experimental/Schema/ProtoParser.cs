using System.Text;
using Google.Protobuf.Reflection;
using Type = Google.Protobuf.Reflection.FieldDescriptorProto.Types.Type;
using Label = Google.Protobuf.Reflection.FieldDescriptorProto.Types.Label;
using FileOptions = Google.Protobuf.Reflection.FileOptions;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;

/// <summary>
/// Recursive descent parser for <c>.proto</c> text. Covers proto2, proto3 and editions.
/// </summary>
/// <remarks>
/// Emits <see cref="FileDescriptorProto"/> — the unresolved form. Type linking, name resolution
/// and edition feature merging belong to <c>FileDescriptor.BuildFromByteStrings</c>, which reads
/// the same descriptor the C++ implementation does.
/// <para>
/// Google.Protobuf ships no <c>.proto</c> reader at any visibility, so this stage has no
/// substitute; <c>protoc</c> is a separate native binary and cannot run in-process.
/// </para>
/// </remarks>
static class ProtoParser {
    public static FileDescriptorProto Parse(string fileName, string content) {
        var lexer = new ProtoLexer(fileName, content);
        var file  = new FileDescriptorProto { Name = fileName };

        while (true) {
            var token = lexer.Next();

            if (token.Kind == TokenKind.Eof) break;

            switch (token.Value) {
                case "syntax":
                    Expect(lexer, "=");
                    file.Syntax = ExpectString(lexer);
                    Expect(lexer, ";");
                    break;

                // An editions file carries both: syntax names the dialect, edition names the year.
                // protoc emits the pair this way and BuildFromByteStrings reads it the same way.
                case "edition":
                    Expect(lexer, "=");
                    file.Edition = ParseEdition(lexer, ExpectString(lexer));
                    file.Syntax  = "editions";
                    Expect(lexer, ";");
                    break;

                case "package":
                    file.Package = ReadFullIdent(lexer);
                    Expect(lexer, ";");
                    break;

                case "import":
                    if (lexer.Peek().Value is "weak" or "public") lexer.Next();
                    file.Dependency.Add(ExpectString(lexer));
                    Expect(lexer, ";");
                    break;

                case "option": {
                    var features = TryParseFeatureOption(lexer);

                    if (features is null) SkipOption(lexer);
                    else MergeInto(file.Options ??= new FileOptions(), features);

                    break;
                }

                case "message":
                    file.MessageType.Add(ParseMessage(lexer, fileName, file.Syntax));
                    break;

                case "enum":
                    file.EnumType.Add(ParseEnum(lexer, fileName));
                    break;

                // Services are parsed only so the tokens are consumed. A transcoder decodes
                // messages, and a service declares none.
                case "service":
                    SkipBlock(lexer);
                    break;

                case "extend":
                    ParseExtendBlock(lexer, fileName, file.Syntax, file.Extension);
                    break;
            }
        }

        return file;
    }

    static DescriptorProto ParseMessage(ProtoLexer lexer, string fileName, string syntax) {
        var name = ExpectIdent(lexer);

        Expect(lexer, "{");

        var message = new DescriptorProto { Name = name };

        // A proto3 `optional` field is carried by a oneof holding only that field. Those synthetic
        // oneofs must follow every declared one, so they are appended once the body is closed.
        var synthetic = new List<FieldDescriptorProto>();

        while (true) {
            var token = lexer.Next();

            if (token.Kind == TokenKind.Eof)
                throw new ProtoParseException(fileName, token.Line, token.Column, "Unexpected end of input in message");

            if (token.Value == "}") break;

            switch (token.Value) {
                case "message":
                    message.NestedType.Add(ParseMessage(lexer, fileName, syntax));
                    break;

                case "enum":
                    message.EnumType.Add(ParseEnum(lexer, fileName));
                    break;

                case "oneof":
                    ParseOneof(lexer, fileName, message, syntax);
                    break;

                case "map":
                    ParseMapField(lexer, message);
                    break;

                case "repeated":
                    ParseField(lexer, fileName, message, Label.Repeated, syntax, synthetic);
                    break;

                case "optional":
                    ParseField(lexer, fileName, message, Label.Optional, syntax, synthetic, explicitOptional: true);
                    break;

                case "required":
                    ParseField(lexer, fileName, message, Label.Required, syntax, synthetic);
                    break;

                case "reserved":
                    SkipToSemicolon(lexer);
                    break;

                case "extensions":
                    ParseExtensionsRange(lexer, message);
                    break;

                case "extend":
                    ParseExtendBlock(lexer, fileName, syntax, message.Extension);
                    break;

                case "option": {
                    var features = TryParseFeatureOption(lexer);

                    if (features is not null) MergeInto(message.Options ??= new MessageOptions(), features);
                    else if (!TryParseMapEntryOption(lexer, message)) SkipOption(lexer);

                    break;
                }

                case "group":
                    ParseGroupField(lexer, fileName, message, Label.Optional, syntax);
                    break;

                case ";":
                    break;

                default:
                    ParseFieldWithType(lexer, fileName, message, Label.Optional, token, syntax, synthetic);
                    break;
            }
        }

        foreach (var field in synthetic) {
            field.OneofIndex = message.OneofDecl.Count;
            message.OneofDecl.Add(new OneofDescriptorProto { Name = $"_{field.Name}" });
        }

        return message;
    }

    static void ParseField(
        ProtoLexer lexer, string fileName, DescriptorProto message, Label label, string syntax,
        List<FieldDescriptorProto> synthetic, bool explicitOptional = false
    ) {
        var typeToken = lexer.Next();

        if (typeToken.Value == "group") {
            ParseGroupField(lexer, fileName, message, label, syntax);
            return;
        }

        ParseFieldWithType(lexer, fileName, message, label, typeToken, syntax, synthetic, explicitOptional);
    }

    static void ParseFieldWithType(
        ProtoLexer lexer, string fileName, DescriptorProto message, Label label, Token typeToken, string syntax,
        List<FieldDescriptorProto> synthetic, bool explicitOptional = false
    ) {
        var (fieldType, typeName) = ResolveFieldType(typeToken, lexer);

        var fieldName = ExpectIdent(lexer);

        Expect(lexer, "=");

        var number = ExpectInt(lexer);

        FieldOptionValues options = default;

        if (lexer.Peek().Value == "[") {
            lexer.Next();
            options = ParseFieldOptions(lexer);
        }

        Expect(lexer, ";");

        var field = new FieldDescriptorProto {
            Name   = fieldName,
            Number = number,
            Type   = fieldType,
            Label  = label
        };

        if (typeName.Length > 0) field.TypeName = typeName;
        if (options.JsonName is not null) field.JsonName = options.JsonName;
        if (options.DefaultValue is not null) field.DefaultValue = options.DefaultValue;

        ApplyFieldOptions(field, options);

        message.Field.Add(field);

        if (explicitOptional && syntax == "proto3") {
            field.Proto3Optional = true;
            synthetic.Add(field);
        }
    }

    static void ParseGroupField(ProtoLexer lexer, string fileName, DescriptorProto message, Label label, string syntax) {
        var groupName = ExpectIdent(lexer);

        Expect(lexer, "=");

        var number = ExpectInt(lexer);

        if (lexer.Peek().Value == "[") {
            lexer.Next();
            SkipUntil(lexer, "]");
        }

        Expect(lexer, "{");

        var nested    = new DescriptorProto { Name = groupName };
        var synthetic = new List<FieldDescriptorProto>();

        while (true) {
            var token = lexer.Next();

            if (token.Kind == TokenKind.Eof)
                throw new ProtoParseException(fileName, token.Line, token.Column, "Unexpected end of input in group");

            if (token.Value == "}") break;

            switch (token.Value) {
                case "message":
                    nested.NestedType.Add(ParseMessage(lexer, fileName, syntax));
                    break;

                case "enum":
                    nested.EnumType.Add(ParseEnum(lexer, fileName));
                    break;

                case "repeated":
                    ParseField(lexer, fileName, nested, Label.Repeated, syntax, synthetic);
                    break;

                case "optional":
                    ParseField(lexer, fileName, nested, Label.Optional, syntax, synthetic, explicitOptional: true);
                    break;

                case "required":
                    ParseField(lexer, fileName, nested, Label.Required, syntax, synthetic);
                    break;

                case "reserved":
                    SkipToSemicolon(lexer);
                    break;

                case "extensions":
                    ParseExtensionsRange(lexer, nested);
                    break;

                case "option":
                    SkipOption(lexer);
                    break;

                case ";":
                    break;

                default:
                    ParseFieldWithType(lexer, fileName, nested, Label.Optional, token, syntax, synthetic);
                    break;
            }
        }

        foreach (var field in synthetic) {
            field.OneofIndex = nested.OneofDecl.Count;
            nested.OneofDecl.Add(new OneofDescriptorProto { Name = $"_{field.Name}" });
        }

        message.NestedType.Add(nested);

        // The group's field is named after the group in lower case. That is the language rule,
        // not a convention: https://protobuf.dev/programming-guides/proto2/#groups
        message.Field.Add(new FieldDescriptorProto {
            Name     = groupName.ToLowerInvariant(),
            Number   = number,
            Type     = Type.Group,
            Label    = label,
            TypeName = groupName
        });
    }

    static void ParseExtendBlock(ProtoLexer lexer, string fileName, string syntax, IList<FieldDescriptorProto> extensions) {
        var extendee = ReadFullIdentOrDotPrefixed(lexer);

        Expect(lexer, "{");

        while (true) {
            var token = lexer.Next();

            if (token.Kind == TokenKind.Eof)
                throw new ProtoParseException(fileName, token.Line, token.Column, "Unexpected end of input in extend block");

            if (token.Value == "}") break;
            if (token.Value == ";") continue;

            var label = token.Value switch {
                "required" => Label.Required,
                "repeated" => Label.Repeated,
                _          => Label.Optional
            };

            var typeToken = token.Value is "optional" or "required" or "repeated" ? lexer.Next() : token;

            if (typeToken.Value == "group") {
                SkipBlock(lexer);
                continue;
            }

            var (fieldType, typeName) = ResolveFieldType(typeToken, lexer);

            var fieldName = ExpectIdent(lexer);

            Expect(lexer, "=");

            var number = ExpectInt(lexer);

            FieldOptionValues options = default;

            if (lexer.Peek().Value == "[") {
                lexer.Next();
                options = ParseFieldOptions(lexer);
            }

            Expect(lexer, ";");

            var field = new FieldDescriptorProto {
                Name     = fieldName,
                Number   = number,
                Type     = fieldType,
                Label    = label,
                Extendee = extendee
            };

            if (typeName.Length > 0) field.TypeName = typeName;
            if (options.JsonName is not null) field.JsonName = options.JsonName;
            if (options.DefaultValue is not null) field.DefaultValue = options.DefaultValue;

            ApplyFieldOptions(field, options);

            extensions.Add(field);
        }
    }

    /// <remarks>
    /// The language writes an inclusive upper bound and the descriptor stores an exclusive one, so
    /// <c>extensions 1 to 100</c> becomes <c>[1, 101)</c>.
    /// </remarks>
    static void ParseExtensionsRange(ProtoLexer lexer, DescriptorProto message) {
        const int MaxFieldNumber = 536870911;

        while (true) {
            var start = ExpectInt(lexer);
            var end   = start;

            if (lexer.Peek().Value == "to") {
                lexer.Next();

                if (lexer.Peek().Value == "max") {
                    lexer.Next();
                    end = MaxFieldNumber;
                }
                else {
                    end = ExpectInt(lexer);
                }
            }

            message.ExtensionRange.Add(new DescriptorProto.Types.ExtensionRange { Start = start, End = end + 1 });

            if (lexer.Peek().Value != ",") break;

            lexer.Next();
        }

        Expect(lexer, ";");
    }

    /// <remarks>
    /// A map field is sugar for a repeated entry message. The entry is synthesized here and flagged
    /// through <c>options.map_entry</c>, which is how the descriptor carries it — the reader then
    /// derives <c>IsMap</c> from the flag rather than from anything on this field.
    /// </remarks>
    static void ParseMapField(ProtoLexer lexer, DescriptorProto message) {
        Expect(lexer, "<");

        var (keyType, keyTypeName) = ResolveFieldType(lexer.Next(), lexer);

        Expect(lexer, ",");

        var (valueType, valueTypeName) = ResolveFieldType(lexer.Next(), lexer);

        Expect(lexer, ">");

        var fieldName = ExpectIdent(lexer);

        Expect(lexer, "=");

        var number = ExpectInt(lexer);

        if (lexer.Peek().Value == "[") {
            lexer.Next();
            SkipUntil(lexer, "]");
        }

        Expect(lexer, ";");

        var key = new FieldDescriptorProto { Name = "key", Number = 1, Type = keyType, Label = Label.Optional };
        if (keyTypeName.Length > 0) key.TypeName = keyTypeName;

        var value = new FieldDescriptorProto { Name = "value", Number = 2, Type = valueType, Label = Label.Optional };
        if (valueTypeName.Length > 0) value.TypeName = valueTypeName;

        var entryName = ToPascalCase(fieldName) + "Entry";

        message.NestedType.Add(new DescriptorProto {
            Name    = entryName,
            Options = new MessageOptions { MapEntry = true },
            Field   = { key, value }
        });

        message.Field.Add(new FieldDescriptorProto {
            Name     = fieldName,
            Number   = number,
            Type     = Type.Message,
            Label    = Label.Repeated,
            TypeName = entryName
        });
    }

    static void ParseOneof(ProtoLexer lexer, string fileName, DescriptorProto message, string syntax) {
        var oneofName = ExpectIdent(lexer);

        Expect(lexer, "{");

        var oneofIndex = message.OneofDecl.Count;

        message.OneofDecl.Add(new OneofDescriptorProto { Name = oneofName });

        while (true) {
            var token = lexer.Next();

            if (token.Kind == TokenKind.Eof)
                throw new ProtoParseException(fileName, token.Line, token.Column, "Unexpected end of input in oneof");

            if (token.Value == "}") break;
            if (token.Value == ";") continue;

            if (token.Value == "option") {
                SkipOption(lexer);
                continue;
            }

            var (fieldType, typeName) = ResolveFieldType(token, lexer);

            var fieldName = ExpectIdent(lexer);

            Expect(lexer, "=");

            var number = ExpectInt(lexer);

            FieldOptionValues options = default;

            if (lexer.Peek().Value == "[") {
                lexer.Next();
                options = ParseFieldOptions(lexer);
            }

            Expect(lexer, ";");

            var field = new FieldDescriptorProto {
                Name       = fieldName,
                Number     = number,
                Type       = fieldType,
                Label      = Label.Optional,
                OneofIndex = oneofIndex
            };

            if (typeName.Length > 0) field.TypeName = typeName;
            if (options.JsonName is not null) field.JsonName = options.JsonName;
            if (options.DefaultValue is not null) field.DefaultValue = options.DefaultValue;

            ApplyFieldOptions(field, options);

            message.Field.Add(field);
        }

        _ = syntax;
    }

    static EnumDescriptorProto ParseEnum(ProtoLexer lexer, string fileName) {
        var name = ExpectIdent(lexer);

        Expect(lexer, "{");

        var result = new EnumDescriptorProto { Name = name };

        while (true) {
            var token = lexer.Next();

            if (token.Kind == TokenKind.Eof)
                throw new ProtoParseException(fileName, token.Line, token.Column, "Unexpected end of input in enum");

            if (token.Value == "}") break;
            if (token.Value == ";") continue;

            if (token.Value == "reserved") {
                SkipToSemicolon(lexer);
                continue;
            }

            if (token.Value == "option") {
                var features = TryParseFeatureOption(lexer);

                if (features is null) SkipOption(lexer);
                else MergeInto(result.Options ??= new EnumOptions(), features);

                continue;
            }

            var valueName = token.Value;

            Expect(lexer, "=");

            var number = ExpectSignedInt(lexer);

            if (lexer.Peek().Value == "[") {
                lexer.Next();
                SkipUntil(lexer, "]");
            }

            Expect(lexer, ";");

            result.Value.Add(new EnumValueDescriptorProto { Name = valueName, Number = number });
        }

        return result;
    }

    static (Type Type, string TypeName) ResolveFieldType(Token typeToken, ProtoLexer lexer) {
        if (typeToken.Value == ".") return (Type.Message, "." + ReadFullIdent(lexer));

        if (lexer.Peek().Value == ".") {
            var name = typeToken.Value;

            while (lexer.Peek().Value == ".") {
                lexer.Next();
                name += "." + ExpectIdent(lexer);
            }

            return (Type.Message, name);
        }

        return typeToken.Value switch {
            "double"   => (Type.Double, ""),
            "float"    => (Type.Float, ""),
            "int32"    => (Type.Int32, ""),
            "int64"    => (Type.Int64, ""),
            "uint32"   => (Type.Uint32, ""),
            "uint64"   => (Type.Uint64, ""),
            "sint32"   => (Type.Sint32, ""),
            "sint64"   => (Type.Sint64, ""),
            "fixed32"  => (Type.Fixed32, ""),
            "fixed64"  => (Type.Fixed64, ""),
            "sfixed32" => (Type.Sfixed32, ""),
            "sfixed64" => (Type.Sfixed64, ""),
            "bool"     => (Type.Bool, ""),
            "string"   => (Type.String, ""),
            "bytes"    => (Type.Bytes, ""),

            // A named type is provisionally a message. One pass cannot tell an enum name from a
            // message name — the declaration may come later, or in another file — so the set
            // retypes enum-named fields once every file is parsed. The descriptor builder validates
            // this rather than inferring it, and rejects a field typed message that names an enum.
            _ => (Type.Message, typeToken.Value)
        };
    }

    readonly record struct FieldOptionValues(bool? Packed, string? JsonName, string? DefaultValue, FeatureSet? Features);

    /// <remarks>
    /// <c>packed</c> is recorded only when the source states it. Editions express repeated encoding
    /// through <c>features.repeated_field_encoding</c> instead, and writing both onto one field is
    /// rejected when the descriptor is built.
    /// </remarks>
    static FieldOptionValues ParseFieldOptions(ProtoLexer lexer) {
        bool?       packed       = null;
        string?     jsonName     = null;
        string?     defaultValue = null;
        FeatureSet? features     = null;

        while (true) {
            var token = lexer.Next();

            if (token.Value == "]") break;
            if (token.Value == ",") continue;

            switch (token.Value) {
                case "packed":
                    Expect(lexer, "=");
                    packed = lexer.Next().Value == "true";
                    break;

                case "json_name":
                    Expect(lexer, "=");
                    jsonName = ExpectString(lexer);
                    break;

                case "default":
                    Expect(lexer, "=");
                    defaultValue = ReadDefaultValue(lexer);
                    break;

                case "features": {
                    Expect(lexer, ".");

                    var featureName = ExpectIdent(lexer);

                    Expect(lexer, "=");

                    features = ApplyFeature(features, featureName, ExpectIdent(lexer));
                    break;
                }

                default: {
                    if (token.Value == "(") {
                        SkipUntil(lexer, ")");

                        if (lexer.Peek().Value == ".") {
                            lexer.Next();
                            lexer.Next();
                        }
                    }

                    if (lexer.Peek().Value == "=") {
                        lexer.Next();

                        if (lexer.Next().Value == "{") SkipBlockBody(lexer);
                    }

                    break;
                }
            }
        }

        return new FieldOptionValues(packed, jsonName, defaultValue, features);
    }

    static void ApplyFieldOptions(FieldDescriptorProto field, FieldOptionValues values) {
        if (values.Packed is null && values.Features is null) return;

        field.Options ??= new FieldOptions();

        if (values.Packed is { } packed) field.Options.Packed = packed;
        if (values.Features is { } features) MergeInto(field.Options, features);
    }

    static string ReadDefaultValue(ProtoLexer lexer) {
        var token = lexer.Next();

        if (token.Kind == TokenKind.StringLiteral) return token.Value;
        if (token.Value == "-") return "-" + lexer.Next().Value;

        return token.Value;
    }

    static FeatureSet? TryParseFeatureOption(ProtoLexer lexer) {
        if (lexer.Peek().Value != "features") return null;

        lexer.Next();

        Expect(lexer, ".");

        var featureName = ExpectIdent(lexer);

        Expect(lexer, "=");

        var featureValue = ExpectIdent(lexer);

        Expect(lexer, ";");

        return ApplyFeature(null, featureName, featureValue);
    }

    static FeatureSet ApplyFeature(FeatureSet? features, string name, string value) {
        features ??= new FeatureSet();

        switch (name) {
            case "field_presence" when Enum.TryParse<FeatureSet.Types.FieldPresence>(ToPascalCase(value), true, out var presence):
                features.FieldPresence = presence;
                break;

            case "enum_type" when Enum.TryParse<FeatureSet.Types.EnumType>(ToPascalCase(value), true, out var enumType):
                features.EnumType = enumType;
                break;

            case "repeated_field_encoding" when Enum.TryParse<FeatureSet.Types.RepeatedFieldEncoding>(ToPascalCase(value), true, out var encoding):
                features.RepeatedFieldEncoding = encoding;
                break;

            case "message_encoding" when Enum.TryParse<FeatureSet.Types.MessageEncoding>(ToPascalCase(value), true, out var messageEncoding):
                features.MessageEncoding = messageEncoding;
                break;

            case "utf8_validation" when Enum.TryParse<FeatureSet.Types.Utf8Validation>(ToPascalCase(value), true, out var utf8):
                features.Utf8Validation = utf8;
                break;

            case "json_format" when Enum.TryParse<FeatureSet.Types.JsonFormat>(ToPascalCase(value), true, out var jsonFormat):
                features.JsonFormat = jsonFormat;
                break;
        }

        return features;
    }

    static void MergeInto(FileOptions options, FeatureSet features) => Merge(options.Features ??= new FeatureSet(), features);

    static void MergeInto(MessageOptions options, FeatureSet features) => Merge(options.Features ??= new FeatureSet(), features);

    static void MergeInto(EnumOptions options, FeatureSet features) => Merge(options.Features ??= new FeatureSet(), features);

    static void MergeInto(FieldOptions options, FeatureSet features) => Merge(options.Features ??= new FeatureSet(), features);

    static void Merge(FeatureSet target, FeatureSet source) {
        if (source.HasFieldPresence) target.FieldPresence = source.FieldPresence;
        if (source.HasEnumType) target.EnumType = source.EnumType;
        if (source.HasRepeatedFieldEncoding) target.RepeatedFieldEncoding = source.RepeatedFieldEncoding;
        if (source.HasMessageEncoding) target.MessageEncoding = source.MessageEncoding;
        if (source.HasUtf8Validation) target.Utf8Validation = source.Utf8Validation;
        if (source.HasJsonFormat) target.JsonFormat = source.JsonFormat;
    }

    static bool TryParseMapEntryOption(ProtoLexer lexer, DescriptorProto message) {
        if (lexer.Peek().Value != "map_entry") return false;

        lexer.Next();

        Expect(lexer, "=");

        if (lexer.Next().Value == "true") (message.Options ??= new MessageOptions()).MapEntry = true;

        Expect(lexer, ";");

        return true;
    }

    static Edition ParseEdition(ProtoLexer lexer, string edition) =>
        Enum.TryParse<Edition>($"_{edition}", out var value) && Enum.IsDefined(value)
            ? value
            : throw new ProtoParseException(lexer.FileName, 0, 0, $"Unsupported edition '{edition}'");

    static string ReadFullIdent(ProtoLexer lexer) {
        var name = ExpectIdent(lexer);

        while (lexer.Peek().Value == ".") {
            lexer.Next();
            name += "." + ExpectIdent(lexer);
        }

        return name;
    }

    static string ReadFullIdentOrDotPrefixed(ProtoLexer lexer) {
        if (lexer.Peek().Value != ".") return ReadFullIdent(lexer);

        lexer.Next();

        return "." + ReadFullIdent(lexer);
    }

    static string ExpectIdent(ProtoLexer lexer) {
        var token = lexer.Next();

        return token.Kind == TokenKind.Identifier
            ? token.Value
            : throw new ProtoParseException(lexer.FileName, token.Line, token.Column, $"Expected identifier, got '{token.Value}'");
    }

    static string ExpectString(ProtoLexer lexer) {
        var token = lexer.Next();

        return token.Kind == TokenKind.StringLiteral
            ? token.Value
            : throw new ProtoParseException(lexer.FileName, token.Line, token.Column, $"Expected string literal, got '{token.Value}'");
    }

    static int ExpectInt(ProtoLexer lexer) {
        var token = lexer.Next();

        return token.Kind == TokenKind.IntLiteral
            ? ParseInt(token.Value)
            : throw new ProtoParseException(lexer.FileName, token.Line, token.Column, $"Expected integer, got '{token.Value}'");
    }

    static int ExpectSignedInt(ProtoLexer lexer) {
        var token    = lexer.Next();
        var negative = token.Value == "-";

        if (negative) token = lexer.Next();

        if (token.Kind != TokenKind.IntLiteral)
            throw new ProtoParseException(lexer.FileName, token.Line, token.Column, $"Expected integer, got '{token.Value}'");

        var value = ParseInt(token.Value);

        return negative ? -value : value;
    }

    static int ParseInt(string value) =>
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(value, 16) : int.Parse(value);

    static void Expect(ProtoLexer lexer, string expected) {
        var token = lexer.Next();

        if (token.Value != expected)
            throw new ProtoParseException(lexer.FileName, token.Line, token.Column, $"Expected '{expected}', got '{token.Value}'");
    }

    static void SkipOption(ProtoLexer lexer) => SkipToSemicolon(lexer);

    static void SkipToSemicolon(ProtoLexer lexer) {
        while (true) {
            var token = lexer.Next();

            if (token.Kind == TokenKind.Eof || token.Value == ";") return;
            if (token.Value == "{") SkipBlockBody(lexer);
        }
    }

    static void SkipBlock(ProtoLexer lexer) {
        while (true) {
            var token = lexer.Next();

            if (token.Kind == TokenKind.Eof) return;
            if (token.Value == "{") break;
        }

        SkipBlockBody(lexer);
    }

    static void SkipBlockBody(ProtoLexer lexer) {
        var depth = 1;

        while (depth > 0) {
            var token = lexer.Next();

            if (token.Kind == TokenKind.Eof) return;
            if (token.Value == "{") depth++;
            else if (token.Value == "}") depth--;
        }
    }

    static void SkipUntil(ProtoLexer lexer, string terminator) {
        var depth = 0;

        while (true) {
            var token = lexer.Next();

            if (token.Kind == TokenKind.Eof) return;

            if (token.Value == "(") depth++;
            else if (token.Value == ")" && depth > 0) depth--;

            if (depth == 0 && token.Value == terminator) return;
        }
    }

    static string ToPascalCase(string value) {
        var text       = new StringBuilder(value.Length);
        var capitalize = true;

        foreach (var c in value) {
            if (c == '_') {
                capitalize = true;
            }
            else if (capitalize) {
                text.Append(char.ToUpperInvariant(c));
                capitalize = false;
            }
            else {
                text.Append(c);
            }
        }

        return text.ToString();
    }
}
