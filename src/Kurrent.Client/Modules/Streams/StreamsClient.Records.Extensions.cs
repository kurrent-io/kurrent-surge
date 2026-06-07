using System.Runtime.CompilerServices;

namespace Kurrent.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(IAsyncEnumerable<ReadRecordsResult> source) {
        /// <summary>
        /// Projects a read down to its records. Lossless for reads: on the V1 wire a read
        /// emits no filter checkpoints (subscriptions own those), so the only message this
        /// drops is the terminal CaughtUp marker — irrelevant for a bounded read.
        /// </summary>
        public async IAsyncEnumerable<Record> Records([EnumeratorCancellation] CancellationToken cancellationToken = default) {
            await foreach (var msg in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                // Concrete type test, not IsRecord: a default ReadRecordsResult reports
                // IsRecord with a null payload (see DEV-1769).
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
