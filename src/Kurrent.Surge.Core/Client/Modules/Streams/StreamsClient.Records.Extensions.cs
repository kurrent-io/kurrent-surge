using System.Runtime.CompilerServices;

namespace Kurrent.Surge.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(IAsyncEnumerable<ReadMessage> source) {
        /// <summary>
        /// Projects a read down to its records, routing every <see cref="Checkpoint"/> to
        /// <paramref name="onCheckpoint"/>. A client-filtered read (one whose filter
        /// <see cref="RecordFilter.RequiresClientEvaluation"/>) synthesizes <see cref="FilterCheckpoint"/>s
        /// to carry progress across the records it discards — so, exactly like the subscription
        /// projection, a record-only view requires acknowledging them and progress can never be
        /// dropped by accident. The terminal CaughtUp marker is routed through the handler too.
        /// </summary>
        public async IAsyncEnumerable<Record> Records(Action<Checkpoint> onCheckpoint, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            await foreach (var msg in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                // Concrete type test, not IsRecord: a defaulted variant struct reports its
                // first case while holding a null payload.
                if (msg.Value is Record record)
                    yield return record;
                else if (msg.Value is Checkpoint checkpoint)
                    onCheckpoint(checkpoint);
            }
        }

        /// <summary>
        /// Same record-only projection with an asynchronous checkpoint handler — the shape
        /// checkpoint stores actually need (persisting a position is I/O).
        /// </summary>
        public async IAsyncEnumerable<Record> Records(Func<Checkpoint, ValueTask> onCheckpoint, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            await foreach (var msg in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (msg.Value is Record record)
                    yield return record;
                else if (msg.Value is Checkpoint checkpoint)
                    await onCheckpoint(checkpoint).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Projects a read down to its records, ending enumeration once a record past
        /// <paramref name="position"/> is observed or the read completes.
        /// </summary>
        /// <param name="position">The last position to include in the result.</param>
        /// <returns>The records up to and including <paramref name="position"/>.</returns>
        /// <remarks>
        /// Checkpoints are discarded. Use <see cref="Records(Action{Checkpoint}, CancellationToken)"/>
        /// when read progress must be tracked.
        /// </remarks>
        public IAsyncEnumerable<Record> TakeRecordsUpTo(RecordPosition position) =>
            source
                // Concrete type test, not IsRecord: a defaulted variant struct reports its
                // first case while holding a null payload.
                .Where(msg => msg.Value is Record)
                .Select(msg => msg.AsRecord)
                .TakeWhile(rec => rec.Position.LogPosition <= position.LogPosition);

        /// <summary>
        /// Projects a read down to its records, ending enumeration when the server signals
        /// that the read has caught up with the end of the log.
        /// </summary>
        /// <returns>The records observed before the caught-up signal.</returns>
        /// <remarks>
        /// Checkpoints are discarded. Use <see cref="Records(Action{Checkpoint}, CancellationToken)"/>
        /// when read progress must be tracked.
        /// </remarks>
        public IAsyncEnumerable<Record> TakeUntilCaughtUp() =>
            source
                .TakeWhile(msg => msg.Value is not CaughtUpCheckpoint)
                .Where(msg => msg.Value is Record)
                .Select(msg => msg.AsRecord);
    }

    extension(IAsyncEnumerable<SubscribeMessage> source) {
        /// <summary>
        /// Projects a subscription down to its records, routing every <see cref="Checkpoint"/>
        /// to <paramref name="onCheckpoint"/>. Checkpoints are the subscription's progress
        /// protocol — persisting them is what makes resume cheap through stretches where
        /// nothing matches — so a record-only view requires acknowledging them by design.
        /// </summary>
        public async IAsyncEnumerable<Record> Records(Action<Checkpoint> onCheckpoint, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            await foreach (var msg in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (msg.Value is Record record)
                    yield return record;
                else if (msg.Value is Checkpoint checkpoint)
                    onCheckpoint(checkpoint);

                // Connected is a session signal, deliberately dropped: a consumer projecting
                // to records has opted out of session-level concerns.
            }
        }

        /// <summary>
        /// Same record-only projection with an asynchronous checkpoint handler — the shape
        /// checkpoint stores actually need (persisting a position is I/O).
        /// </summary>
        public async IAsyncEnumerable<Record> Records(Func<Checkpoint, ValueTask> onCheckpoint, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            await foreach (var msg in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (msg.Value is Record record)
                    yield return record;
                else if (msg.Value is Checkpoint checkpoint)
                    await onCheckpoint(checkpoint).ConfigureAwait(false);
            }
        }
    }
}
