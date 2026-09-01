using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using static Kurrent.Surge.Codecs.Avro.Generators.Tests.Unit.AvroGeneratorHarness;

namespace Kurrent.Surge.Codecs.Avro.Generators.Tests.Unit;

/// <summary>
/// Covers the message types the generator emits from a schema, as opposed to the codecs.
/// </summary>
/// <remarks>
/// Wire behaviour belongs to the round-trip and parity tests in Kurrent.Surge.Codecs.Avro.Tests, which
/// compile against these types. What is asserted here is the declaration shape those tests cannot see:
/// escaping, defaults, the variant interface, and the absence of a schema parse at type initialisation.
/// </remarks>
[Category("Codecs")]
public class AvroTypeGeneratorTests {

    [Test]
    public async ValueTask emits_specific_record_for_a_record_schema() {
        // Arrange
        var schema =
            """
            {
              "type": "record",
              "name": "Order",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "OrderId", "type": "string" },
                { "name": "Quantity", "type": "int" }
              ]
            }
            """;

        // Act
        var interop = SourceOf(RunWithApacheInterop(("/schemas/Order.avsc", schema)));
        var plain   = SourceOf(Run(("/schemas/Order.avsc", schema)));

        // Assert — with interop on, the interface and both members Apache's readers and writers call.
        await Assert.That(interop).Contains("public sealed partial record Order : global::Avro.Specific.ISpecificRecord");
        await Assert.That(interop).Contains("object? global::Avro.Specific.ISpecificRecord.Get(int fieldPos)");
        await Assert.That(interop).Contains("void global::Avro.Specific.ISpecificRecord.Put(int fieldPos, object? fieldValue)");

        // Assert — by default none of it is emitted. Avro's contract is the bytes, and a consumer who
        // never hands a type to Apache should not carry the surface Apache needs.
        await Assert.That(plain).Contains("public sealed partial record Order");
        await Assert.That(plain).DoesNotContain("ISpecificRecord");

        // Assert — the properties are the same either way.
        foreach (var source in (string[])[interop, plain]) {
            await Assert.That(source).Contains("namespace Acme.Messages;");
            await Assert.That(source).Contains("public required string OrderId { get; set; }");
            await Assert.That(source).Contains("public required int Quantity { get; set; }");
        }
    }

    [Test]
    public async ValueTask emits_variant_interface_for_an_all_record_union() {
        // Arrange
        var schema =
            """
            {
              "type": "record",
              "name": "Sighting",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "Subject", "type": [
                    { "type": "record", "name": "Cat", "fields": [ { "name": "Lives", "type": "int" } ] },
                    { "type": "record", "name": "Dog", "fields": [ { "name": "Bark", "type": "string" } ] }
                  ] }
              ]
            }
            """;

        // Act
        var source = SourceOf(Run(("/schemas/Sighting.avsc", schema)));

        // Assert — every branch implements the interface, and the field is typed as it.
        await Assert.That(source).Contains("public partial interface ISightingSubjectVariant;");
        await Assert.That(source).Contains("record Cat : global::Acme.Messages.ISightingSubjectVariant");
        await Assert.That(source).Contains("record Dog : global::Acme.Messages.ISightingSubjectVariant");
        await Assert.That(source).Contains("public required global::Acme.Messages.ISightingSubjectVariant Subject { get; set; }");
    }

    [Test]
    public async ValueTask types_a_nullable_record_union_as_a_nullable_variant() {
        // Arrange — the same union with a null branch. The value is a Cat, a Dog, or absent, so the
        // property has to admit null. A non-nullable property with no initializer and no `required`
        // is a hole: the compiler cannot see it and the decoder can produce it.
        var schema =
            """
            {
              "type": "record",
              "name": "Sighting",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "Subject", "type": [
                    "null",
                    { "type": "record", "name": "Cat", "fields": [ { "name": "Lives", "type": "int" } ] },
                    { "type": "record", "name": "Dog", "fields": [ { "name": "Bark", "type": "string" } ] }
                  ] }
              ]
            }
            """;

        // Act
        var source = SourceOf(Run(("/schemas/Sighting.avsc", schema)));

        // Assert
        await Assert.That(source).Contains("public global::Acme.Messages.ISightingSubjectVariant? Subject { get; set; }");
    }

