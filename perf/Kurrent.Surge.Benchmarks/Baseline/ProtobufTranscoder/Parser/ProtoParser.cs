using ProtobufTranscoder.Descriptors;

namespace ProtobufTranscoder.Parser;

/// <summary>
/// Hand-written recursive descent parser for .proto files.
/// Supports proto2, proto3, and editions 2024.
/// </summary>
static class ProtoParser
{
    public static FileDescriptorProto Parse(string fileName, string content)
    {
        var lexer = new ProtoLexer(fileName, content);
        var file = new FileDescriptorProto { Name = fileName };

        while (true)
        {
            var token = lexer.Next();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Value)
            {
                case "syntax":
                    Expect(lexer, "=");
                    file.Syntax = ExpectString(lexer);
                    Expect(lexer, ";");
                    break;

                case "edition":
                    Expect(lexer, "=");
                    file.Edition = ExpectString(lexer);
                    file.Syntax = "editions";
                    Expect(lexer, ";");
                    break;

                case "package":
                    file.Package = ReadFullIdent(lexer);
                    Expect(lexer, ";");
                    break;

                case "import":
                    var peek = lexer.Peek();
                    if (peek.Value is "weak" or "public")
                        lexer.Next();
                    var importPath = ExpectString(lexer);
                    Expect(lexer, ";");
                    file.Imports.Add(importPath);
                    break;

                case "option":
                    var fileFeatures = TryParseFeatureOption(lexer);
                    if (fileFeatures is not null)
                        file.Features = MergeOverrides(file.Features, fileFeatures);
                    else
                        SkipOption(lexer);
                    break;

                case "message":
                    file.Messages.Add(ParseMessage(lexer, fileName, file.Syntax));
                    break;

                case "enum":
                    file.Enums.Add(ParseEnum(lexer, fileName));
                    break;

                case "service":
                    SkipBlock(lexer);
                    break;

                case "extend":
                    file.ExtendBlocks.Add(ParseExtendBlock(lexer, fileName, file.Syntax));
                    break;

                default:
                    break;
            }
        }

