using Google.Protobuf;
using Kurrent.Surge.Client.Schema.Serialization;

using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Surge.Client.Streams;

static class AppendRecordsMapping {
    extension(AppendRecord source) {
        public async ValueTask<Contracts.AppendRecord> ToContract(MappingContext ctx) {
            try {
                var context = new SchemaSerializationContext {
                    Stream               = source.Stream,
                    RecordSchema         = new() { DataFormat = source.DataFormat },
                    SchemaRegistryPolicy = ctx.Policy,
                    CancellationToken    = ctx.CancellationToken
                };

                var data = await ctx.SerializerProvider
                    .GetSerializer(source.DataFormat)
                    .Serialize(source.Value, context)
                    .ConfigureAwait(false);

                var schemaVersionId = context.RecordSchema.HasSchemaId
                    ? context.RecordSchema.VersionId.ToString()
                    : Guid.NewGuid().ToString();

                var schema = new Contracts.SchemaInfo {
                    Id     = schemaVersionId,
                    Name   = source.SchemaName ?? context.RecordSchema.Schema,
                    Format = (Contracts.SchemaFormat)context.RecordSchema.DataFormat
                };

                // UnsafeWrap constructs a new ByteString from the given bytes.
                // The bytes are not copied, and must not be modified while the
                // ByteString is in use.
                var contract = new Contracts.AppendRecord {
                    // The wire expects the GUID string form — RecordId.ToString() is the ULID face.
                    RecordId = source.RecordId.ToGuidString(),
                    Data     = UnsafeByteOperations.UnsafeWrap(data),
                    Schema   = schema,
                    Stream   = source.Stream
                };

                // Empty metadata skips the properties path entirely; otherwise write straight
                // into the message's own map instead of building a throwaway one.
                if (source.Metadata.Count > 0)
                    source.Metadata.CopyToProperties(contract.Properties);

                return contract;
            }
            catch (Exception ex) {
                throw new ArgumentException($"Failed to map AppendRecord for stream '{source.Stream}'", ex);
            }
        }
    }

    extension(ConsistencyCheck source) {
        public Contracts.ConsistencyCheck ToContract() =>
            new() {
                StreamState = source.Value switch {
                    StreamStateCheck check => new() { Stream = check.Stream, ExpectedState = check.ExpectedState.ToWireValue() },
                    _                      => throw new ArgumentException($"Unsupported consistency check type: {source.Value.GetType().Name}")
                }
            };
    }

    extension(Contracts.AppendRecordsResponse source) {
        public AppendRecordsResponse ToResponse() {
            var revisions = source.Revisions
                .Select(x => new StreamPosition(x.Stream, x.Revision))
                .ToArray();

            return new(revisions, source.Position);
        }
    }
}