    [Test]
    public async ValueTask implements_every_variant_interface_a_record_belongs_to() {
        // Arrange — the same two records in two unions. Each field gets its own interface, so a branch
        // record has to carry both; holding one interface per record would silently drop whichever field
        // was walked first.
        var schema =
            """
            {
              "type": "record",
              "name": "Report",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "Primary", "type": [
                    { "type": "record", "name": "Cat", "fields": [ { "name": "Lives", "type": "int" } ] },
                    { "type": "record", "name": "Dog", "fields": [ { "name": "Bark", "type": "string" } ] }
                  ] },
                { "name": "Secondary", "type": [ "Cat", "Dog" ] }
              ]
            }
            """;

        // Act
        var source = SourceOf(Run(("/schemas/Report.avsc", schema)));

        // Assert
        await Assert.That(source).Contains("public partial interface IReportPrimaryVariant;");
        await Assert.That(source).Contains("public partial interface IReportSecondaryVariant;");
        await Assert.That(source).Contains(
            "record Cat : global::Acme.Messages.IReportPrimaryVariant, global::Acme.Messages.IReportSecondaryVariant");
        await Assert.That(source).Contains(
            "record Dog : global::Acme.Messages.IReportPrimaryVariant, global::Acme.Messages.IReportSecondaryVariant");
    }

    [Test]
    public async ValueTask escapes_field_names_that_are_csharp_keywords() {
        // Arrange — 'class' and 'event' are legal Avro names and illegal C# identifiers.
        var schema =
            """
            {
              "type": "record",
              "name": "Booking",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "class", "type": "string" },
                { "name": "event", "type": "int" }
              ]
            }
            """;

        // Act
        var source = SourceOf(Run(("/schemas/Booking.avsc", schema)));

        // Assert — escaped everywhere the name is used as an identifier, including the codec's accessors.
        await Assert.That(source).Contains("public required string @class { get; set; }");
        await Assert.That(source).Contains("public required int @event { get; set; }");
        await Assert.That(source).Contains("encoder.WriteString(value.@class);");
        await Assert.That(source).Contains("encoder.WriteInt(value.@event);");
    }

    [Test]
    public async ValueTask escapes_namespace_segments_that_are_csharp_keywords() {
        // Arrange — a namespace segment is escaped on its own, not as a whole.
        var schema =
            """
            {
              "type": "record",
              "name": "Ping",
              "namespace": "Acme.event.internal",
              "fields": [ { "name": "Id", "type": "string" } ]
            }
            """;

        // Act
        var source = SourceOf(Run(("/schemas/Ping.avsc", schema)));

        // Assert
        await Assert.That(source).Contains("namespace Acme.@event.@internal;");
        await Assert.That(source).Contains("typeof(global::Acme.@event.@internal.Ping)");
    }

    [Test]
    public async ValueTask initialises_property_from_schema_default() {
        // Arrange — one default per JSON shape the renderer supports.
        var schema =
            """
            {
              "type": "record",
              "name": "Settings",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "Retries",  "type": "int",              "default": 3 },
                { "name": "Label",    "type": "string",           "default": "none" },
                { "name": "Enabled",  "type": "boolean",          "default": true },
                { "name": "Note",     "type": ["null", "string"], "default": null },
                { "name": "Tags",     "type": { "type": "array", "items": "string" }, "default": ["a", "b"] },
                { "name": "Required", "type": "string" }
              ]
            }
            """;

        // Act
        var source = SourceOf(Run(("/schemas/Settings.avsc", schema)));

        // Assert — a defaulted field is initialised and drops 'required'; an undefaulted one keeps it.
        await Assert.That(source).Contains("public int Retries { get; set; } = 3;");
        await Assert.That(source).Contains("public string Label { get; set; } = \"none\";");
        await Assert.That(source).Contains("public bool Enabled { get; set; } = true;");
        await Assert.That(source).Contains("public string? Note { get; set; } = null;");
        await Assert.That(source).Contains("Tags { get; set; } = new global::System.Collections.Generic.List<string> { \"a\", \"b\" };");
        await Assert.That(source).Contains("public required string Required { get; set; }");
    }

