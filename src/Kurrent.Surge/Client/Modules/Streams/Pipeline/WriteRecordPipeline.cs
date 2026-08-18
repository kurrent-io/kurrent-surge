using Kurrent.Surge.Client.Schema.Governance;
using Kurrent.Surge.Codecs;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Validation;
using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// The governed write chain: classify → govern → encode → frame, one composed chain. The
/// governed segment is lifted over the <see cref="WriteResolution"/> union, so raw-bytes
/// records pass straight through to the shared frame while catalog records run the full
/// segment. Validation is orchestration in <see cref="Execute"/> — it decides whether the
/// chain runs at all.
/// </summary>
sealed class WriteRecordPipeline : IStep<AppendRecord, Contracts.AppendRecord> {
    WriteRecordPipeline(IStep<AppendRecord, Contracts.AppendRecord> pipeline) => _pipeline = pipeline;

    readonly IStep<AppendRecord, Contracts.AppendRecord> _pipeline;

    /// <summary>
    /// Wires the canonical chain from client-composed parts.
    /// </summary>
    internal static WriteRecordPipeline Create(
        MessageResolver messages,
        SchemaCodecProvider codecs,
        ISchemaGovernance governance
    ) =>
        new(new ClassifyWriteStep(messages)
            .Then(new GovernWriteStep(governance)
                .Then(new EncodeWriteStep(codecs))
                .OnNamed())
            .Then(new FrameWriteStep()));

    public async ValueTask<Contracts.AppendRecord> Execute(AppendRecord record, CancellationToken ct) {
        record.EnsureValid();

        return await _pipeline.Execute(record, ct).ConfigureAwait(false);
    }
}
