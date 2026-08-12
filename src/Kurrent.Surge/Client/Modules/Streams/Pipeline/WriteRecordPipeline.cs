using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Governance;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Schema;
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
        SchemaRegistryPolicy policy,
        IReadOnlyDictionary<SchemaDataFormat, SchemaRegistrationOptions> registrations,
        SchemaCodecProvider codecs,
        CatalogValidationInterceptor validation
    ) =>
        new(new ClassifyWriteStep(messages)
            .Then(new GovernWriteStep(validation, policy, registrations)
                .Then(new EncodeWriteStep(codecs))
                .OnNamed())
            .Then(new FrameWriteStep()));

    public async ValueTask<Contracts.AppendRecord> Execute(AppendRecord record, CancellationToken ct) {
        try {
            record.EnsureValid();

            return await Pipeline.Execute(record, ct).ConfigureAwait(false);
        }
        // Validation speaks for itself, and cancellation must surface as cancellation —
        // the wrap preserves the mapping contract for the rest.
        catch (Exception ex) when (ex is not ValidationException and not OperationCanceledException) {
            throw new ArgumentException($"Failed to map AppendRecord for stream '{record.Stream}'", ex);
        }
    }
}