    [Test]
    public async ValueTask initialises_property_from_a_union_default_on_any_branch() {
        // Arrange — the Avro specification says a union default matches ONE element of the union, not
        // the first. Reading it against the first branch turns the 0 below into null.
        var schema =
            """
            {
              "type": "record",
              "name": "Settings",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "Retries", "type": ["null", "int"],    "default": 0 },
                { "name": "Note",    "type": ["string", "null"], "default": null },
                { "name": "Mode",    "type": ["string", "int"],  "default": "fast" },
                { "name": "Unset",   "type": ["null", "int"] }
              ]
            }
            """;

        // Act
        var source = SourceOf(Run(("/schemas/Settings.avsc", schema)));

        // Assert
        await Assert.That(source).Contains("public int? Retries { get; set; } = 0;");
        await Assert.That(source).Contains("public string? Note { get; set; } = null;");
        await Assert.That(source).Contains("public object Mode { get; set; } = \"fast\";");
        await Assert.That(source).Contains("public int? Unset { get; set; }");
    }

    [Test]
    public async ValueTask initialises_a_fixed_property_by_constructing_it() {
        // Arrange — a fixed maps to a SpecificFixed subclass, so a byte[] initialiser would not compile.
        // The default's code points 0-255 are its bytes.
        var schema =
            """
            {
              "type": "record",
              "name": "Blob",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "Salt", "type": { "type": "fixed", "name": "Salt4", "size": 4 }, "default": "\u00de\u00ad\u00be\u00ef" },
                { "name": "Wrong", "type": { "type": "fixed", "name": "Salt8", "size": 8 }, "default": "" }
              ]
            }
            """;

        // Act
        var source = SourceOf(Run(("/schemas/Blob.avsc", schema)));

        // Assert — and a default whose length disagrees with the declared size is dropped, not truncated.
        await Assert.That(source).Contains(
            "public global::Acme.Messages.Salt4 Salt { get; set; } = new global::Acme.Messages.Salt4 { Value = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF } };");
        await Assert.That(source).Contains("public required global::Acme.Messages.Salt8 Wrong { get; set; }");
    }

    [Test]
    public async ValueTask exposes_collections_as_read_only_interfaces() {
        // Arrange — a collection reaches the public surface read-only, per the repo's collection
        // conventions. The decoder still builds the concrete type, because it sizes from the block count
        // and adds to it.
        var schema =
            """
            {
              "type": "record",
              "name": "Basket",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "Lines",    "type": { "type": "array", "items": "string" } },
                { "name": "Counters", "type": { "type": "map",   "values": "long" } }
              ]
            }
            """;

        // Act
        var source = SourceOf(Run(("/schemas/Basket.avsc", schema)));

        // Assert
        await Assert.That(source).Contains("public required global::System.Collections.Generic.IReadOnlyList<string> Lines { get; set; }");
        await Assert.That(source).Contains("public required global::System.Collections.Generic.IReadOnlyDictionary<string, long> Counters { get; set; }");
        await Assert.That(source).Contains("var _Lines = new global::System.Collections.Generic.List<string>();");
        await Assert.That(source).Contains("var _Counters = new global::System.Collections.Generic.Dictionary<string, long>();");
    }

    [Test]
    public async ValueTask emits_a_fixed_backed_decimal_as_avro_decimal() {
        // Arrange — 'decimal' is the one logical type Apache accepts on a fixed base. The fixed is only
        // storage, so the field is an AvroDecimal and no C# class is emitted for the fixed itself.
        var schema =
            """
            {
              "type": "record",
              "name": "Invoice",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "Total", "type": {
                    "type": "fixed", "name": "Money", "size": 8,
                    "logicalType": "decimal", "precision": 12, "scale": 2 } }
              ]
            }
            """;

        // Act
        var source = SourceOf(Run(("/schemas/Invoice.avsc", schema)));

        // Assert
        await Assert.That(source).Contains("public required global::Kurrent.Surge.Codecs.Avro.AvroDecimal Total { get; set; }");
        await Assert.That(source).DoesNotContain("class Money");
        await Assert.That(source).Contains("WriteDecimalFixed(value.Total, encoder, 2, 8);");
        await Assert.That(source).Contains("ReadDecimalFixed(decoder, 2, 8)");
    }

    [Test]
    public async ValueTask omits_fixed_decimal_helpers_from_a_schema_that_has_no_fixed_decimal() {
        // Arrange & Act — the bytes-backed and fixed-backed converters are separate, so a schema using
        // one must not carry the other.
        var bytesDecimal =
            """
            {
              "type": "record",
              "name": "Payment",
              "namespace": "Acme.Messages",
              "fields": [ { "name": "Amount", "type": { "type": "bytes", "logicalType": "decimal", "precision": 12, "scale": 2 } } ]
            }
            """;

        var source = SourceOf(Run(("/schemas/Payment.avsc", bytesDecimal)));

        // Assert
        await Assert.That(source).Contains("WriteDecimal(");
        await Assert.That(source).DoesNotContain("WriteDecimalFixed");
    }

