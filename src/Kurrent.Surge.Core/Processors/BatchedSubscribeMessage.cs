using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Variant;

namespace Kurrent.Surge.Streaming;

/// <summary>
/// Sum yielded by <c>ReadBatched</c> on the drone's batched path. Records are
/// accumulated into <see cref="RecordBatch"/>; <see cref="Connected"/> and
/// <see cref="Checkpoint"/> messages flush the pending batch and pass through.
/// </summary>
[PublicAPI]
public readonly partial record struct BatchedSubscribeMessage : IVariant<Connected, RecordBatch, Checkpoint> {
    public static readonly BatchedSubscribeMessage None;
}
