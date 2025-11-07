#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

namespace Kurrent.Client.Streams.Checkpoints;

/// <summary>
/// Defines the interface for a consumer checkpoint store.
/// </summary>
public interface ICheckpointStore {
    /// <summary>
    /// Initializes the checkpoint store. This method should be called before using the store.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    ValueTask Initialize(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>
    /// Loads the positions from the checkpoint store.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous load operation, containing the loaded positions.</returns>
    Task<IReadOnlyList<RecordPosition>> LoadPositions(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the given positions to the checkpoint store.
    /// </summary>
    /// <param name="positions">The positions to commit.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task<IReadOnlyList<RecordPosition>> CommitPositions(IReadOnlyList<RecordPosition> positions, CancellationToken cancellationToken = default);

    // /// <summary>
    // ///  Resolves the start position for a consumer's subscription to a stream.
    // /// </summary>
    // public Task<RecordPosition> ResolveStartPosition(RecordPosition? startPosition, SubscriptionInitialPosition initialPosition, CancellationToken cancellationToken = default) =>
    //     ResolveStartPosition(startPosition ?? LogPosition.Unset, initialPosition, cancellationToken);

    /// <summary>
    ///  Resolves the start position for a consumer's subscription to a stream.
    /// </summary>
    public async Task<RecordPosition> ResolveStartPosition(LogPosition startPosition, SubscriptionInitialPosition initialPosition, CancellationToken cancellationToken = default) {
        // currently we only support a single consumer per group and no partitions.
        var positions = await LoadPositions(cancellationToken).ConfigureAwait(false);

        // 0 denotes the beginning of the stream, so we should start from the earliest position.
        if (startPosition == LogPosition.From(0))
            return new() { LogPosition = LogPosition.Earliest };

        if (startPosition != LogPosition.Unset)
            return startPosition;

        var subscriptionPosition = positions.Count == 0
            ? InitialPositionToStartPosition(initialPosition)
            : positions[^1];

        return subscriptionPosition;

        static RecordPosition InitialPositionToStartPosition(SubscriptionInitialPosition initialPosition) {
            return initialPosition switch {
                SubscriptionInitialPosition.Earliest => new() { LogPosition = LogPosition.Earliest },
                SubscriptionInitialPosition.Latest   => new() { LogPosition = LogPosition.Latest }
            };
        }
    }
}