    [Test]
    public async ValueTask erases_a_logical_type_it_does_not_model() {
        // Arrange — 'local-timestamp-nanos' is in the specification and 'varchar' is not. Apache
        // implements neither, and resolves both to UnknownLogicalType, whose conversion is the identity.
        // Erasing to the underlying primitive is the same policy, and the specification's own instruction.
        var schema =
            """
            {
              "type": "record",
              "name": "Reading",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "Nanos",    "type": { "type": "long",   "logicalType": "local-timestamp-nanos" } },
                { "name": "Invented", "type": { "type": "string", "logicalType": "varchar", "maxLength": 65 } }
              ]
            }
            """;

        // Act
        var source = SourceOf(Run(("/schemas/Reading.avsc", schema)));

        // Assert
        await Assert.That(source).Contains("public required long Nanos { get; set; }");
        await Assert.That(source).Contains("public required string Invented { get; set; }");
        await Assert.That(source).Contains("encoder.WriteLong(value.Nanos);");
        await Assert.That(source).Contains("encoder.WriteString(value.Invented);");
    }

    [Test]
    public async ValueTask erases_a_logical_type_declared_on_the_wrong_primitive() {
        // Arrange — 'date' is defined on int. On a long it is invalid, and the specification says to
        // ignore the logical type and use the underlying one rather than emit a DateTime backed by a long.
        var schema =
            """
            {
              "type": "record",
              "name": "Event",
              "namespace": "Acme.Messages",
              "fields": [ { "name": "When", "type": { "type": "long", "logicalType": "date" } } ]
            }
            """;

        // Act
        var source = SourceOf(Run(("/schemas/Event.avsc", schema)));

        // Assert
        await Assert.That(source).Contains("public required long When { get; set; }");
        await Assert.That(source).Contains("encoder.WriteLong(value.When);");
        await Assert.That(source).DoesNotContain("UnixEpoch");
    }

    [Test]
    public async ValueTask emits_local_timestamps_as_datetime() {
        // Arrange
        var schema =
            """
            {
              "type": "record",
              "name": "Reading",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "Ms", "type": { "type": "long", "logicalType": "local-timestamp-millis" } },
                { "name": "Us", "type": { "type": "long", "logicalType": "local-timestamp-micros" } }
              ]
            }
            """;

        // Act
        var source = SourceOf(Run(("/schemas/Reading.avsc", schema)));

        // Assert — the write matches the absolute variants; only the read converts back to local.
        await Assert.That(source).Contains("public required global::System.DateTime Ms { get; set; }");
        await Assert.That(source).Contains("encoder.WriteLong((long)(value.Ms.ToUniversalTime() - UnixEpoch).TotalMilliseconds);");
        await Assert.That(source).Contains("UnixEpoch.AddMilliseconds(decoder.ReadLong()).ToLocalTime()");
        await Assert.That(source).Contains("UnixEpoch.AddTicks(decoder.ReadLong() * TicksPerMicrosecond).ToLocalTime()");
    }

    [Test]
    public async ValueTask omits_schema_parse_when_nothing_reads_it() {
        // Arrange & Act — the whole point of owning type generation. A static field with an initialiser
        // gives the type a static constructor, which roots Avro.Schema.Parse, which roots Newtonsoft.Json
        // into every native binary whether or not the schema is ever read.
        var schema =
            """
            {
              "type": "record",
              "name": "Order",
              "namespace": "Acme.Messages",
              "fields": [ { "name": "OrderId", "type": "string" } ]
            }
            """;

        var source = SourceOf(Run(("/schemas/Order.avsc", schema)));

        // Assert — by default there is no schema text on the type at all, so nothing can reach
        // Avro.Schema.Parse even by accident. The AOT probe proves the consequence empirically: zero
        // Newtonsoft symbols in the native binary.
        await Assert.That(source).DoesNotContain("Avro.Schema.Parse");
        await Assert.That(source).DoesNotContain("SchemaJson");

        // Assert — with interop on the schema is still there, but declared without an initialiser and
        // parsed only from inside the accessor. A static field with an initialiser gives the type a
        // static constructor, which roots the parse whether or not anything reads it.
        var interop = SourceOf(RunWithApacheInterop(("/schemas/Order.avsc", schema)));

        await Assert.That(interop).Contains("static global::Avro.Schema? s_schema;");
        await Assert.That(interop).Contains("ISpecificRecord.Schema => s_schema ??= global::Avro.Schema.Parse(SchemaJson);");
        await Assert.That(Regex.Matches(interop, @"Avro\.Schema\.Parse").Count).IsEqualTo(1);
    }

