using System.Collections.Frozen;
using Kurrent.Surge.Codecs.Avro;
using Kurrent.Surge.Codecs.Json;
using Kurrent.Surge.Codecs.Protobuf;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Codecs;

/// <summary>
/// The built-in codec set, matching what a client registers when no custom codec is supplied.
/// </summary>
/// <remarks>
/// A client builds its own <see cref="SchemaCodecProvider"/> so user-registered codecs and JSON
/// options take effect. This set exists for the paths that have no client to ask.
/// </remarks>
static class DefaultSchemaCodecs {
    static readonly FrozenDictionary<SchemaDataFormat, ISchemaCodec> Codecs =
        new Dictionary<SchemaDataFormat, ISchemaCodec> {
            [SchemaDataFormat.Json]     = new JsonSchemaCodec(),
            [SchemaDataFormat.Protobuf] = new ProtobufSchemaCodec(),
            [SchemaDataFormat.Avro]     = new AvroSchemaCodec()
        }.ToFrozenDictionary();

    public static ISchemaCodec GetCodec(SchemaDataFormat dataFormat) =>
        Codecs.TryGetValue(dataFormat, out var codec)
            ? codec
            : throw new SchemaCodecNotFoundException(dataFormat, [.. Codecs.Keys]);
}
