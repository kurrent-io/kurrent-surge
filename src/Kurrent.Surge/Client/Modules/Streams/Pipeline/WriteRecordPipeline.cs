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
    WriteRecordPipeline(IStep<AppendRecord, Contracts.AppendRecord> pipeline) => Pipeline = pipeline;

    public IStep<AppendRecord, Contracts.AppendRecord> Pipeline { get; init; }

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
        try {
            record.EnsureValid();

            return await Pipeline.Execute(record, ct).ConfigureAwait(false);
        }
        // Validation speaks for itself, cancellation must surface as cancellation, and a
        // failed catalog walk surfaces bare so both pipelines share one exception contract —
        // the wrap preserves the mapping contract for the rest.
        catch (Exception ex) when (ex is not ValidationException and not OperationCanceledException and not CatalogRegistrationFailedException) {
            throw new ArgumentException($"Failed to map AppendRecord for stream '{record.Stream}'", ex);
        }
    }
}
