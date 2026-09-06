namespace Kurrent.Surge.Processors;

/// <summary>
/// A record processor that dispatches decoded messages to strongly-typed handlers based on their
/// runtime type, receiving records in batches. Handlers are per-message and identical in shape to
/// <see cref="MessageProcessor"/>'s — only the dispatch granularity differs: the drone delivers a
/// whole batch, and positions are tracked per batch rather than per record.
/// <para>
/// Supports both inline fluent registration and subclass patterns:
/// </para>
/// <code>
/// // Inline
/// var processor = new BatchMessageProcessor()
///     .Process&lt;OrderPlaced&gt;(async (msg, ctx) => { /* ... */ })
///     .Process&lt;OrderShipped&gt;((msg, ctx) => { /* sync */ });
///
/// // Subclass
/// class MyProcessor : BatchMessageProcessor {
///     public MyProcessor() {
///         Process&lt;OrderPlaced&gt;(HandleOrderPlaced);
///     }
/// }
/// </code>
/// </summary>
[PublicAPI]
public class BatchMessageProcessor : IRecordBatchProcessor {
    readonly HandlerRegistry<IMessageHandler> _handlers = new();

    /// <summary>
    /// Registers an async handler for messages of type <typeparamref name="T"/>.
    /// </summary>
    public BatchMessageProcessor Process<T>(Func<T, ProcessRecordContext, ValueTask> handler) {
        _handlers.Add<T>(new AsyncHandler<T>(handler));
        return this;
    }

    /// <summary>
    /// Registers a synchronous handler for messages of type <typeparamref name="T"/>.
    /// </summary>
    public BatchMessageProcessor Process<T>(Action<T, ProcessRecordContext> handler) {
        _handlers.Add<T>(new SyncHandler<T>(handler));
        return this;
    }

    /// <summary>
    /// Registers an async fallback handler invoked when no typed handler matches
    /// or the record is not decoded.
    /// </summary>
    public BatchMessageProcessor Fallback(Func<ProcessRecordContext, ValueTask> handler) {
        _handlers.Fallback(new AsyncFallbackHandler(handler));
        return this;
    }

    /// <summary>
    /// Registers a synchronous fallback handler invoked when no typed handler matches
    /// or the record is not decoded.
    /// </summary>
    public BatchMessageProcessor Fallback(Action<ProcessRecordContext> handler) {
        _handlers.Fallback(new SyncFallbackHandler(handler));
        return this;
    }

    Func<CheckpointContext, ValueTask>? _onCheckpoint;

    /// <summary>
    /// Registers a handler invoked when a checkpoint is signaled.
    /// Required when automatic checkpointing is disabled.
    /// </summary>
    public BatchMessageProcessor OnCheckpoint(Func<CheckpointContext, ValueTask> handler) {
        _onCheckpoint = handler;
        return this;
    }

    /// <inheritdoc cref="OnCheckpoint(Func{CheckpointContext, ValueTask})"/>
    public BatchMessageProcessor OnCheckpoint(Action<CheckpointContext> handler) {
        _onCheckpoint = ctx => {
            handler(ctx);
            return ValueTask.CompletedTask;
        };

        return this;
    }

    async ValueTask IRecordBatchProcessor.ProcessBatch(BatchContext context) {
        foreach (var record in context.Records) {
            var match = _handlers.Resolve(record);

            if (!match.Found)
                continue;

            if (match.IsFallback)
                context.Logger.LogTrace("Record {Position} fell back to the generic handler", record.Position);

            // Tracking flows through the batch's own callback, so a handler that tracks a position
            // affects the same frontier the drone commits. Checkpoint and stop requests land on this
            // per-record context and are propagated below — the drone only reads them off the batch.
            var recordContext = context.ForProcessRecord(record, context.RequestTrack);

            await match.Handler.Handle(recordContext).ConfigureAwait(false);

            if (recordContext.CheckpointRequested && context is { CheckpointRequested: false, StopRequested: false })
                context.Checkpoint(recordContext.CheckpointReason);

            // Stop means stop after THIS record, same as record-at-a-time dispatch: the rest of the
            // batch is neither handled nor tracked, so a restart replays it.
            if (recordContext.StopRequested) {
                context.StopProcessing(recordContext.StopReason);
                return;
            }
        }
    }

    ValueTask IProcessor.OnCheckpoint(CheckpointContext context) =>
        _onCheckpoint?.Invoke(context) ?? ValueTask.CompletedTask;
}