    [Test]
    public async ValueTask duplicate_type_reports_a_diagnostic() {
        // Arrange — two schemas declaring the same fullname with different fields. Only one C# type can
        // exist, so the other declaration is dropped and the conflict has to be visible.
        var first =
            """
            {
              "type": "record",
              "name": "Order",
              "namespace": "Acme.Messages",
              "fields": [ { "name": "OrderId", "type": "string" } ]
            }
            """;

        var second =
            """
            {
              "type": "record",
              "name": "Order",
              "namespace": "Acme.Messages",
              "fields": [ { "name": "Reference", "type": "long" } ]
            }
            """;

        // Act
        var result = Run(("/schemas/First.avsc", first), ("/schemas/Second.avsc", second));

        // Assert
        var diagnostic = result.Diagnostics.Single();

        // An error, not a warning: the surviving type comes from one schema while the other schema's
        // codec was emitted against its own declaration, so the compilation fails either way.
        await Assert.That(diagnostic.Id).IsEqualTo("KAVRO003");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(Regex.Matches(SourceOf(result), "partial record Order").Count).IsEqualTo(1);
    }

    [Test]
    public async ValueTask emits_one_type_when_two_schemas_declare_it_identically() {
        // Arrange — sharing a nested type between schema files is normal and generates the same C# from
        // either, so it is deduplicated silently rather than reported.
        var schema =
            """
            {
              "type": "record",
              "name": "Order",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "Customer", "type": {
                    "type": "record",
                    "name": "Customer",
                    "fields": [ { "name": "Name", "type": "string" } ] } }
              ]
            }
            """;

        var other =
            """
            {
              "type": "record",
              "name": "Refund",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "Customer", "type": {
                    "type": "record",
                    "name": "Customer",
                    "fields": [ { "name": "Name", "type": "string" } ] } }
              ]
            }
            """;

        // Act
        var result = Run(("/schemas/Order.avsc", schema), ("/schemas/Refund.avsc", other));

        // Assert
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(Regex.Matches(SourceOf(result), "partial record Customer").Count).IsEqualTo(1);
    }

    [Test]
    public async ValueTask emits_enum_and_fixed_declarations() {
        // Arrange
        var schema =
            """
            {
              "type": "record",
              "name": "Shipment",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "Status", "type": {
                    "type": "enum", "name": "Status", "symbols": ["Pending", "default"] } },
                { "name": "Checksum", "type": { "type": "fixed", "name": "Md5", "size": 16 } }
              ]
            }
            """;

        // Act
        var source = SourceOf(Run(("/schemas/Shipment.avsc", schema)));

        // Assert — a symbol that is a C# keyword is escaped, and a fixed derives from our own base type.
        await Assert.That(source).Contains("public enum Status");
        await Assert.That(source).Contains("@default,");
        await Assert.That(source).Contains("public sealed partial class Md5 : global::Kurrent.Surge.Codecs.Avro.AvroFixedValue");
        await Assert.That(source).Contains("public Md5() : base(16) { }");
    }

    [Test]
    public async ValueTask writes_the_inherited_namespace_into_a_nested_types_schema() {
        // Arrange — a nested declaration inherits the enclosing namespace, so its own schema text has to
        // carry it. Parsed on its own the span would otherwise resolve to a different fullname than the
        // type it belongs to.
        var schema =
            """
            {
              "type": "record",
              "name": "Order",
              "namespace": "Acme.Messages",
              "fields": [
                { "name": "Customer", "type": {
                    "type": "record",
                    "name": "Customer",
                    "fields": [ { "name": "Name", "type": "string" } ] } }
              ]
            }
            """;

        // Act — schema text is only emitted when Apache needs to read it.
        var source = SourceOf(RunWithApacheInterop(("/schemas/Order.avsc", schema)));

        // Assert
        await Assert.That(source).Contains("""const string SchemaJson = @"{""namespace"": ""Acme.Messages"", ""type"": ""record"",""");
    }
}
