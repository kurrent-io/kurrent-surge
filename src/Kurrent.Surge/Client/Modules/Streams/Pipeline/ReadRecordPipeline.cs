using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Governance;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Experimental;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// The governed read chain over the decode stage — same scope as <c>RecordDecoder.TryDecode</c>,
/// which stays untouched as the comparison baseline. One composed chain: the decodable segment
/// is lifted over the <see cref="TypeResolution"/> union, so terminal resolutions exit mid-chain
/// without breaking the composition. Guards are orchestration in <see cref="Execute"/> — they
/// decide whether the chain runs at all.
/// </summary>
sealed class ReadRecordPipeline : IStep<Record, DecodeOutcome> {
    ReadRecordPipeline(IStep<Record, DecodeOutcome> pipeline) => Pipeline = pipeline;

    public IStep<Record, DecodeOutcome> Pipeline { get; init; }

    /// <summary>
    /// Wires the canonical chain. Passing no governance falls back to the
    /// <see cref="SchemaManagerGovernanceAdapter"/> over the client's live manager.
    /// </summary>
    public static ReadRecordPipeline From(ISchemaSerializerProvider serializerProvider, SchemaRegistryPolicy policy, ISchemaGovernance? governance = null) {
        var (manager, messages, registrations) = SchemaManagerGovernanceAdapter.Extract(serializerProvider);

        var codecs = SerializerCoreCodecs.From(serializerProvider);

        return new(new ResolvePolicyReadStep(policy, registrations)
            .Then(new ResolveTypeReadStep(messages))
            .Then(new GovernReadStep(governance ?? new SchemaManagerGovernanceAdapter(manager))
                .Then(new DecodeReadStep(codecs))
                .OnDecodable()));
    }

    public async ValueTask<DecodeOutcome> Execute(Record record, CancellationToken ct) {
        // Guards are orchestration, not stages: they decide whether the chain runs at all.
        if (record.IsDecoded)
            return DecodeOutcome.Decoded;

        if (record.Data.IsEmpty)
            return DecodeOutcome.NoData;

        // Data already exposes the raw payload; decoding is not applicable — Unavailable.
        if (record.Schema.DataFormat == SchemaDataFormat.Bytes)
            return DecodeOutcome.Unavailable;

        return await Pipeline.Execute(record, ct).ConfigureAwait(false);
    }
}