        return file;
    }

    static MessageDescriptorProto ParseMessage(ProtoLexer lexer, string fileName, string syntax)
    {
        var name = ExpectIdent(lexer);
        Expect(lexer, "{");

        var msg = new MessageDescriptorProto { Name = name };

        while (true)
        {
            var token = lexer.Next();
            if (token.Kind == TokenKind.Eof)
                throw new ProtoParseException(fileName, token.Line, token.Column, "Unexpected EOF in message");
            if (token.Value == "}")
                break;

            switch (token.Value)
            {
                case "message":
                    msg.NestedMessages.Add(ParseMessage(lexer, fileName, syntax));
                    break;

                case "enum":
                    msg.Enums.Add(ParseEnum(lexer, fileName));
                    break;

                case "oneof":
                    ParseOneof(lexer, fileName, msg, syntax);
                    break;

                case "map":
                    ParseMapField(lexer, fileName, msg, syntax);
                    break;

                case "repeated":
                    ParseField(lexer, fileName, msg, FieldLabel.Repeated, syntax);
                    break;

                case "optional":
                    ParseField(lexer, fileName, msg, FieldLabel.Optional, syntax);
                    break;

                case "required":
                    ParseField(lexer, fileName, msg, FieldLabel.Required, syntax);
                    break;

                case "reserved":
                    SkipToSemicolon(lexer);
                    break;

                case "extensions":
                    ParseExtensionsRange(lexer, msg);
                    break;

                case "extend":
                    msg.ExtendBlocks.Add(ParseExtendBlock(lexer, fileName, syntax));
                    break;

                case "option":
                    var msgFeatures = TryParseFeatureOption(lexer);
                    if (msgFeatures is not null)
                        msg.Features = MergeOverrides(msg.Features, msgFeatures);
                    else if (TryParseMessageOption(lexer, msg))
                        break;
                    else
                        SkipOption(lexer);
                    break;

                case ";":
                    break;

                case "group":
                    ParseGroupField(lexer, fileName, msg, FieldLabel.Optional, syntax);
                    break;

                default:
                    ParseFieldWithType(lexer, fileName, msg, FieldLabel.Optional, token, syntax);
                    break;
            }
        }

        return msg;
    }

    static void ParseField(ProtoLexer lexer, string fileName, MessageDescriptorProto msg, FieldLabel label, string syntax)
    {
        var typeToken = lexer.Next();

        // Handle group declarations: optional group MyGroup = 1 { ... }
        if (typeToken.Value == "group")
        {
            ParseGroupField(lexer, fileName, msg, label, syntax);
            return;
        }

        ParseFieldWithType(lexer, fileName, msg, label, typeToken, syntax);
    }

    static void ParseFieldWithType(ProtoLexer lexer, string fileName, MessageDescriptorProto msg, FieldLabel label, Token typeToken, string syntax)
    {
        var (fieldType, typeName) = ResolveFieldType(typeToken, lexer);

        var fieldName = ExpectIdent(lexer);
        Expect(lexer, "=");
        var number = ExpectInt(lexer);

        var packed = GetDefaultPacked(label, fieldType, syntax);
        string? jsonName = null;
        string? defaultValue = null;
        FeatureSetOverrides? fieldFeatures = null;

        var next = lexer.Peek();
        if (next.Value == "[")
        {
            lexer.Next();
            (packed, jsonName, defaultValue, fieldFeatures) = ParseFieldOptions(lexer, packed, fieldType, syntax);
        }

        Expect(lexer, ";");

        msg.Fields.Add(new FieldDescriptorProto
        {
            Name = fieldName,
            JsonName = jsonName,
            Number = number,
            Type = fieldType,
            Label = label,
            TypeName = typeName,
            IsPacked = packed,
            DefaultValue = defaultValue,
            Features = fieldFeatures,
        });
    }

    static void ParseGroupField(ProtoLexer lexer, string fileName, MessageDescriptorProto msg, FieldLabel label, string syntax)
    {
        var groupName = ExpectIdent(lexer);
        Expect(lexer, "=");
        var number = ExpectInt(lexer);

        // Check for field options before the block
        var peek = lexer.Peek();
        if (peek.Value == "[")
        {
            lexer.Next();
            SkipUntil(lexer, "]");
        }

        Expect(lexer, "{");

        // Parse the group body as a nested message
        var nestedMsg = new MessageDescriptorProto { Name = groupName };

        while (true)
        {
            var token = lexer.Next();
            if (token.Kind == TokenKind.Eof)
                throw new ProtoParseException(fileName, token.Line, token.Column, "Unexpected EOF in group");
            if (token.Value == "}")
                break;

            switch (token.Value)
            {
                case "message":
                    nestedMsg.NestedMessages.Add(ParseMessage(lexer, fileName, syntax));
                    break;
                case "enum":
                    nestedMsg.Enums.Add(ParseEnum(lexer, fileName));
                    break;
                case "repeated":
                    ParseField(lexer, fileName, nestedMsg, FieldLabel.Repeated, syntax);
                    break;
                case "optional":
                    ParseField(lexer, fileName, nestedMsg, FieldLabel.Optional, syntax);
                    break;
                case "required":
                    ParseField(lexer, fileName, nestedMsg, FieldLabel.Required, syntax);
                    break;
                case "reserved":
                    SkipToSemicolon(lexer);
                    break;
                case "extensions":
                    ParseExtensionsRange(lexer, nestedMsg);
                    break;
                case "option":
                    SkipOption(lexer);
                    break;
                case ";":
                    break;
                default:
                    ParseFieldWithType(lexer, fileName, nestedMsg, FieldLabel.Optional, token, syntax);
                    break;
            }
        }

        msg.NestedMessages.Add(nestedMsg);

        // Add a field referencing the group message type (lowercase name per proto convention)
        var fieldName = groupName.ToLowerInvariant();
        msg.Fields.Add(new FieldDescriptorProto
        {
            Name = fieldName,
            Number = number,
            Type = FieldType.Group,
            Label = label,
            TypeName = groupName,
            IsGroupEncoded = true,
        });
    }

    static ExtendBlockProto ParseExtendBlock(ProtoLexer lexer, string fileName, string syntax)
    {
        var extendeeName = ReadFullIdentOrDotPrefixed(lexer);
        Expect(lexer, "{");

        var block = new ExtendBlockProto { ExtendeeTypeName = extendeeName };

        while (true)
        {
            var token = lexer.Next();
            if (token.Kind == TokenKind.Eof)
                throw new ProtoParseException(fileName, token.Line, token.Column, "Unexpected EOF in extend block");
            if (token.Value == "}")
                break;
            if (token.Value == ";")
                continue;

            var label = token.Value switch
            {
                "required" => FieldLabel.Required,
                "repeated" => FieldLabel.Repeated,
                _ => FieldLabel.Optional,
            };
            var typeToken = token.Value is "optional" or "required" or "repeated"
                ? lexer.Next()
                : token;

            if (typeToken.Value == "group")
            {
                // Group in extend block — skip for now (very rare)
                SkipBlock(lexer);
                continue;
            }

            var (fieldType, typeName) = ResolveFieldType(typeToken, lexer);
            var fieldName = ExpectIdent(lexer);
            Expect(lexer, "=");
            var number = ExpectInt(lexer);

            var packed = GetDefaultPacked(label, fieldType, syntax);
            string? jsonName = null;
            string? defaultValue = null;

            var next = lexer.Peek();
            if (next.Value == "[")
            {
                lexer.Next();
                (packed, jsonName, defaultValue, _) = ParseFieldOptions(lexer, packed, fieldType, syntax);
            }

            Expect(lexer, ";");

            block.Fields.Add(new FieldDescriptorProto
            {
                Name = fieldName,
                JsonName = jsonName,
                Number = number,
                Type = fieldType,
                Label = label,
                TypeName = typeName,
                IsPacked = packed,
                DefaultValue = defaultValue,
            });
        }

        return block;
    }

    static void ParseExtensionsRange(ProtoLexer lexer, MessageDescriptorProto msg)
    {
        while (true)
        {
            var start = ExpectInt(lexer);
            var end = start;

            var next = lexer.Peek();
            if (next.Value == "to")
            {
                lexer.Next();
                var endToken = lexer.Peek();
                if (endToken.Value == "max")
                {
                    lexer.Next();
                    end = 536870911; // 2^29 - 1
                }
                else
                {
                    end = ExpectInt(lexer);
                }
            }

            msg.ExtensionRanges.Add(new ExtensionRangeProto { Start = start, End = end });

            next = lexer.Peek();
            if (next.Value == ",")
            {
                lexer.Next();
                continue;
            }

            break;
        }

        Expect(lexer, ";");
    }

    static void ParseMapField(ProtoLexer lexer, string fileName, MessageDescriptorProto msg, string syntax)
    {
        Expect(lexer, "<");
        var keyTypeToken = lexer.Next();
        var (keyType, keyTypeName) = ResolveFieldType(keyTypeToken, lexer);
        Expect(lexer, ",");
        var valueTypeToken = lexer.Next();
        var (valueType, valueTypeName) = ResolveFieldType(valueTypeToken, lexer);
        Expect(lexer, ">");

        var fieldName = ExpectIdent(lexer);
        Expect(lexer, "=");
        var number = ExpectInt(lexer);

        var next = lexer.Peek();
        if (next.Value == "[")
        {
            lexer.Next();
            SkipUntil(lexer, "]");
        }

        Expect(lexer, ";");

        var entryName = ToPascalCase(fieldName) + "Entry";
        var entryMsg = new MessageDescriptorProto { Name = entryName, IsMapEntry = true };
        entryMsg.Fields.Add(new FieldDescriptorProto
        {
            Name = "key",
            Number = 1,
            Type = keyType,
            Label = FieldLabel.Optional,
            TypeName = keyTypeName,
        });
        entryMsg.Fields.Add(new FieldDescriptorProto
        {
            Name = "value",
            Number = 2,
            Type = valueType,
            Label = FieldLabel.Optional,
            TypeName = valueTypeName,
        });
        msg.NestedMessages.Add(entryMsg);

        msg.Fields.Add(new FieldDescriptorProto
        {
            Name = fieldName,
            Number = number,
            Type = FieldType.Message,
            Label = FieldLabel.Repeated,
            TypeName = entryName,
            IsMap = true,
        });
    }

    static void ParseOneof(ProtoLexer lexer, string fileName, MessageDescriptorProto msg, string syntax)
    {
        var oneofName = ExpectIdent(lexer);
        Expect(lexer, "{");

        var oneofIndex = msg.Oneofs.Count;
        msg.Oneofs.Add(oneofName);

        while (true)
        {
            var token = lexer.Next();
            if (token.Kind == TokenKind.Eof)
                throw new ProtoParseException(fileName, token.Line, token.Column, "Unexpected EOF in oneof");
            if (token.Value == "}")
                break;
            if (token.Value == ";")
                continue;
            if (token.Value == "option")
            {
                SkipOption(lexer);
                continue;
            }

            var (fieldType, typeName) = ResolveFieldType(token, lexer);
            var fieldName = ExpectIdent(lexer);
            Expect(lexer, "=");
            var number = ExpectInt(lexer);

            string? jsonName = null;
            string? defaultValue = null;
            var next = lexer.Peek();
            if (next.Value == "[")
            {
                lexer.Next();
                (_, jsonName, defaultValue, _) = ParseFieldOptions(lexer, false, fieldType, syntax);
            }

            Expect(lexer, ";");

            msg.Fields.Add(new FieldDescriptorProto
            {
                Name = fieldName,
                Number = number,
                Type = fieldType,
                Label = FieldLabel.Optional,
                TypeName = typeName,
                OneofIndex = oneofIndex,
                DefaultValue = defaultValue,
            });
        }
    }

    static EnumDescriptorProto ParseEnum(ProtoLexer lexer, string fileName)
    {
        var name = ExpectIdent(lexer);
        Expect(lexer, "{");

        var enumProto = new EnumDescriptorProto { Name = name };

        while (true)
        {
            var token = lexer.Next();
            if (token.Kind == TokenKind.Eof)
                throw new ProtoParseException(fileName, token.Line, token.Column, "Unexpected EOF in enum");
            if (token.Value == "}")
                break;
            if (token.Value == "reserved")
            {
                SkipToSemicolon(lexer);
                continue;
            }
            if (token.Value == "option")
            {
                var enumFeatures = TryParseFeatureOption(lexer);
                if (enumFeatures is not null)
                    enumProto.Features = MergeOverrides(enumProto.Features, enumFeatures);
                else
                    SkipOption(lexer);
                continue;
            }
            if (token.Value == ";")
                continue;

            var valueName = token.Value;
            Expect(lexer, "=");
            var number = ExpectSignedInt(lexer);

            var next = lexer.Peek();
            if (next.Value == "[")
            {
                lexer.Next();
                SkipUntil(lexer, "]");
            }

            Expect(lexer, ";");

            enumProto.Values.Add(new EnumValueProto { Name = valueName, Number = number });
        }

        return enumProto;
    }

    static (FieldType type, string typeName) ResolveFieldType(Token typeToken, ProtoLexer lexer)
    {
        // Handle fully-qualified names starting with '.'
        if (typeToken.Value == ".")
        {
            var name = "." + ReadFullIdent(lexer);
            return (FieldType.Message, name);
        }

        var peek = lexer.Peek();
        if (peek.Value == ".")
        {
            var name = typeToken.Value;
            while (lexer.Peek().Value == ".")
            {
                lexer.Next();
                name += "." + ExpectIdent(lexer);
            }
            return (FieldType.Message, name);
        }

        return typeToken.Value switch
        {
            "double" => (FieldType.Double, ""),
            "float" => (FieldType.Float, ""),
            "int32" => (FieldType.Int32, ""),
            "int64" => (FieldType.Int64, ""),
            "uint32" => (FieldType.UInt32, ""),
            "uint64" => (FieldType.UInt64, ""),
            "sint32" => (FieldType.SInt32, ""),
            "sint64" => (FieldType.SInt64, ""),
            "fixed32" => (FieldType.Fixed32, ""),
            "fixed64" => (FieldType.Fixed64, ""),
            "sfixed32" => (FieldType.SFixed32, ""),
            "sfixed64" => (FieldType.SFixed64, ""),
            "bool" => (FieldType.Bool, ""),
            "string" => (FieldType.String, ""),
            "bytes" => (FieldType.Bytes, ""),
            "group" => (FieldType.Group, typeToken.Value),
            _ => (FieldType.Message, typeToken.Value),
        };
    }

    static (bool packed, string? jsonName, string? defaultValue, FeatureSetOverrides? features)
        ParseFieldOptions(ProtoLexer lexer, bool defaultPacked, FieldType fieldType, string syntax)
    {
        var packed = defaultPacked;
        string? jsonName = null;
        string? defaultValue = null;
        FeatureSetOverrides? features = null;

        while (true)
        {
            var token = lexer.Next();
            if (token.Value == "]") break;
            if (token.Value == ",") continue;

            if (token.Value == "packed")
            {
                Expect(lexer, "=");
                var val = lexer.Next();
                packed = val.Value == "true";
            }
            else if (token.Value == "json_name")
            {
                Expect(lexer, "=");
                jsonName = ExpectString(lexer);
            }
            else if (token.Value == "default")
            {
                Expect(lexer, "=");
                defaultValue = ReadDefaultValue(lexer);
            }
            else if (token.Value == "features")
            {
                Expect(lexer, ".");
                var featureName = ExpectIdent(lexer);
                Expect(lexer, "=");
                var featureValue = ExpectIdent(lexer);
                features = ApplyFeatureOverride(features, featureName, featureValue);
            }
            else
            {
                // Skip unknown option: name = value
                if (token.Value == "(")
                {
                    SkipUntil(lexer, ")");
                    token = lexer.Peek();
                    if (token.Value == ".")
                    {
                        lexer.Next();
                        lexer.Next();
                    }
                }
                var eq = lexer.Peek();
                if (eq.Value == "=")
                {
                    lexer.Next();
                    var val = lexer.Next();
                    if (val.Value == "{")
                        SkipBlockBody(lexer);
                }
            }
        }

        return (packed, jsonName, defaultValue, features);
    }

    static string ReadDefaultValue(ProtoLexer lexer)
    {
        var token = lexer.Next();

        // String literal
        if (token.Kind == TokenKind.StringLiteral)
            return "\"" + token.Value + "\"";

        // Negative number
        if (token.Value == "-")
        {
            var num = lexer.Next();
            return "-" + num.Value;
        }

        // Number or identifier (for enum values, true, false, inf, nan)
        return token.Value;
    }

    static FeatureSetOverrides? TryParseFeatureOption(ProtoLexer lexer)
    {
        // option features.field_presence = EXPLICIT;
        var peek = lexer.Peek();
        if (peek.Value != "features")
            return null;

        lexer.Next(); // features
        Expect(lexer, ".");
        var featureName = ExpectIdent(lexer);
        Expect(lexer, "=");
        var featureValue = ExpectIdent(lexer);
        Expect(lexer, ";");

        return ApplyFeatureOverride(null, featureName, featureValue);
    }

    static FeatureSetOverrides ApplyFeatureOverride(FeatureSetOverrides? existing, string featureName, string featureValue)
    {
        var overrides = existing ?? new FeatureSetOverrides();

        return featureName switch
        {
            "field_presence" => overrides with
            {
                FieldPresence = featureValue switch
                {
                    "IMPLICIT" => FieldPresence.Implicit,
                    "EXPLICIT" => FieldPresence.Explicit,
                    "LEGACY_REQUIRED" => FieldPresence.Required,
                    _ => overrides.FieldPresence,
                },
            },
            "enum_type" => overrides with
            {
                EnumType = featureValue switch
                {
                    "OPEN" => Descriptors.EnumType.Open,
                    "CLOSED" => Descriptors.EnumType.Closed,
                    _ => overrides.EnumType,
                },
            },
            "repeated_field_encoding" => overrides with
            {
                RepeatedFieldEncoding = featureValue switch
                {
                    "PACKED" => RepeatedFieldEncoding.Packed,
                    "EXPANDED" => RepeatedFieldEncoding.Expanded,
                    _ => overrides.RepeatedFieldEncoding,
                },
            },
            "message_encoding" => overrides with
            {
                MessageEncoding = featureValue switch
                {
                    "LENGTH_PREFIXED" => Descriptors.MessageEncoding.LengthPrefixed,
                    "DELIMITED" => Descriptors.MessageEncoding.Delimited,
                    _ => overrides.MessageEncoding,
                },
            },
            "json_format" => overrides with
            {
                JsonFormat = featureValue switch
                {
                    "ALLOW" => Descriptors.JsonFormat.Allow,
                    "LEGACY_BEST_EFFORT" => Descriptors.JsonFormat.LegacyBestEffort,
                    _ => overrides.JsonFormat,
                },
            },
            _ => overrides,
        };
    }

    static FeatureSetOverrides MergeOverrides(FeatureSetOverrides? existing, FeatureSetOverrides newOverrides)
    {
        if (existing is null)
            return newOverrides;

        return new FeatureSetOverrides
        {
            FieldPresence = newOverrides.FieldPresence ?? existing.FieldPresence,
            EnumType = newOverrides.EnumType ?? existing.EnumType,
            RepeatedFieldEncoding = newOverrides.RepeatedFieldEncoding ?? existing.RepeatedFieldEncoding,
            MessageEncoding = newOverrides.MessageEncoding ?? existing.MessageEncoding,
            JsonFormat = newOverrides.JsonFormat ?? existing.JsonFormat,
        };
    }

    static bool TryParseMessageOption(ProtoLexer lexer, MessageDescriptorProto msg)
    {
        var peek = lexer.Peek();
        if (peek.Value == "map_entry")
        {
            lexer.Next();
            Expect(lexer, "=");
            var val = lexer.Next();
            if (val.Value == "true")
                msg.IsMapEntry = true;
            Expect(lexer, ";");
            return true;
        }
        return false;
    }

    static bool GetDefaultPacked(FieldLabel label, FieldType fieldType, string syntax)
    {
        if (label != FieldLabel.Repeated || !IsPackableType(fieldType))
            return false;

        // Proto3 and editions default to packed; proto2 defaults to not packed
        return syntax != "proto2";
    }

    static bool IsPackableType(FieldType type) =>
        type is not (FieldType.String or FieldType.Bytes or FieldType.Message or FieldType.Group);

    static string ReadFullIdent(ProtoLexer lexer)
    {
        var name = ExpectIdent(lexer);
        while (lexer.Peek().Value == ".")
        {
            lexer.Next();
            name += "." + ExpectIdent(lexer);
        }
        return name;
    }

    static string ReadFullIdentOrDotPrefixed(ProtoLexer lexer)
    {
        var next = lexer.Peek();
        if (next.Value == ".")
        {
            lexer.Next();
            return "." + ReadFullIdent(lexer);
        }
        return ReadFullIdent(lexer);
    }

    static string ExpectIdent(ProtoLexer lexer)
    {
        var token = lexer.Next();
        if (token.Kind != TokenKind.Identifier)
            throw new ProtoParseException(lexer.FileName, token.Line, token.Column,
                $"Expected identifier, got '{token.Value}'");
        return token.Value;
    }

    static string ExpectString(ProtoLexer lexer)
    {
        var token = lexer.Next();
        if (token.Kind != TokenKind.StringLiteral)
            throw new ProtoParseException(lexer.FileName, token.Line, token.Column,
                $"Expected string literal, got '{token.Value}'");
        return token.Value;
    }

    static int ExpectInt(ProtoLexer lexer)
    {
        var token = lexer.Next();
        if (token.Kind != TokenKind.IntLiteral)
            throw new ProtoParseException(lexer.FileName, token.Line, token.Column,
                $"Expected integer, got '{token.Value}'");
        return ParseInt(token.Value);
    }

    static int ExpectSignedInt(ProtoLexer lexer)
    {
        var token = lexer.Next();
        var negative = false;
        if (token.Value == "-")
        {
            negative = true;
            token = lexer.Next();
        }
        if (token.Kind != TokenKind.IntLiteral)
            throw new ProtoParseException(lexer.FileName, token.Line, token.Column,
                $"Expected integer, got '{token.Value}'");
        var value = ParseInt(token.Value);
        return negative ? -value : value;
    }

    static int ParseInt(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt32(value, 16);
        return int.Parse(value);
    }

    static void Expect(ProtoLexer lexer, string expected)
    {
        var token = lexer.Next();
        if (token.Value != expected)
            throw new ProtoParseException(lexer.FileName, token.Line, token.Column,
                $"Expected '{expected}', got '{token.Value}'");
    }

    static void SkipOption(ProtoLexer lexer) =>
        SkipToSemicolon(lexer);

    static void SkipToSemicolon(ProtoLexer lexer)
    {
        while (true)
        {
            var token = lexer.Next();
            if (token.Kind == TokenKind.Eof || token.Value == ";") return;
            if (token.Value == "{") SkipBlockBody(lexer);
        }
    }

    static void SkipBlock(ProtoLexer lexer)
    {
        while (true)
        {
            var token = lexer.Next();
            if (token.Kind == TokenKind.Eof) return;
            if (token.Value == "{") break;
        }
        SkipBlockBody(lexer);
    }

    static void SkipBlockBody(ProtoLexer lexer)
    {
        var depth = 1;
        while (depth > 0)
        {
            var token = lexer.Next();
            if (token.Kind == TokenKind.Eof) return;
            if (token.Value == "{") depth++;
            else if (token.Value == "}") depth--;
        }
    }

    static void SkipUntil(ProtoLexer lexer, string terminator)
    {
        var depth = 0;
        while (true)
        {
            var token = lexer.Next();
            if (token.Kind == TokenKind.Eof) return;
            if (token.Value == "(") depth++;
            else if (token.Value == ")") { if (depth > 0) depth--; }
            if (depth == 0 && token.Value == terminator) return;
        }
    }

    static string ToPascalCase(string snakeCase)
    {
        var sb = new System.Text.StringBuilder(snakeCase.Length);
        var capitalize = true;
        foreach (var c in snakeCase)
        {
            if (c == '_')
            {
                capitalize = true;
            }
            else if (capitalize)
            {
                sb.Append(char.ToUpperInvariant(c));
                capitalize = false;
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
