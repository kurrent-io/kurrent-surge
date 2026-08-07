using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using Google.Protobuf;
using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// Typestate carrier after catalog resolution: the source record and its schema name.
/// Bytes never enters this chain — the pipeline branches it before step one.
/// </summary>
readonly record struct NamedWrite(AppendRecord Source, SchemaName Schema);

/// <summary>
/// Typestate carrier after policy resolution — embeds the prior stage instead of re-carrying
/// its fields.
/// </summary>
readonly record struct PolicyResolvedWrite(NamedWrite Named, ResolvedSchemaRegistryPolicy Policy);

/// <summary>
/// Pure catalog lookup: type → schema name over the client's frozen snapshot. An unmapped
/// type throws here, with the pipeline on the stack instead of the serializer.
/// </summary>
sealed class ResolveNameWriteStep(MessageResolver messages) : IStep<AppendRecord, NamedWrite> {
    public ValueTask<NamedWrite> Execute(AppendRecord input, CancellationToken ct) =>
        new(new NamedWrite(input, messages.ResolveSchemaName(input.Value.GetType())));
}

/// <summary>
/// Policy resolution stays a per-record step: it is designed to consult the server's registry
/// settings (<c>SchemaRegistryPolicy.FromServerFeatures</c>, not implemented yet), so it cannot
/// be hoisted to pipeline construction.
/// </summary>
sealed class ResolvePolicyWriteStep(
    SchemaRegistryPolicy policy,
    IReadOnlyDictionary<SchemaDataFormat, SchemaRegistrationOptions> registrations
) : IStep<NamedWrite, PolicyResolvedWrite> {
    public ValueTask<PolicyResolvedWrite> Execute(NamedWrite input, CancellationToken ct) {
        var source = input.Source;

        var resolved = policy
            .Resolve(registrations[source.DataFormat], source.Stream, source.DataFormat)
            .EnsureDataFormatCompliance(source.DataFormat);

        return new(new PolicyResolvedWrite(input, resolved));
    }
}

/// <summary>
/// The one effectful write stage: registry governance per policy. Produces the resolved
/// schema for the encode/frame tail.
/// </summary>
sealed class GovernWriteStep(ISchemaGovernance governance) : IStep<PolicyResolvedWrite, ResolvedWrite> {
    public async ValueTask<ResolvedWrite> Execute(PolicyResolvedWrite input, CancellationToken ct) {
        var (named, policy) = input;
        var source          = named.Source;

        var versionId = await governance
            .EnsureRegistered(named.Schema, source.Value.GetType(), policy, ct)
            .ConfigureAwait(false);

        var schema = versionId == SchemaVersionId.Unset
            ? new RecordSchemaInfo { Schema = named.Schema, DataFormat = source.DataFormat }
            : new RecordSchemaInfo { Schema = named.Schema, DataFormat = source.DataFormat, VersionId = versionId };

        return new(source, schema);
    }
}

/// <summary>
/// Pure encode over an already-resolved schema through the format's codec.
/// </summary>
sealed class GovernedEncodeStep(SchemaCodecProvider codecs) : IStep<ResolvedWrite, EncodedWrite> {
    public ValueTask<EncodedWrite> Execute(ResolvedWrite input, CancellationToken ct) {
        var source = input.Source;

        try {
            var payload = source.Value is null
                ? ReadOnlyMemory<byte>.Empty
                : codecs.GetCodec(source.DataFormat).Encode(source.Value);

            return new(new EncodedWrite(source, input.Schema, payload));
        }
        // Mirrors the serializer base's wrap so the governed chain keeps the same error contract.
        catch (Exception ex) when (ex is not SchemaSerializationException) {
            throw new SchemaSerializationFailedException(source.DataFormat, source.Value!.GetType(), ex);
        }
    }
}

/// <summary>
/// Frames the wire message. <c>SchemaInfo.Id</c> is set only when the registry supplied a
/// version id — an absent id stays absent on the wire, never minted client-side.
/// </summary>
sealed class GovernedFrameStep : IStep<EncodedWrite, Contracts.AppendRecord> {
    public ValueTask<Contracts.AppendRecord> Execute(EncodedWrite input, CancellationToken ct) {
        var source = input.Source;

        // UnsafeWrap constructs a new ByteString from the given bytes. The bytes are not
        // copied, and must not be modified while the ByteString is in use.
        var contract = new Contracts.AppendRecord {
            // The wire expects the GUID string form — RecordId.ToString() is the ULID face.
            RecordId = source.RecordId.ToGuidString(),
            Data     = UnsafeByteOperations.UnsafeWrap(input.Payload),
            Schema   = new Contracts.SchemaInfo {
                Name   = source.SchemaName ?? input.Schema.Schema,
                Format = (Contracts.SchemaFormat)input.Schema.DataFormat
            },
            Stream   = source.Stream
        };

        if (input.Schema.HasSchemaId)
            contract.Schema.Id = input.Schema.VersionId.ToString();

        if (source.Metadata.Count > 0)
            source.Metadata.CopyToProperties(contract.Properties);

        return new(contract);
    }
}
