using Kurrent.Toolkit;
using Kurrent.Surge.Schema.Governance;
using Kurrent.Surge.Codecs;
using Kurrent.Surge.Schema.Catalogs;
using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// The governed write chain: classify → govern → encode → frame, one composed chain. The
/// governed segment is lifted over the <see cref="WriteResolution"/> union, so raw-bytes
/// records pass straight through to the shared frame while catalog records run the full
/// segment. Validation is orchestration in <see cref="Execute"/> — it decides whether the
/// chain runs at all.
/// </summary>
sealed class WriteRecordPipeline : IPipelineStep<AppendRecord, Contracts.AppendRecord> {
    WriteRecordPipeline(IPipelineStep<AppendRecord, Contracts.AppendRecord> pipeline) => _pipeline = pipeline;

    readonly IPipelineStep<AppendRecord, Contracts.AppendRecord> _pipeline;

    /// <summary>
    /// Wires the canonical chain from client-composed parts.
    /// </summary>
    internal static WriteRecordPipeline Create(
        MessageResolver messages,
        KurrentClientSchemaOptions schema,
        SchemaCodecProvider codecs,
        ISchemaGovernance governance
    ) =>
        new(new ClassifyWriteStep(messages, schema.AppendAsJson)
            .Then(new GovernWriteStep(governance)
                .Then(new EncodeWriteStep(codecs))
                .OnNamed())
            .Then(new FrameWriteStep()));

    public async ValueTask<Contracts.AppendRecord> Execute(AppendRecord record, CancellationToken ct) {
        record.EnsureValid();

        return await _pipeline.Execute(record, ct).ConfigureAwait(false);
    }
}
