using System.Threading.Channels;
using Kurrent.Surge.Client.Streams;
using Kurrent.Variant;

namespace Kurrent.Surge.Streaming.Gateway;

public readonly partial record struct DataSourceMessage : IVariant<Connected, Record, Checkpoint> {
    public static readonly DataSourceMessage None;
}

public interface IDataSource {
    /// <summary>
    /// Feeds records and barriers into the channel: subscribes from the least-advanced of
    /// <paramref name="startPositions"/>, assigns partitions, and prunes the caught-up prefix against the
    /// per-partition floors those positions carry. The start positions come from the checkpointer, which
    /// owns resolution; the source owns only the feed.
    /// </summary>
    /// <remarks>
    /// The channel element is <see cref="DataSourceMessage"/> — the engine's own message type, so the drone and
    /// checkpointer do not depend on the client's <see cref="SubscribeMessage"/>. The concrete source
    /// (<c>KdbDataSource</c>) bridges the client's <see cref="SubscribeMessage"/> stream into
    /// <see cref="DataSourceMessage"/> via <c>Unsafe.BitCast</c> — the two variants share an identical layout,
    /// guarded by <c>VariantLayoutCompatibilityTests</c>. The checkpointer is not yet decoupled; it writes its
    /// barriers into this same channel as <see cref="DataSourceMessage"/>.
    /// </remarks>
    Task Connect(ChannelWriter<DataSourceMessage> writer, RecordPosition[] startPositions, CancellationToken ct);
}
