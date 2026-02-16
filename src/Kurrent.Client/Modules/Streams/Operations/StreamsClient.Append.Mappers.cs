// ReSharper disable MemberCanBeMadeStatic.Global

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using DotNext.Collections.Generic;
using Google.Protobuf;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;

using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Client.Streams;

[SuppressMessage("Performance", "CA1822:Mark members as static")]
partial class StreamsProtocolMapper {
    public async ValueTask<Contracts.AppendRecordsRequest> ToContract(AppendRecordsRequest source, CancellationToken ct) {
        var result = new Contracts.AppendRecordsRequest {
            Records = { Capacity = source.Records.Length }
        };

        foreach (var record in source.Records)
            result.Records.Add(await ToContract(record, ct).ConfigureAwait(false));

        foreach (var check in source.Checks)
            result.ConsistencyChecks.Add(ToContract(check));

        return result;
    }

    public async ValueTask<Contracts.AppendRecord> ToContract(AppendRecord source, CancellationToken ct) {
        try {
            var context = new SchemaSerializationContext {
                Stream               = source.Stream,
                RecordSchema         = new RecordSchemaInfo { SchemaFormat = source.DataFormat },
                SchemaRegistryPolicy = RegistryPolicy,
                CancellationToken    = ct
            };

            var data = await SerializerProvider
                .GetSerializer(source.DataFormat)
                .Serialize(source.Value, context)
                .ConfigureAwait(false);

            var schema = new Contracts.SchemaInfo {
                Id     = context.RecordSchema.SchemaVersionId,
                Name   = context.RecordSchema.SchemaName,
                Format = (Contracts.SchemaFormat)context.RecordSchema.SchemaFormat
            };

            // UnsafeWrap constructs a new ByteString from the given bytes.
            // The bytes are not copied, and must not be modified while the
            // ByteString is in use.
            return new Contracts.AppendRecord {
                RecordId   = source.RecordId.ToString(),
                Data       = UnsafeByteOperations.UnsafeWrap(data),
                Schema     = schema,
                Properties = { source.Metadata.ToProperties() },
                Stream     = source.Stream
            };
        }
        catch (Exception ex) {
            throw new ArgumentException($"Failed to map AppendRecord for stream '{source.Stream}'", ex);
        }
    }

    public Contracts.ConsistencyCheck ToContract(ConsistencyCheck source) {
        var result = new Contracts.ConsistencyCheck();

        switch (source.Value) {
            case RevisionConsistencyCheck check:
                result.Revision = new Contracts.StreamRevision {
                    Stream   = check.Stream,
                    Revision = check.Revision
                };
                break;
            default:
                throw new ArgumentException($"Unsupported consistency check type: {source.Value.GetType().Name}");
        }

        return result;
    }

    public AppendRecordsSuccess ToModel(Contracts.AppendRecordsResponse source) {
        var revisions = new (string Stream, long Revision)[source.Revisions.Count];
        revisions.AddAll(source.Revisions.Select(x => (x.Stream, x.Revision)));
        return new(revisions, source.Position);
    }
}

static partial class StreamsClientV2Mapper {
    #region . Old MSA .

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
                RecordSchema         = new RecordSchemaInfo { SchemaFormat = source.DataFormat },
                SchemaRegistryPolicy = SchemaRegistryPolicy.NoRequirements,
                CancellationToken    = ct
            };

            var data = await serializerProvider
                .GetSerializer(source.DataFormat)
                .Serialize(source.Value, context)
                .ConfigureAwait(false);

            var schemaInfo = new Contracts.SchemaInfo {
                Name   = context.RecordSchema.SchemaName,
                Format = (Contracts.SchemaFormat)source.DataFormat
            };

            // UnsafeWrap constructs a new ByteString from the given bytes.
            // The bytes are not copied, and must not be modified while the
            // ByteString is in use.
            return new Contracts.AppendRecord {
                RecordId   = source.RecordId.ToString(),
                Data       = UnsafeByteOperations.UnsafeWrap(data),
                Schema     = schemaInfo,
                Properties = { source.Metadata.ToProperties() }
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


    #endregion
}
