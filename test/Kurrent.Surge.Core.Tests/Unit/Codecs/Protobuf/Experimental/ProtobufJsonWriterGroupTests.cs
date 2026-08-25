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


    static string Normalize(string json) => JsonNode.Parse(json)!.ToJsonString();
}
