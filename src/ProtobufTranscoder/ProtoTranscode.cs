using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProtobufTranscoder.Dynamic;
using ProtobufTranscoder.Json;

namespace ProtobufTranscoder;

public static class ProtoTranscode
{
    private static readonly DynamicMessageJsonSerializer DefaultSerializer = new();

    public static DynamicMessage ToDynamicMessage(
        ReadOnlySpan<byte> bytes, ProtoDefinitions definitions, string messageTypeName)
    {
        var descriptor = definitions.FindMessageType(messageTypeName);
        return DynamicMessage.Decode(descriptor, bytes);
    }

    public static byte[] ToJson(
        ReadOnlySpan<byte> bytes, ProtoDefinitions definitions, string messageTypeName,
        JsonSerializeOptions? options = null)
    {
        var message = ToDynamicMessage(bytes, definitions, messageTypeName);
        return GetSerializer(options).Write(message);
    }

    public static void ToJson(
        ReadOnlySpan<byte> bytes, ProtoDefinitions definitions, string messageTypeName,
        IBufferWriter<byte> output, JsonSerializeOptions? options = null)
    {
        var message = ToDynamicMessage(bytes, definitions, messageTypeName);
        GetSerializer(options).Write(message, output);
    }

    public static JsonDocument ToJsonDocument(
        ReadOnlySpan<byte> bytes, ProtoDefinitions definitions, string messageTypeName,
        JsonSerializeOptions? options = null)
    {
        var message = ToDynamicMessage(bytes, definitions, messageTypeName);
        return JsonDocument.Parse(GetSerializer(options).WriteToMemory(message));
    }

    public static JsonNode ToJsonNode(
        ReadOnlySpan<byte> bytes, ProtoDefinitions definitions, string messageTypeName,
        JsonSerializeOptions? options = null)
    {
        var message = ToDynamicMessage(bytes, definitions, messageTypeName);
        return JsonNode.Parse(GetSerializer(options).WriteToMemory(message).Span)!;
    }

    private static DynamicMessageJsonSerializer GetSerializer(JsonSerializeOptions? options) =>
        options is null ? DefaultSerializer : new DynamicMessageJsonSerializer(options);
}
