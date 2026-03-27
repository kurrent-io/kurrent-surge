// ReSharper disable MemberCanBeMadeStatic.Global

using System.Diagnostics.CodeAnalysis;
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
            if (check != ConsistencyCheck.None)
                result.Checks.Add(ToContract(check));

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

            var schemaVersionId = context.RecordSchema.HasSchemaId
                ? context.RecordSchema.SchemaVersionId.ToString()
                : Guid.NewGuid().ToString();

            var schema = new Contracts.SchemaInfo {
                Id     = schemaVersionId,
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
        var result = new Contracts.ConsistencyCheck {
            StreamState = source.Value switch {
                StreamStateCheck check => new Contracts.ConsistencyCheck.Types.StreamStateCheck { Stream = check.Stream, ExpectedState = check.ExpectedState.ToWireValue() },
                _                      => throw new ArgumentException($"Unsupported consistency check type: {source.Value.GetType().Name}")
            }
        };

        return result;
    }

    public async ValueTask<Contracts.AppendRecordsRequest> ToContract(AppendToStreamRequest source, CancellationToken ct) {
        var result = new Contracts.AppendRecordsRequest {
            Records = { Capacity = source.Records.Length }
        };

        foreach (var record in source.Records) {
            var stamped = record with { Stream = source.Stream };
            result.Records.Add(await ToContract(stamped, ct).ConfigureAwait(false));
        }

        foreach (var check in source.Checks)
            if (check != ConsistencyCheck.None)
                result.Checks.Add(ToContract(check));

        return result;
    }

    public AppendToStreamResponse ToAppendToStreamResponse(Contracts.AppendRecordsResponse source, StreamName stream) {
        var revision = source.Revisions
            .Where(r => (StreamName)r.Stream == stream)
            .Select(r => (Revision)r.Revision)
            .LastOrDefault(Revision.Unset);

        return new AppendToStreamResponse(stream, revision, source.Position);
    }

    public AppendRecordsResponse ToAppendRecordsResponse(Contracts.AppendRecordsResponse source) {
        var revisions = source.Revisions
            .Select(x => new StreamRevision(x.Stream, x.Revision))
            .ToArray();

        return new AppendRecordsResponse(revisions, source.Position);
    }

    public AppendRecordsSuccess ToModel(Contracts.AppendRecordsResponse source) {
        var revisions = source.Revisions
            .Select(x => new StreamRevision(x.Stream, x.Revision))
            .ToArray();

        return new(revisions, source.Position);
    }
}
