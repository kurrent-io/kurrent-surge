using System.Runtime.CompilerServices;

namespace Kurrent.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(IAsyncEnumerable<ReadRecordsResult> source) {
        /// <summary>
        /// Projects a read down to its records, dropping every <see cref="Checkpoint"/>. A
        /// client-filtered read (one whose filter <see cref="RecordFilter.RequiresClientEvaluation"/>)
        /// synthesizes <see cref="FilterCheckpoint"/>s to carry progress across the records it
        /// discards; this record-only view drops them, so a consumer that needs to persist read
        /// progress through sparse stretches must iterate the result directly. The terminal CaughtUp
        /// marker is dropped too.
        /// </summary>
        public async IAsyncEnumerable<Record> Records([EnumeratorCancellation] CancellationToken cancellationToken = default) {
            await foreach (var msg in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                // Concrete type test, not IsRecord: a defaulted variant struct reports its
                // first case while holding a null payload.
                if (msg.Value is Record record)
                    yield return record;
            }
        }
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
