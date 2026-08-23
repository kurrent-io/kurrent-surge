using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Kurrent.Surge.Codecs.Protobuf.Experimental;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Indexed;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;
using Transcoder.Groups;

namespace Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;

/// <summary>
/// Groups, which only proto2 has. A group's body is delimited by a start and end tag rather than by
/// a length prefix, so it is the one construct the proto3 probe schema cannot reach.
/// </summary>
public class ProtobufJsonWriterGroupTests {
    const string ParcelTypeName = "transcoder.groups.Parcel";

    static string Text { get; } = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "protos", "transcoder_groups.proto"));

    static ProtoSchemaSet Set { get; } = ProtoSchemaSet.Build(new ProtoFile("transcoder_groups.proto", Text));

    static JsonFormatter Oracle { get; } = new(
        JsonFormatter.Settings.Default.WithTypeRegistry(TypeRegistry.FromFiles(TranscoderGroupsReflection.Descriptor)));

    static string Write(ReadOnlyMemory<byte> data, ProtobufJsonWriterOptions? options = null) =>
        new ProtobufJsonWriter(Set, ParcelTypeName, options).Write(data);

    [Test]
    public async ValueTask parses_a_group_from_schema_text() {
        // Act
        var parcel = Set.FindMessage(ParcelTypeName);
        var line   = parcel.FindFieldByNumber(2);

        // Assert
        await Assert.That(line.FieldType).IsEqualTo(FieldType.Group);
        await Assert.That(line.IsRepeated).IsTrue();
        await Assert.That(line.MessageType.Name).IsEqualTo("Line");
    }

    [Test]
    public async ValueTask matches_google_json_for_a_repeated_group() {
        // Arrange
        var parcel = new Parcel {
            ParcelId  = "P-1",
            Reference = "REF-1",
            Line = {
                new Parcel.Types.Line { Sku = "A", Quantity = 1 },
                new Parcel.Types.Line { Sku = "B", Quantity = 7, Barcode = ByteString.CopyFrom(1, 2, 3) }
            }
        };

        var expected = Normalize(Oracle.Format(parcel));

        // Act
        var actual = Normalize(Write(parcel.ToByteArray()));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask matches_google_json_for_a_group_nested_inside_a_group() {
        // The inner end tag must close the inner group only. Reading it as the outer one truncates
        // every field that follows.

        // Arrange
        var parcel = new Parcel {
            Reference = "REF-2",
            Shipping = new Parcel.Types.Shipping {
                Carrier = Carrier.Air,
                Weight  = 4_200,
                Address = new Parcel.Types.Shipping.Types.Address { City = "Lisbon", Postcode = "1000-001" }
            }
        };

        var expected = Normalize(Oracle.Format(parcel));

        // Act
        var actual = Normalize(Write(parcel.ToByteArray()));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask reads_fields_declared_after_a_group() {
        // Arrange
        var parcel = new Parcel {
            Reference = "REF-3",
            Line      = { new Parcel.Types.Line { Sku = "A" } },
            Retries   = 9
        };

        var expected = Normalize(Oracle.Format(parcel));

        // Act
        var actual = Normalize(Write(parcel.ToByteArray()));

        // Assert
        await Assert.That(actual).Contains("\"reference\":\"REF-3\"");
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask matches_google_json_for_a_map_and_a_oneof_declared_inside_a_group() {
        // A group body obeys the full message grammar. A reader that only handles the constructs a
        // group "usually" contains mis-parses these two.

        // Arrange
        var parcel = new Parcel {
            Reference = "REF-6",
            Shipping = new Parcel.Types.Shipping {
                Carrier = Carrier.Road,
                Labels  = { ["fragile"] = "yes", ["side"] = "up" },
                Account = "ACC-9"
            }
        };

        var expected = Normalize(Oracle.Format(parcel));

        // Act
        var actual = Normalize(Write(parcel.ToByteArray()));

        // Assert
        await Assert.That(actual).Contains("\"labels\"").And.Contains("\"account\"");
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask matches_google_json_for_an_empty_group() {
        // Arrange
        var parcel = new Parcel {
            Reference = "REF-4",
            Shipping  = new Parcel.Types.Shipping()
        };

        var expected = Normalize(Oracle.Format(parcel));

        // Act
        var actual = Normalize(Write(parcel.ToByteArray()));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask matches_google_json_when_defaults_are_emitted() {
        // proto2 fields carry explicit presence, so an absent one stays absent even here.

        // Arrange
        var parcel   = new Parcel { Reference = "REF-5" };
        var options  = new ProtobufJsonWriterOptions { EmitDefaults = true };
        var expected = Normalize(new JsonFormatter(
            JsonFormatter.Settings.Default
                .WithFormatDefaultValues(true)
                .WithTypeRegistry(TypeRegistry.FromFiles(TranscoderGroupsReflection.Descriptor))).Format(parcel));

        // Act
        var actual = Normalize(Write(parcel.ToByteArray(), options));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    static string Normalize(string json) => JsonNode.Parse(json)!.ToJsonString();
}
