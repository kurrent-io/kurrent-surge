using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProtobufTranscoder.Dynamic;
using ProtobufTranscoder.Json;

namespace ProtobufTranscoder;

/// <summary>
/// Injectable, long-lived service for decoding protobuf bytes.
/// Caches compiled <see cref="ProtoDefinitions"/> by schema version.
/// </summary>
public sealed class ProtoTranscoder(IProtoSchemaSource source)
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    private record CacheEntry(ProtoDefinitions Definitions, string MessageTypeName, string Version);

    private (ProtoDefinitions Definitions, string MessageTypeName) ResolveAndCache(string schemaId)
    {
        var info = source.Resolve(schemaId);

        if (_cache.TryGetValue(schemaId, out var cached) && cached.Version == info.Version)
            return (cached.Definitions, cached.MessageTypeName);

        var definitions = ProtoDefinitions.Build(info.Schemas);
        var entry = new CacheEntry(definitions, info.MessageTypeName, info.Version);
        _cache[schemaId] = entry;
        return (definitions, info.MessageTypeName);
    }

    public DynamicMessage ToDynamicMessage(ReadOnlySpan<byte> bytes, string schemaId)
    {
        var (definitions, messageTypeName) = ResolveAndCache(schemaId);
        return ProtoTranscode.ToDynamicMessage(bytes, definitions, messageTypeName);
    }

    public byte[] ToJson(ReadOnlySpan<byte> bytes, string schemaId, JsonSerializeOptions? options = null)
    {
        var (definitions, messageTypeName) = ResolveAndCache(schemaId);
        return ProtoTranscode.ToJson(bytes, definitions, messageTypeName, options);
    }

    public void ToJson(ReadOnlySpan<byte> bytes, string schemaId, IBufferWriter<byte> output, JsonSerializeOptions? options = null)
    {
        var (definitions, messageTypeName) = ResolveAndCache(schemaId);
        ProtoTranscode.ToJson(bytes, definitions, messageTypeName, output, options);
    }

    public JsonDocument ToJsonDocument(ReadOnlySpan<byte> bytes, string schemaId, JsonSerializeOptions? options = null)
    {
        var (definitions, messageTypeName) = ResolveAndCache(schemaId);
        return ProtoTranscode.ToJsonDocument(bytes, definitions, messageTypeName, options);
    }

    public JsonNode ToJsonNode(ReadOnlySpan<byte> bytes, string schemaId, JsonSerializeOptions? options = null)
    {
        var (definitions, messageTypeName) = ResolveAndCache(schemaId);
        return ProtoTranscode.ToJsonNode(bytes, definitions, messageTypeName, options);
    }
}
