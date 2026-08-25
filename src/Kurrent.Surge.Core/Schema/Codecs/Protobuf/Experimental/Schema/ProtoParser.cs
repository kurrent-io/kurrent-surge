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
        var lexer     = new ProtoLexer(fileName, content);
        var file      = new FileDescriptorProto { Name = fileName };
        var locations = new ProtoSourceLocations();

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

                    if (features is not null) MergeInto(file.Options ??= new FileOptions(), features);
                    else if (!TryParseFileOption(lexer, file)) SkipOption(lexer);

                    break;
                }

                // 4 and 5 are message_type and enum_type on FileDescriptorProto.
                case "message":
                    locations.Enter(4, file.MessageType.Count);
                    file.MessageType.Add(ParseMessage(lexer, file.Syntax, locations, token));
                    locations.Exit();
                    break;

                case "enum":
                    locations.Enter(5, file.EnumType.Count);
                    file.EnumType.Add(ParseEnum(lexer, locations, token));
                    locations.Exit();
                    break;

                // Services are parsed only so the tokens are consumed. A transcoder decodes
                // messages, and a service declares none.
                case "service":
                    SkipBlock(lexer);
                    break;

                case "extend":
                    ParseExtendBlock(lexer, file.Extension);
                    break;
            }
        }

        file.SourceCodeInfo = locations.ToSourceCodeInfo();

        return file;
    }

    static DescriptorProto ParseMessage(ProtoLexer lexer, string syntax, ProtoSourceLocations locations, Token start) {
        var name = ExpectIdent(lexer);

        Expect(lexer, "{");

        var message = ParseMessageBody(lexer, syntax, new DescriptorProto { Name = name }, locations);

        // The body has just consumed its closing brace, so that is where the declaration ends.
        locations.Record(start, lexer.LastToken, lexer.TakeTrailingComment(lexer.LastToken.EndLine));

        return message;
    }

    /// <summary>Reads a message body, from just past its opening brace to its closing one.</summary>
    /// <remarks>
    /// A group declares a message and a field at once, and its body obeys the same grammar as any
    /// other message — the language says so. Sharing the reader is what makes that true here rather
    /// than nearly true: the group form used to accept nine of these constructs, so a oneof, a map,
    /// an extend block or a nested group inside a group was silently mis-parsed.
    /// </remarks>
    static DescriptorProto ParseMessageBody(ProtoLexer lexer, string syntax, DescriptorProto message, ProtoSourceLocations locations) {
        // A proto3 `optional` field is carried by a oneof holding only that field. Those synthetic
        // oneofs must follow every declared one, so they are appended once the body is closed.
        var synthetic = new List<FieldDescriptorProto>();

        while (true) {
            var token = lexer.Next();

            if (token.Kind == TokenKind.Eof)
                throw new ProtoParseException(lexer.FileName, token.Line, token.Column, "Unexpected end of input in message");

            if (token.Value == "}") break;

            switch (token.Value) {
                // 3, 4 and 8 are nested_type, enum_type and oneof_decl on DescriptorProto.
                case "message":
                    locations.Enter(3, message.NestedType.Count);
                    message.NestedType.Add(ParseMessage(lexer, syntax, locations, token));
                    locations.Exit();
                    break;

                case "enum":
                    locations.Enter(4, message.EnumType.Count);
                    message.EnumType.Add(ParseEnum(lexer, locations, token));
                    locations.Exit();
                    break;

                case "oneof":
                    ParseOneOf(lexer, message, locations, token);
                    break;

                case "map":
                    ParseMapField(lexer, message, locations, token);
                    break;

                case "repeated":
                    ParseField(lexer, message, Label.Repeated, syntax, synthetic, locations, token);
                    break;

                case "optional":
                    ParseField(lexer, message, Label.Optional, syntax, synthetic, locations, token, explicitOptional: true);
                    break;

                case "required":
                    ParseField(lexer, message, Label.Required, syntax, synthetic, locations, token);
                    break;

                case "reserved":
                    ParseReserved(lexer, message);
                    break;

                case "extensions":
                    ParseExtensionsRange(lexer, message);
                    break;

                case "extend":
                    ParseExtendBlock(lexer, message.Extension);
                    break;

                case "option": {
                    var features = TryParseFeatureOption(lexer);

                    if (features is not null) MergeInto(message.Options ??= new MessageOptions(), features);
                    else if (TryParseBoolOption(lexer, "map_entry", out var mapEntry)) (message.Options ??= new MessageOptions()).MapEntry = mapEntry;
                    else if (TryParseBoolOption(lexer, "deprecated", out var deprecated)) (message.Options ??= new MessageOptions()).Deprecated = deprecated;
                    else SkipOption(lexer);

                    break;
                }

                case "group":
                    ParseGroupField(lexer, message, Label.Optional, syntax, locations, token);
                    break;

                case ";":
                    break;

                default:
                    ParseFieldWithType(lexer, message, Label.Optional, token, syntax, synthetic, locations, token);
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
        ProtoLexer lexer, DescriptorProto message, Label label, string syntax,
        List<FieldDescriptorProto> synthetic, ProtoSourceLocations locations, Token start, bool explicitOptional = false
    ) {
        var typeToken = lexer.Next();

        if (typeToken.Value == "group") {
            ParseGroupField(lexer, message, label, syntax, locations, start);
            return;
        }

        ParseFieldWithType(lexer, message, label, typeToken, syntax, synthetic, locations, start, explicitOptional);
    }

    static void ParseFieldWithType(
        ProtoLexer lexer, DescriptorProto message, Label label, Token typeToken, string syntax,
        List<FieldDescriptorProto> synthetic, ProtoSourceLocations locations, Token start, bool explicitOptional = false
    ) {
        var (fieldType, typeName) = ResolveFieldType(typeToken, lexer);

        var fieldName = ExpectIdent(lexer);

        Expect(lexer, "=");

        var number = ExpectInt(lexer);

        var field = new FieldDescriptorProto {
            Name   = fieldName,
            Number = number,
            Type   = fieldType,
            Label  = label
        };

        // 2 is DescriptorProto.field. Entered before the index is taken, because the field is only
        // appended once its terminator is read.
        locations.Enter(2, message.Field.Count);

        CompleteField(lexer, field, typeName);

        locations.Record(start, lexer.LastToken, lexer.TakeTrailingComment(lexer.LastToken.EndLine));
        locations.Exit();

        message.Field.Add(field);

        if (explicitOptional && syntax == "proto3") {
            field.Proto3Optional = true;
            synthetic.Add(field);
        }
    }

    static void ParseGroupField(ProtoLexer lexer, DescriptorProto message, Label label, string syntax, ProtoSourceLocations locations, Token start) {
        var groupName = ExpectIdent(lexer);

        Expect(lexer, "=");

        var number = ExpectInt(lexer);

        if (lexer.Peek().Value == "[") {
            lexer.Next();
            SkipUntil(lexer, "]");
        }

        Expect(lexer, "{");

        locations.Enter(3, message.NestedType.Count);

        message.NestedType.Add(ParseMessageBody(lexer, syntax, new DescriptorProto { Name = groupName }, locations));

        locations.Record(start, lexer.LastToken, lexer.TakeTrailingComment(lexer.LastToken.EndLine));
        locations.Exit();

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

    static void ParseExtendBlock(ProtoLexer lexer, IList<FieldDescriptorProto> extensions) {
        var extendee = ReadFullIdentOrDotPrefixed(lexer);

        Expect(lexer, "{");

        while (true) {
            var token = lexer.Next();

            if (token.Kind == TokenKind.Eof)
                throw new ProtoParseException(lexer.FileName, token.Line, token.Column, "Unexpected end of input in extend block");

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

            var field = new FieldDescriptorProto {
                Name     = fieldName,
                Number   = number,
                Type     = fieldType,
                Label    = label,
                Extendee = extendee
            };

            CompleteField(lexer, field, typeName);

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
    /// A statement carries either numbers or names and never both, so the first token decides which.
    /// <c>ReservedRange.End</c> is exclusive, matching <c>ExtensionRange</c> above and unlike the enum
    /// form below. See https://protobuf.dev/reference/protobuf/proto3-spec/#reserved.
    /// </remarks>
    static void ParseReserved(ProtoLexer lexer, DescriptorProto message) {
        const int MaxFieldNumber = 536870911;

        if (lexer.Peek().Kind == TokenKind.StringLiteral) {
            ReadReservedNames(lexer, message.ReservedName);
            return;
        }

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

            message.ReservedRange.Add(new DescriptorProto.Types.ReservedRange { Start = start, End = end + 1 });

            if (lexer.Peek().Value != ",") break;

            lexer.Next();
        }

        Expect(lexer, ";");
    }

    /// <remarks>
    /// <c>EnumReservedRange.End</c> is inclusive where the message form's is exclusive. An enum value
    /// reaches int32 max, so an exclusive end could not name the last one. The numbers are signed for
    /// the same reason — an enum value may be negative, a field number may not.
    /// </remarks>
    static void ParseReserved(ProtoLexer lexer, EnumDescriptorProto @enum) {
        if (lexer.Peek().Kind == TokenKind.StringLiteral) {
            ReadReservedNames(lexer, @enum.ReservedName);
            return;
        }

        while (true) {
            var start = ExpectSignedInt(lexer);
            var end   = start;

            if (lexer.Peek().Value == "to") {
                lexer.Next();

                if (lexer.Peek().Value == "max") {
                    lexer.Next();
                    end = int.MaxValue;
                }
                else {
                    end = ExpectSignedInt(lexer);
                }
            }

            @enum.ReservedRange.Add(new EnumDescriptorProto.Types.EnumReservedRange { Start = start, End = end });

            if (lexer.Peek().Value != ",") break;

            lexer.Next();
        }

        Expect(lexer, ";");
    }

    static void ReadReservedNames(ProtoLexer lexer, ICollection<string> names) {
        while (true) {
            names.Add(ExpectString(lexer));

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
    static void ParseMapField(ProtoLexer lexer, DescriptorProto message, ProtoSourceLocations locations, Token start) {
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

        // The comment belongs to the map field the source declares, not to the entry message the
        // language synthesizes beside it.
        locations.Enter(2, message.Field.Count);
        locations.Record(start, lexer.LastToken, lexer.TakeTrailingComment(lexer.LastToken.EndLine));
        locations.Exit();

        message.Field.Add(new FieldDescriptorProto {
            Name     = fieldName,
            Number   = number,
            Type     = Type.Message,
            Label    = Label.Repeated,
            TypeName = entryName
        });
    }

    static void ParseOneOf(ProtoLexer lexer, DescriptorProto message, ProtoSourceLocations locations, Token start) {
        var oneOfName = ExpectIdent(lexer);

        Expect(lexer, "{");

        var oneofIndex = message.OneofDecl.Count;

        message.OneofDecl.Add(new OneofDescriptorProto { Name = oneOfName });

        while (true) {
            var token = lexer.Next();

            if (token.Kind == TokenKind.Eof)
                throw new ProtoParseException(lexer.FileName, token.Line, token.Column, "Unexpected end of input in oneof");

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

            var field = new FieldDescriptorProto {
                Name       = fieldName,
                Number     = number,
                Type       = fieldType,
                Label      = Label.Optional,
                OneofIndex = oneofIndex
            };

            locations.Enter(2, message.Field.Count);

            CompleteField(lexer, field, typeName);

            locations.Record(token, lexer.LastToken, lexer.TakeTrailingComment(lexer.LastToken.EndLine));
            locations.Exit();

            message.Field.Add(field);
        }

        // 8 is DescriptorProto.oneof_decl. Recorded after the body, so a comment on the oneof itself
        // is not mistaken for one on its first member.
        locations.Enter(8, oneofIndex);
        locations.Record(start, lexer.LastToken, lexer.TakeTrailingComment(lexer.LastToken.EndLine));
        locations.Exit();
    }

    static EnumDescriptorProto ParseEnum(ProtoLexer lexer, ProtoSourceLocations locations, Token start) {
        var name = ExpectIdent(lexer);

        Expect(lexer, "{");

        var result = new EnumDescriptorProto { Name = name };

        while (true) {
            var token = lexer.Next();

            if (token.Kind == TokenKind.Eof)
                throw new ProtoParseException(lexer.FileName, token.Line, token.Column, "Unexpected end of input in enum");

            if (token.Value == "}") break;
            if (token.Value == ";") continue;

            if (token.Value == "reserved") {
                ParseReserved(lexer, result);
                continue;
            }

            if (token.Value == "option") {
                var features = TryParseFeatureOption(lexer);

                if (features is not null) MergeInto(result.Options ??= new EnumOptions(), features);
                else if (TryParseBoolOption(lexer, "allow_alias", out var allowAlias)) (result.Options ??= new EnumOptions()).AllowAlias = allowAlias;
                else if (TryParseBoolOption(lexer, "deprecated", out var deprecated)) (result.Options ??= new EnumOptions()).Deprecated = deprecated;
                else SkipOption(lexer);

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

            // 2 is EnumDescriptorProto.value.
            locations.Enter(2, result.Value.Count);
            locations.Record(token, lexer.LastToken, lexer.TakeTrailingComment(lexer.LastToken.EndLine));
            locations.Exit();

            result.Value.Add(new EnumValueDescriptorProto { Name = valueName, Number = number });
        }

        locations.Record(start, lexer.LastToken, lexer.TakeTrailingComment(lexer.LastToken.EndLine));

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

    readonly record struct FieldOptionValues(bool? Packed, string? JsonName, string? DefaultValue, bool? Deprecated, FeatureSet? Features);

    /// <summary>Reads a field's optional <c>[…]</c> block and its terminator, then applies both.</summary>
    /// <remarks>
    /// Every field ends the same way whatever declared it — a plain field, an extension, a oneof
    /// member. Only the object above differs, so the tail is written once.
    /// </remarks>
    static void CompleteField(ProtoLexer lexer, FieldDescriptorProto field, string typeName) {
        FieldOptionValues options = default;

        if (lexer.Peek().Value == "[") {
            lexer.Next();

            options = ParseFieldOptions(lexer);
        }

        Expect(lexer, ";");

        if (typeName.Length > 0) field.TypeName = typeName;
        if (options.JsonName is not null) field.JsonName = options.JsonName;
        if (options.DefaultValue is not null) field.DefaultValue = options.DefaultValue;

        ApplyFieldOptions(field, options);
    }

    /// <remarks>
    /// <c>packed</c> is recorded only when the source states it. Editions express repeated encoding
    /// through <c>features.repeated_field_encoding</c> instead, and writing both onto one field is
    /// rejected when the descriptor is built.
    /// </remarks>
    static FieldOptionValues ParseFieldOptions(ProtoLexer lexer) {
        bool?       packed       = null;
        string?     jsonName     = null;
        string?     defaultValue = null;
        bool?       deprecated   = null;
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

                case "deprecated":
                    Expect(lexer, "=");
                    deprecated = lexer.Next().Value == "true";
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

        return new FieldOptionValues(packed, jsonName, defaultValue, deprecated, features);
    }

    static void ApplyFieldOptions(FieldDescriptorProto field, FieldOptionValues values) {
        if (values.Packed is null && values.Deprecated is null && values.Features is null) return;

        field.Options ??= new FieldOptions();

        if (values.Packed is { } packed) field.Options.Packed = packed;
        if (values.Deprecated is { } deprecated) field.Options.Deprecated = deprecated;
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

    /// <remarks>
    /// Consumes <c>name = true|false ;</c> when the next token names the option, and leaves the lexer
    /// where it was otherwise, so a caller can try the next name before giving up and skipping.
    /// </remarks>
    static bool TryParseBoolOption(ProtoLexer lexer, string name, out bool value) {
        value = false;

        if (lexer.Peek().Value != name) return false;

        lexer.Next();

        Expect(lexer, "=");

        value = lexer.Next().Value == "true";

        Expect(lexer, ";");

        return true;
    }

    /// <remarks>
    /// The generated-code options. None reaches the wire or the JSON mapping, so nothing downstream
    /// reads them — but a file that drops them cannot be written back out as the one that was read.
    /// A name not listed here is skipped, which today is only the extension options a service declares.
    /// </remarks>
    static bool TryParseFileOption(ProtoLexer lexer, FileDescriptorProto file) {
        var name = lexer.Peek().Value;

        if (name is not ("java_package" or "java_outer_classname" or "java_multiple_files" or "go_package"
                      or "csharp_namespace" or "objc_class_prefix" or "cc_enable_arenas" or "deprecated"))
            return false;

        lexer.Next();

        Expect(lexer, "=");

        var value = lexer.Next().Value;

        Expect(lexer, ";");

        var options = file.Options ??= new FileOptions();

        switch (name) {
            case "java_package":         options.JavaPackage       = value; break;
            case "java_outer_classname": options.JavaOuterClassname = value; break;
            case "go_package":           options.GoPackage         = value; break;
            case "csharp_namespace":     options.CsharpNamespace   = value; break;
            case "objc_class_prefix":    options.ObjcClassPrefix   = value; break;

            case "java_multiple_files": options.JavaMultipleFiles = value == "true"; break;
            case "cc_enable_arenas":    options.CcEnableArenas    = value == "true"; break;
            case "deprecated":          options.Deprecated        = value == "true"; break;
        }

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
