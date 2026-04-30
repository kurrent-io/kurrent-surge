using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;

namespace Kurrent.Processors.Gateway;

public interface ISubscriptionSource : IAsyncDisposable {
    // IAsyncEnumerable<SubscribeMessage> Subscribe(RecordPosition start, IRecordFilter filter, bool skipDecoding, CancellationToken ct);
    
    #region ->> Remote (server) <<-

    /// <summary>
    /// Loads the latest checkpoint, resolves the start position, opens the subscription
    /// stream from the server, and yields records and server-yielded barriers, fanned in
    /// with locally-injected barriers. Records are yielded with partitions already
    /// calculated; checkpoint markers carry positions snapshotted at injection time.
    /// </summary>
    IAsyncEnumerable<SubscribeMessage> Subscribe(CancellationToken ct);

    /// <summary>
    /// Sends a checkpoint receipt to the server: positions on the marker persisted
    /// under the marker's id. Drone calls this after the user's <c>OnCheckpoint</c>
    /// hook returns successfully.
    /// <para>
    /// For <see cref="CheckpointTrigger.Shutdown"/> markers this is also the drone's
    /// final goodbye to the gateway; impls may branch internally to send an outbound
    /// disconnect message when bidirectional gRPC lands.
    /// </para>
    /// </summary>
    ValueTask CompleteCheckpoint(
        Checkpoint         checkpoint,
        CancellationToken  ct = default);

    #endregion

    #region ->> Local (drone state) <<-

    /// <summary>
    /// Tells the local position tracker that progress was made on a record's partition.
    /// </summary>
    void Track(Record record);

    /// <summary>
    /// Tells the local position tracker that progress was made at the supplied position.
    /// </summary>
    void Track(RecordPosition position);

    /// <summary>
    /// Atomically tracks a batch of records. The tracker is updated under the same
    /// internal lock that <see cref="InjectCheckpoint"/> uses for its position snapshot,
    /// so a concurrent Shutdown injection cannot capture a partial-batch state.
    /// </summary>
    void Track(IEnumerable<Record> records);

    /// <summary>
    /// Injects a checkpoint barrier on the subscription channel so it lands in order
    /// with records. The marker carries trigger, reason, the source's clock-derived
    /// id, timestamp, and a positions snapshot taken at injection time.
    /// <para>
    /// For <see cref="CheckpointTrigger.Shutdown"/> the call is idempotent — only the
    /// first caller writes a marker — and the channel writer is completed atomically
    /// with the write so the iteration drains and exits.
    /// </para>
    /// </summary>
    ValueTask InjectCheckpoint(CheckpointTrigger trigger, string? reason = null);

    #endregion
}
