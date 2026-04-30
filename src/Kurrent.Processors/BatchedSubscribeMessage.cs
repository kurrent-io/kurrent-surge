using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Processors.Gateway;
using Kurrent.Variant;

namespace Kurrent.Processors;

/// <summary>
/// Sum yielded by <c>ReadBatched</c> on the drone's batched path. Records are
/// accumulated into <see cref="RecordBatch"/>; <see cref="Connected"/> and
/// <see cref="Checkpoint"/> messages flush the pending batch and pass through.
/// </summary>
[PublicAPI]
public readonly partial record struct BatchedSubscribeMessage : IVariant<Connected, RecordBatch, Checkpoint> {
    public static readonly BatchedSubscribeMessage None;
}
