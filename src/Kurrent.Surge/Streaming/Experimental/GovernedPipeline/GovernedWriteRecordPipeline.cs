using System.Buffers;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Validation;
using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// The governed write chain: resolve name → resolve policy → govern → encode → frame. Pure
/// steps consult the client's frozen catalog snapshot; <see cref="ISchemaGovernance"/> is the
/// single effectful stage. Validation and the Bytes branch are orchestration in
/// <see cref="Execute"/>, not stages — they decide whether the chain runs. Produces the same
/// wire message as the <c>ToContract</c> mapping, which stays untouched as the baseline.
/// </summary>
sealed class GovernedWriteRecordPipeline : IStep<AppendRecord, Contracts.AppendRecord> {
    GovernedWriteRecordPipeline(IStep<AppendRecord, Contracts.AppendRecord> pipeline, GovernedFrameStep frame) {
        Pipeline = pipeline;
        Frame    = frame;
    }

    public IStep<AppendRecord, Contracts.AppendRecord> Pipeline { get; init; }

    GovernedFrameStep Frame { get; }

    /// <summary>
    /// Wires the canonical chain. Passing no governance falls back to the
    /// <see cref="SchemaManagerGovernanceAdapter"/> over the client's live manager.
    /// </summary>
    public static GovernedWriteRecordPipeline From(ISchemaSerializerProvider serializerProvider, SchemaRegistryPolicy policy, ISchemaGovernance? governance = null) {
        var (manager, messages, registrations) = SchemaManagerGovernanceAdapter.Extract(serializerProvider);

        var codecs = SchemaCodecProvider.From(serializerProvider);
        var frame  = new GovernedFrameStep();

        var pipeline = new ResolveNameWriteStep(messages)
            .Then(new ResolvePolicyWriteStep(policy, registrations))
            .Then(new GovernWriteStep(governance ?? new SchemaManagerGovernanceAdapter(manager)))
            .Then(new GovernedEncodeStep(codecs))
            .Then(frame);

        return new(pipeline, frame);
    }

    public async ValueTask<Contracts.AppendRecord> Execute(AppendRecord record, CancellationToken ct) {
        try {
            record.EnsureValid();

            // Bytes is a branch, not a codec: no name to resolve, no governance to consult.
            if (record.DataFormat == SchemaDataFormat.Bytes) {
                var encoded = new EncodedWrite(record, new RecordSchemaInfo { DataFormat = SchemaDataFormat.Bytes }, UnwrapBytes(record.Value));
                return await Frame.Execute(encoded, ct).ConfigureAwait(false);
            }

            return await Pipeline.Execute(record, ct).ConfigureAwait(false);
        }
        // Validation speaks for itself — the wrap preserves the mapping contract for the rest.
        catch (Exception ex) when (ex is not ValidationException) {
            throw new ArgumentException($"Failed to map AppendRecord for stream '{record.Stream}'", ex);
        }
    }

    // Kept in sync with BytesPassthroughSerializer.Serialize.
    static ReadOnlyMemory<byte> UnwrapBytes(object? value) =>
        value switch {
            byte[] bytes               => bytes,
            ReadOnlyMemory<byte> bytes => bytes,
            Memory<byte> bytes         => bytes,
            ReadOnlySequence<byte> seq => seq.IsSingleSegment ? seq.First : seq.ToArray(),
            ArraySegment<byte> seg     => seg.AsMemory(),
            null                       => ReadOnlyMemory<byte>.Empty,
            _                          => throw new InvalidOperationException($"Bytes format requires a binary payload, got '{value.GetType().Name}'.")
        };
}
