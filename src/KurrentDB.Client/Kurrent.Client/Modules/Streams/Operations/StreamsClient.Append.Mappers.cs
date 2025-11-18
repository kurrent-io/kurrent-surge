using System.Runtime.CompilerServices;
using Google.Protobuf;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;
using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Client.Streams;

static partial class StreamsClientV2Mapper {
    public static async IAsyncEnumerable<Contracts.AppendRecord> Map(
        this IEnumerable<AppendRecord> source,
        string stream,
        ISchemaSerializerProvider serializerProvider,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        foreach (var message in source)
            yield return await message
                .Map(stream, serializerProvider, ct)
                .ConfigureAwait(false);
    }

    public static async ValueTask<Contracts.AppendRecord> Map(
        this AppendRecord source,
        string stream,
        ISchemaSerializerProvider serializerProvider,
        CancellationToken ct
    ) {
        try {
            var context = new SchemaSerializationContext {
                Stream               = stream,
                Metadata             = source.Metadata,
                SchemaRegistryPolicy = SchemaRegistryPolicy.NoRequirements,
                CancellationToken    = ct
            };

            var data = await serializerProvider
                .GetSerializer(source.DataFormat)
                .Serialize(source.Value, context)
                .ConfigureAwait(false);

            var schemaInfo = new Contracts.SchemaInfo {
                Name   = context.Metadata.GetSchemaName(),
                Format = (Contracts.SchemaFormat)source.DataFormat
            };

            // TODO SS: rethink/consider how to return schema info from serializer base without mutating metadata
            context.Metadata.Remove(SystemMetadataKeys.SchemaName);
            context.Metadata.Remove(SystemMetadataKeys.SchemaFormat);

            // UnsafeWrap constructs a new ByteString from the given bytes.
            // The bytes are not copied, and must not be modified while the
            // ByteString is in use.
            return new Contracts.AppendRecord {
                RecordId   = source.RecordId.ToString(),
                Data       = UnsafeByteOperations.UnsafeWrap(data),
                Schema     = schemaInfo,
                Properties = { source.Metadata.MapToMapField() }
            };
        }
        catch (Exception ex) {
            throw new ArgumentException($"Failed to map AppendRecord for stream '{stream}'", ex);
        }
    }

    public static AppendStreamSuccess Map(this Contracts.AppendResponse source) =>
        new(source.Stream, source.Position, source.StreamRevision);

    public static AppendSessionSuccess Map(this Contracts.AppendSessionResponse source) =>
        new(source.Output.Select(success => {
            if (!success.HasPosition)
                success.Position = source.Position;
            return success.Map();
        }).ToList(), source.Position);
}
