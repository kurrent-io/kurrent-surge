using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// The governed read chain over the decode stage — same scope as <c>RecordDecoder.TryDecode</c>,
/// which stays untouched as the comparison baseline. Guards and the resolution branch are
/// orchestration in <see cref="Execute"/>; the two linear segments between them are pure step
/// chains joined by the <see cref="TypeResolution"/> union.
/// </summary>
sealed class GovernedReadRecordPipeline : IStep<Record, DecodeOutcome> {
    GovernedReadRecordPipeline(IStep<Record, TypeResolution> resolution, IStep<DecodableRead, DecodeOutcome> decoding) {
        Resolution = resolution;
        Decoding   = decoding;
    }

    public IStep<Record, TypeResolution> Resolution { get; init; }

    public IStep<DecodableRead, DecodeOutcome> Decoding { get; init; }

    /// <summary>
    /// Wires the canonical segments. Passing no governance falls back to the
    /// <see cref="SchemaManagerGovernanceAdapter"/> over the client's live manager.
    /// </summary>
    public static GovernedReadRecordPipeline From(ISchemaSerializerProvider serializerProvider, SchemaRegistryPolicy policy, ISchemaGovernance? governance = null) {
        var (manager, messages, registrations) = SchemaManagerGovernanceAdapter.Extract(serializerProvider);

        var codecs = SchemaCodecProvider.From(serializerProvider);

        return new(
            new ResolvePolicyReadStep(policy, registrations).Then(new ResolveTypeReadStep(messages)),
            new GovernReadStep(governance ?? new SchemaManagerGovernanceAdapter(manager)).Then(new GovernedDecodeStep(codecs))
        );
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

        var resolution = await Resolution.Execute(record, ct).ConfigureAwait(false);

        return await resolution.Match(
            decodable => Decoding.Execute(decodable, ct),
            terminal => new ValueTask<DecodeOutcome>(terminal.Outcome)
        ).ConfigureAwait(false);
    }
}
