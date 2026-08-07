using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using Kurrent.Validation;
using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// The record write path as a composed step chain: validate → encode → frame. The constructor
/// wires the canonical sequence; replace <see cref="Pipeline"/> at construction to insert or
/// substitute stages. Produces the same wire message as the <c>ToContract</c> mapping, which
/// stays untouched as the comparison baseline.
/// </summary>
sealed class WriteRecordPipeline : IStep<AppendRecord, Contracts.AppendRecord> {
    public WriteRecordPipeline(ISchemaSerializerProvider serializerProvider, SchemaRegistryPolicy policy) =>
        Pipeline = new ValidateWriteStep()
            .Then(new EncodeWriteStep(serializerProvider, policy))
            .Then(new FrameWriteStep());

    public IStep<AppendRecord, Contracts.AppendRecord> Pipeline { get; init; }

    /// <summary>
    /// The split chain: schema resolution runs before encoding, cached per (type, format), and
    /// the encoder invokes the serializer's synchronous core. Same wire output as the canonical
    /// chain — the resolve/encode fusion is undone.
    /// </summary>
    public static WriteRecordPipeline Split(ISchemaSerializerProvider serializerProvider, SchemaRegistryPolicy policy) =>
        new(serializerProvider, policy) {
            Pipeline = new ValidateWriteStep()
                .Then(new ResolveWriteStep(SchemaResolver.From(serializerProvider, policy)))
                .Then(new EncodeResolvedWriteStep(serializerProvider))
                .Then(new FrameWriteStep())
        };

    public async ValueTask<Contracts.AppendRecord> Execute(AppendRecord record, CancellationToken ct) {
        try {
            return await Pipeline.Execute(record, ct).ConfigureAwait(false);
        }
        // Validation speaks for itself — the wrap preserves the mapping contract for the rest.
        catch (Exception ex) when (ex is not ValidationException) {
            throw new ArgumentException($"Failed to map AppendRecord for stream '{record.Stream}'", ex);
        }
    }
}
