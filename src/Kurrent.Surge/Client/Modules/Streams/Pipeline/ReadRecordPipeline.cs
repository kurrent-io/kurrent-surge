using System.Text.Json.Nodes;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Governance;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Schema.Serialization.Json;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// The read chain over the decode stage, exposed as <see cref="IRecordDecoder"/>. One composed
/// chain: the decodable segment is lifted over the <see cref="TypeResolution"/> union, so
/// terminal resolutions exit mid-chain without breaking the composition. Guards are
/// orchestration in <see cref="Execute"/> — they decide whether the chain runs at all.
/// </summary>
sealed class ReadRecordPipeline : IStep<Record, DecodeOutcome>, IRecordDecoder {
    ReadRecordPipeline(IStep<Record, DecodeOutcome> pipeline) => Pipeline = pipeline;

    public IStep<Record, DecodeOutcome> Pipeline { get; init; }

    /// <summary>
    /// Wires the canonical chain from client-composed parts.
    /// </summary>
    internal static ReadRecordPipeline Create(
        MessageResolver messages,
        SchemaRegistryPolicy policy,
        IReadOnlyDictionary<SchemaDataFormat, SchemaRegistrationOptions> registrations,
        SchemaCodecProvider codecs,
        ISchemaGovernance governance
    ) =>
        new(new ResolvePolicyReadStep(policy, registrations)
            .Then(new ResolveTypeReadStep(messages))
            .Then(new GovernReadStep(governance)
                .Then(new DecodeReadStep(codecs))
                .OnDecodable()));

    public ValueTask<DecodeOutcome> TryDecode(Record record, CancellationToken ct) =>
        Execute(record, ct);

    public JsonNode? ProjectToJson(Record record) =>
        record.IsDecoded && record.Schema.DataFormat == SchemaDataFormat.Json
            ? JsonSerializer.SerializeToNode(record.Value, record.ValueType, KurrentJsonContext.Default)
            : null;

    public async ValueTask<DecodeOutcome> Execute(Record record, CancellationToken ct) {
        // Guards are orchestration, not stages: they decide whether the chain runs at all.
        if (record.IsDecoded)
            return DecodeOutcome.Decoded;

        if (record.Data.IsEmpty)
            return DecodeOutcome.NoData;

        if (record.Schema.DataFormat == SchemaDataFormat.Bytes) {
            // Wire hazard carried over from the bytes serializer this pipeline replaced: a $>
            // link record's Bytes payload is "{revision}@{stream}" text, not opaque bytes, and
            // decodes into a LinkTo value. Every other Bytes record has nothing to decode.
            if (record.Schema.Schema != LinkTo.SchemaName)
                return DecodeOutcome.Unavailable;

            try {
                var linkTo = LinkTo.Parse(record.Data.Span);
                record.Value     = linkTo;
                record.ValueType = linkTo.GetType();
                return DecodeOutcome.Decoded;
            }
            catch (Exception ex) when (ex is not SchemaSerializationException) {
                throw new SchemaDeserializationFailedException(record.Schema.DataFormat, record.Schema.Schema, ex);
            }
        }

        return await Pipeline.Execute(record, ct).ConfigureAwait(false);
    }
}
