using Google.Protobuf.Reflection;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;
using FileOptions = Google.Protobuf.Reflection.FileOptions;

namespace Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;

/// <summary>
/// Declarations the parser has to carry into the descriptor rather than consume.
/// </summary>
/// <remarks>
/// Everything here is invisible to the transcoder — no payload changes shape because a range is
/// reserved. It is the schema converter that needs them, and a fact skipped at the parser is lost
/// before any later stage can see it.
/// </remarks>
public class ProtoParserTests {
    static DescriptorProto Message(string schema, string fullName) =>
        ProtoSchemaSet.Build(schema).FindMessage(fullName).ToProto();

    static EnumDescriptorProto DeclaredEnum(string schema, string messageName, string enumName) =>
        ProtoSchemaSet.Build(schema).FindMessage(messageName).EnumTypes.Single(declared => declared.Name == enumName).ToProto();

    // Dependencies come first, so the file that was handed in is the last one built.
    static FileDescriptorProto ParsedFile(string schema) => ProtoSchemaSet.Build(schema).Files[^1].ToProto();

    #region ->> Reserved field numbers and names <<-

    [Test]
    public async ValueTask keeps_reserved_field_ranges() {
        // Arrange — the descriptor's end is exclusive, so 'to 11' becomes 12 and a bare 15 becomes 16.
        var schema = """
                     syntax = "proto3";
                     package probe;
                     message Order {
                       reserved 2, 15, 9 to 11;
                       string order_id = 1;
                     }
                     """;

        var expected = new[] {
            new DescriptorProto.Types.ReservedRange { Start = 2,  End = 3  },
            new DescriptorProto.Types.ReservedRange { Start = 15, End = 16 },
            new DescriptorProto.Types.ReservedRange { Start = 9,  End = 12 }
        };

        // Act
        var actual = Message(schema, "probe.Order").ReservedRange;

        // Assert
        await Assert.That(actual).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async ValueTask keeps_a_reserved_range_that_runs_to_max() {
        // Arrange — 536870911 is the largest field number the format allows.
        var schema = """
                     syntax = "proto3";
                     package probe;
                     message Order {
                       reserved 100 to max;
                       string order_id = 1;
                     }
                     """;

        var expected = new DescriptorProto.Types.ReservedRange { Start = 100, End = 536870912 };

        // Act
        var actual = Message(schema, "probe.Order").ReservedRange.Single();

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask keeps_reserved_field_names() {
        // Arrange
        var schema = """
                     syntax = "proto3";
                     package probe;
                     message Order {
                       reserved "legacy_total", "old_status";
                       string order_id = 1;
                     }
                     """;

        var expected = new[] { "legacy_total", "old_status" };

        // Act
        var actual = Message(schema, "probe.Order").ReservedName;

        // Assert
        await Assert.That(actual).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async ValueTask keeps_reserved_declarations_on_a_nested_message() {
        // Arrange
        var schema = """
                     syntax = "proto3";
                     package probe;
                     message Order {
                       message Line {
                         reserved 4;
                         reserved "discount";
                         string sku = 1;
                       }
                       Line line = 1;
                     }
                     """;

        var expectedRange = new DescriptorProto.Types.ReservedRange { Start = 4, End = 5 };

        // Act
        var line = Message(schema, "probe.Order.Line");

        // Assert
        await Assert.That(line.ReservedRange.Single()).IsEqualTo(expectedRange);
        await Assert.That(line.ReservedName.Single()).IsEqualTo("discount");
    }

    #endregion // Reserved field numbers and names

    #region ->> Reserved enum values <<-

    [Test]
    public async ValueTask keeps_reserved_enum_ranges_with_an_inclusive_end() {
        // Arrange — an enum range ends inclusively, where a message range does not. An enum value also
        // reaches into the negatives, which a field number never does.
        var schema = """
                     syntax = "proto3";
                     package probe;
                     message Order {
                       enum Priority {
                         PRIORITY_UNSPECIFIED = 0;
                         reserved 3 to 5, -2 to -1;
                       }
                       Priority priority = 1;
                     }
                     """;

        var expected = new[] {
            new EnumDescriptorProto.Types.EnumReservedRange { Start = 3,  End = 5  },
            new EnumDescriptorProto.Types.EnumReservedRange { Start = -2, End = -1 }
        };

        // Act
        var actual = DeclaredEnum(schema, "probe.Order", "Priority").ReservedRange;

        // Assert
        await Assert.That(actual).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async ValueTask keeps_reserved_enum_names() {
        // Arrange
        var schema = """
                     syntax = "proto3";
                     package probe;
                     message Order {
                       enum Priority {
                         PRIORITY_UNSPECIFIED = 0;
                         reserved "PRIORITY_LEGACY";
                       }
                       Priority priority = 1;
                     }
                     """;

        // Act
        var actual = DeclaredEnum(schema, "probe.Order", "Priority").ReservedName.Single();

        // Assert
        await Assert.That(actual).IsEqualTo("PRIORITY_LEGACY");
    }

    #endregion // Reserved enum values

    #region ->> Enum aliases <<-

    [Test]
    public async ValueTask builds_an_enum_whose_values_share_a_number() {
        // Arrange — two names on one number is legal only when allow_alias is declared. Skipping the
        // option does not merely lose a fact: the builder rejects the schema outright.
        var schema = """
                     syntax = "proto3";
                     package probe;
                     message Order {
                       enum Priority {
                         option allow_alias = true;
                         PRIORITY_UNSPECIFIED = 0;
                         PRIORITY_DEFAULT = 0;
                         PRIORITY_HIGH = 1;
                       }
                       Priority priority = 1;
                     }
                     """;

        var expected = new[] { "PRIORITY_UNSPECIFIED", "PRIORITY_DEFAULT", "PRIORITY_HIGH" };

        // Act
        var priority = DeclaredEnum(schema, "probe.Order", "Priority");

        // Assert
        await Assert.That(priority.Options!.AllowAlias).IsTrue();
        await Assert.That(priority.Value.Select(value => value.Name)).IsDeepEquivalentTo(expected);
    }

    #endregion // Enum aliases

    #region ->> Options <<-

    [Test]
    public async ValueTask keeps_the_generated_code_file_options() {
        // Arrange — none of these reaches the wire or the JSON mapping. They are carried so the file
        // can be written back out as the one that was read.
        var schema = """
                     syntax = "proto3";
                     package probe;
                     option csharp_namespace = "Probe.Orders";
                     option java_package = "io.probe.orders";
                     option java_outer_classname = "OrdersProto";
                     option java_multiple_files = true;
                     option go_package = "probe/orders";
                     option objc_class_prefix = "PRB";
                     option cc_enable_arenas = true;
                     message Order {
                       string order_id = 1;
                     }
                     """;

        var expected = new FileOptions {
            CsharpNamespace   = "Probe.Orders",
            JavaPackage       = "io.probe.orders",
            JavaOuterClassname = "OrdersProto",
            JavaMultipleFiles = true,
            GoPackage         = "probe/orders",
            ObjcClassPrefix   = "PRB",
            CcEnableArenas    = true
        };

        // Act
        var actual = ParsedFile(schema).Options;

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask keeps_a_deprecated_field() {
        // Arrange — the bracket form, which is how every deprecation in this repository is written.
        var schema = """
                     syntax = "proto3";
                     package probe;
                     message Order {
                       string order_id = 1;
                       uint64 revision = 2 [deprecated = true];
                     }
                     """;

        // Act
        var revision = Message(schema, "probe.Order").Field.Single(field => field.Name == "revision");

        // Assert
        await Assert.That(revision.Options!.Deprecated).IsTrue();
    }

    [Test]
    public async ValueTask keeps_a_deprecated_message_and_enum() {
        // Arrange — the statement form, which is how a whole declaration is deprecated.
        var schema = """
                     syntax = "proto3";
                     package probe;
                     message Order {
                       option deprecated = true;
                       string order_id = 1;
                       enum Priority {
                         option deprecated = true;
                         PRIORITY_UNSPECIFIED = 0;
                       }
                       Priority priority = 2;
                     }
                     """;

        // Act
        var order    = Message(schema, "probe.Order");
        var priority = DeclaredEnum(schema, "probe.Order", "Priority");

        // Assert
        await Assert.That(order.Options!.Deprecated).IsTrue();
        await Assert.That(priority.Options!.Deprecated).IsTrue();
    }

    [Test]
    public async ValueTask still_reads_a_map_field_after_the_option_helpers_were_folded_together() {
        // Arrange — map_entry is compiler-generated rather than hand-written, and it shares the parse
        // path the two new options now use. A map that stopped resolving would be the first sign.
        var schema = """
                     syntax = "proto3";
                     package probe;
                     message Order {
                       map<string, string> tags = 1;
                     }
                     """;

        // Act
        var tags = ProtoSchemaSet.Build(schema).FindMessage("probe.Order").FindFieldByNumber(1);

        // Assert
        await Assert.That(tags.IsMap).IsTrue();
    }

    #endregion // Options

    #region ->> Comments <<-

    [Test]
    public async ValueTask carries_a_leading_comment_onto_the_declaration() {
        // Arrange — a comment is the one fact a descriptor can hold only through SourceCodeInfo, and
        // the lexer used to drop it before anything downstream could see it.
        var schema = """
                     syntax = "proto3";
                     package probe;
                     // An order placed by a customer.
                     message Order {
                       // The identifier the customer quotes.
                       string order_id = 1;
                     }
                     """;

        // Act
        var order = ProtoSchemaSet.Build(schema).FindMessage("probe.Order");

        // Assert
        await Assert.That(order.Declaration!.LeadingComments).IsEqualTo(" An order placed by a customer.\n");
        await Assert.That(order.Fields.InDeclarationOrder()[0].Declaration!.LeadingComments).IsEqualTo(" The identifier the customer quotes.\n");
    }

    [Test]
    public async ValueTask joins_a_run_of_line_comments_into_one_block() {
        // Arrange
        var schema = """
                     syntax = "proto3";
                     package probe;
                     // First line.
                     // Second line.
                     message Order {
                       string order_id = 1;
                     }
                     """;

        // Act
        var order = ProtoSchemaSet.Build(schema).FindMessage("probe.Order");

        // Assert
        await Assert.That(order.Declaration!.LeadingComments).IsEqualTo(" First line.\n Second line.\n");
    }

    [Test]
    public async ValueTask separates_a_detached_comment_from_the_leading_one() {
        // Arrange — a blank line is the only thing telling the two apart, and it is what decides
        // whether the text describes the declaration or the file above it.
        var schema = """
                     syntax = "proto3";
                     package probe;
                     // Copyright notice.

                     // An order placed by a customer.
                     message Order {
                       string order_id = 1;
                     }
                     """;

        // Act
        var declaration = ProtoSchemaSet.Build(schema).FindMessage("probe.Order").Declaration!;

        // Assert
        await Assert.That(declaration.LeadingComments).IsEqualTo(" An order placed by a customer.\n");
        await Assert.That(declaration.LeadingDetachedComments.Single()).IsEqualTo(" Copyright notice.\n");
    }

    [Test]
    public async ValueTask carries_a_trailing_comment_onto_the_declaration_it_follows() {
        // Arrange — the lexer gathers this onto the next token, so without the line check it would
        // describe the field below it instead of the one it sits on.
        var schema = """
                     syntax = "proto3";
                     package probe;
                     message Order {
                       string order_id = 1; // Quoted by the customer.
                       string region = 2;
                     }
                     """;

        // Act
        var fields = ProtoSchemaSet.Build(schema).FindMessage("probe.Order").Fields.InDeclarationOrder();

        // Assert
        // The second field has no declaration at all, which is the point: an uncommented field gets
        // no location, so the trailing comment cannot have leaked onto it as a leading one.
        await Assert.That(fields[0].Declaration!.TrailingComments).IsEqualTo(" Quoted by the customer.\n");
        await Assert.That(fields[1].Declaration).IsNull();
    }

    [Test]
    public async ValueTask carries_comments_on_an_enum_and_its_values() {
        // Arrange
        var schema = """
                     syntax = "proto3";
                     package probe;
                     message Order {
                       // How urgently the order ships.
                       enum Priority {
                         // Not stated.
                         PRIORITY_UNSPECIFIED = 0;
                       }
                       Priority priority = 1;
                     }
                     """;

        // Act
        var priority = ProtoSchemaSet.Build(schema).FindMessage("probe.Order").EnumTypes.Single();

        // Assert
        await Assert.That(priority.Declaration!.LeadingComments).IsEqualTo(" How urgently the order ships.\n");
        await Assert.That(priority.Values[0].Declaration!.LeadingComments).IsEqualTo(" Not stated.\n");
    }

    [Test]
    public async ValueTask leaves_an_uncommented_schema_without_source_info() {
        // Arrange — a location is emitted only where a comment exists. The builder throws on a span
        // it cannot read, so every span not emitted is one that cannot be wrong.
        var schema = """
                     syntax = "proto3";
                     package probe;
                     message Order {
                       string order_id = 1;
                     }
                     """;

        // Act
        var file = ParsedFile(schema);

        // Assert
        await Assert.That(file.SourceCodeInfo).IsNull();
    }

    #endregion // Comments

    #region ->> Repository corpus <<-

    [Test]
    public async ValueTask builds_every_schema_in_the_repository() {
        // Arrange — the fixture schemas are two files and exercise a fraction of the grammar. These
        // are the real ones, and they are what a span arithmetic error fails on: a location the
        // builder cannot read throws rather than losing a comment.
        var root = Path.Combine(AppContext.BaseDirectory, "corpus");

        var files = Directory.EnumerateFiles(root, "*.proto", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new ProtoFile(Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)))
            .ToArray();

        // Act
        var act = () => ProtoSchemaSet.Build(files);

        // Assert
        await Assert.That(files).IsNotEmpty();
        await Assert.That(act).ThrowsNothing();
    }

    #endregion // Repository corpus
}
