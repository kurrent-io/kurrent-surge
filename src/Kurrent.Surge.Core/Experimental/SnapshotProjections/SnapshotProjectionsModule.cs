using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming;

namespace Kurrent.SnapshotProjections;

/// <summary>
/// Applies <paramref name="message"/> to <paramref name="snapshot"/> and returns the snapshot to save.
/// Return the module's <c>EmptySnapshot</c> to signal that nothing changed and skip the append.
/// </summary>
delegate TSnapshot UpdateSnapshot<TSnapshot, in T>(TSnapshot snapshot, T message);

/// <inheritdoc cref="UpdateSnapshot{TSnapshot,T}"/>
delegate TSnapshot UpdateSnapshotWithContext<TSnapshot, in T>(TSnapshot snapshot, T message, ProcessRecordContext context);

/// <summary>
/// A processor that folds messages into a single snapshot held on <paramref name="snapshotStream"/>.
/// Each handled message loads the current snapshot, applies the update, and appends the result guarded
/// on the revision it read — so a losing writer fails loudly instead of clobbering the winner.
/// </summary>
abstract class SnapshotProjectionsModule<TSnapshot> : MessageProcessor
    where TSnapshot : class, new() {
    protected SnapshotProjectionsModule(ISnapshotProjectionsStore store, StreamName snapshotStream) {
        Store          = store;
        SnapshotStream = snapshotStream;

        // MessageProcessor holds ONE checkpoint handler — a second registration replaces the first —
        // so the base claims it and derived classes override OnCheckpointed instead. Registering
        // directly on a subclass would silently unhook the catch-up signal below.
        OnCheckpoint(HandleCheckpoint);
    }

    ISnapshotProjectionsStore Store          { get; }
    StreamName                SnapshotStream { get; }

    /// <summary>
    /// The sentinel an update handler returns to signal that it applied no changes.
    /// </summary>
    protected readonly TSnapshot EmptySnapshot = new();

    readonly TaskCompletionSource<(RecordPosition Position, DateTimeOffset Timestamp)> _caughtUp =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completes when the projection has consumed everything that existed when it started and is
    /// following live, carrying the position it caught up at. Await this before serving reads off the
    /// snapshot, so a caller cannot observe a partially rebuilt state as if it were current.
    /// </summary>
    /// <remarks>
    /// Cancelled — not left pending — if the processor shuts down before catching up, so a waiter
    /// fails loudly on a dead processor instead of hanging on a signal that can no longer arrive.
    /// </remarks>
    public Task<(RecordPosition Position, DateTimeOffset Timestamp)> WaitUntilCaughtUp => _caughtUp.Task;

    /// <summary>
    /// Whether the projection has caught up. False while still replaying, and false if the processor
    /// shut down before it got there.
    /// </summary>
    public bool IsCaughtUp => _caughtUp.Task.IsCompletedSuccessfully;

    /// <summary>
    /// Invoked on every checkpoint barrier, after the module's own bookkeeping. Override this instead
    /// of registering a checkpoint handler.
    /// </summary>
    protected virtual ValueTask OnCheckpointed(CheckpointContext context) => ValueTask.CompletedTask;

    ValueTask HandleCheckpoint(CheckpointContext context) {
        switch (context.Trigger) {
            case CheckpointTrigger.CaughtUp:
                // The barrier carries the scan watermark it caught up at; Positions is the per-partition
                // progress behind it, used when the watermark is absent.
                var position = context.ScanPosition
                            ?? (context.Positions.Length > 0 ? context.Positions[^1] : RecordPosition.Unset);

                _caughtUp.TrySetResult((position, context.Timestamp));
                break;

            case CheckpointTrigger.Shutdown:
                // The final barrier of the processor's life. Anything still waiting on catch-up will
                // wait forever, so end it here.
                _caughtUp.TrySetCanceled();
                break;
        }

        return OnCheckpointed(context);
    }

    Func<Record, DateTimeOffset> GetMessageTimestamp { get; set; } = record => record.Timestamp.ToUniversalTime();

    /// <summary>
    /// Overrides how a message's event time is derived when an <c>UpdateWhen</c> registration does not
    /// supply its own timestamp selector. Defaults to the record's own timestamp in UTC.
    /// </summary>
    protected void SetMessageTimestampProvider(Func<Record, DateTimeOffset> getTimestamp) =>
        GetMessageTimestamp = getTimestamp;

    /// <summary>
    /// Registers an update applied whenever a message of type <typeparamref name="T"/> is processed.
    /// </summary>
    protected void UpdateWhen<T>(UpdateSnapshotWithContext<TSnapshot, T> update, Func<T, DateTimeOffset>? getTimestamp = null) where T : class, new() {
        Process<T>(async (message, ctx) => {
            try {
                var messageTimestamp = getTimestamp?.Invoke(message) ?? GetMessageTimestamp(ctx.Record);

                var (snapshot, position, _) = await Store
                    .LoadSnapshot<TSnapshot>(SnapshotStream, ctx.CancellationToken)
                    .ConfigureAwait(false);

                var updated = update(snapshot, message, ctx);

                if (ReferenceEquals(updated, EmptySnapshot)) {
                    ctx.Logger.LogTrace(
                        "Skipped update of {SnapshotName} v{SnapshotRevision} on {MessageName} because no changes were applied",
                        typeof(TSnapshot).Name, position.Revision, typeof(T).Name
                    );

                    return;
                }

                // A stream that has never been written has no revision to guard on, so guard on its
                // absence instead — two projectors racing to create the same snapshot must not both win.
                // Explicitly typed: Revision and ExpectedStreamState convert to one another, so the
                // conditional has no natural type to infer (CS0172).
                ExpectedStreamState expectedState = position.IsUnset
                    ? ExpectedStreamState.NoStream
                    : position.Revision;

                await Store
                    .SaveSnapshot(updated, SnapshotStream, expectedState,
                        messageTimestamp, ctx.CancellationToken)
                    .ConfigureAwait(false);

                ctx.Logger.LogTrace(
                    "Updated {SnapshotName} v{SnapshotRevision} on {MessageName}",
                    typeof(TSnapshot).Name, position.Revision, typeof(T).Name
                );
            }
            // Cancellation is the host shutting the processor down, not a projection failure — it stays
            // an OperationCanceledException so the drone's shutdown path recognises it.
            catch (Exception ex) when (ex is not OperationCanceledException) {
                throw new SnapshotProjectionException(
                    $"Failed update of {typeof(TSnapshot).Name} on {typeof(T).Name} for stream {SnapshotStream}", ex);
            }
        });
    }

    /// <inheritdoc cref="UpdateWhen{T}(UpdateSnapshotWithContext{TSnapshot,T}, Func{T, DateTimeOffset})"/>
    protected void UpdateWhen<T>(UpdateSnapshot<TSnapshot, T> update, Func<T, DateTimeOffset>? getTimestamp = null) where T : class, new() =>
        UpdateWhen<T>((snapshot, message, _) => update(snapshot, message), getTimestamp);
}

/// <summary>
/// Thrown when a snapshot projection fails to load, apply, or save an update, wrapping the underlying
/// failure with the snapshot and message types that were in play.
/// </summary>
sealed class SnapshotProjectionException(string reason, Exception innerException)
    : KurrentException(ErrorCode.From(nameof(SnapshotProjectionException)), reason, innerException);
