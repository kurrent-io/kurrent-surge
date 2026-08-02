using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Streaming;

/// <summary>
/// A high-performance, zero-allocation record processor that dispatches
/// decoded messages to strongly-typed handlers based on their runtime type.
/// <para>
/// Supports both inline fluent registration and subclass patterns:
/// </para>
/// <code>
/// // Inline
/// var processor = new MessageProcessor()
///     .Process&lt;OrderPlaced&gt;(async (msg, ctx) => { /* ... */ })
///     .Process&lt;OrderShipped&gt;((msg, ctx) => { /* sync */ });
///
/// // Subclass
/// class MyProcessor : MessageProcessor {
///     public MyProcessor() {
///         Process&lt;OrderPlaced&gt;(HandleOrderPlaced);
///     }
/// }
/// </code>
/// </summary>
[PublicAPI]
public class MessageProcessor : IRecordProcessor {
    readonly HandlerRegistry<IMessageHandler> _handlers = new();

    /// <summary>
    /// Registers an async handler for messages of type <typeparamref name="T"/>.
    /// </summary>
    public MessageProcessor Process<T>(Func<T, ProcessRecordContext, ValueTask> handler) {
        _handlers.Add<T>(new AsyncHandler<T>(handler));
        return this;
    }

    /// <summary>
    /// Registers a synchronous handler for messages of type <typeparamref name="T"/>.
    /// </summary>
    public MessageProcessor Process<T>(Action<T, ProcessRecordContext> handler) {
        _handlers.Add<T>(new SyncHandler<T>(handler));
        return this;
    }

    /// <summary>
    /// Registers an async fallback handler invoked when no typed handler matches
    /// or the record is not decoded.
    /// </summary>
    public MessageProcessor Fallback(Func<ProcessRecordContext, ValueTask> handler) {
        _handlers.Fallback(new AsyncFallbackHandler(handler));
        return this;
    }

    /// <summary>
    /// Registers a synchronous fallback handler invoked when no typed handler matches
    /// or the record is not decoded.
    /// </summary>
    public MessageProcessor Fallback(Action<ProcessRecordContext> handler) {
        _handlers.Fallback(new SyncFallbackHandler(handler));
        return this;
    }

    Func<CheckpointContext, ValueTask>? _onCheckpoint;

    /// <summary>
    /// Registers a handler invoked when a checkpoint is signaled.
    /// Required when automatic checkpointing is disabled.
    /// </summary>
    public MessageProcessor OnCheckpoint(Func<CheckpointContext, ValueTask> handler) {
        _onCheckpoint = handler;
        return this;
    }

    /// <inheritdoc cref="OnCheckpoint(Func{CheckpointContext, ValueTask})"/>
    public MessageProcessor OnCheckpoint(Action<CheckpointContext> handler) {
        _onCheckpoint = ctx => {
            handler(ctx);
            return ValueTask.CompletedTask;
        };

        return this;
    }

    /// <summary>
    /// Returns <c>true</c> if a handler is registered for the record's decoded message type, or a
    /// fallback is registered — a fallback takes everything a typed handler does not, so nothing is
    /// skipped once one is set.
    /// </summary>
    public bool CanProcess(Record record) =>
        _handlers.CanHandle(record) || _handlers.HasFallback;

    ValueTask IRecordProcessor.ProcessRecord(ProcessRecordContext context) {
        var match = _handlers.Resolve(context.Record);

        if (!match.Found)
            return ValueTask.CompletedTask;

        if (match.IsFallback)
            context.Logger.LogTrace("Record {Position} fell back to the generic handler", context.Record.Position);

        return match.Handler.Handle(context);
    }

    ValueTask IProcessor.OnCheckpoint(CheckpointContext context) =>
        _onCheckpoint?.Invoke(context) ?? ValueTask.CompletedTask;
}

public interface IMessageHandler {
    ValueTask Handle(ProcessRecordContext context);
}

sealed class AsyncHandler<T>(Func<T, ProcessRecordContext, ValueTask> handler) : IMessageHandler {
    public ValueTask Handle(ProcessRecordContext ctx) =>
        handler(ctx.Record.As<T>(), ctx);
}

sealed class SyncHandler<T>(Action<T, ProcessRecordContext> handler) : IMessageHandler {
    public ValueTask Handle(ProcessRecordContext ctx) {
        handler(ctx.Record.As<T>(), ctx);
        return ValueTask.CompletedTask;
    }
}

sealed class AsyncFallbackHandler(Func<ProcessRecordContext, ValueTask> handler) : IMessageHandler {
    public ValueTask Handle(ProcessRecordContext ctx) =>
        handler(ctx);
}

sealed class SyncFallbackHandler(Action<ProcessRecordContext> handler) : IMessageHandler {
    public ValueTask Handle(ProcessRecordContext ctx) {
        handler(ctx);
        return ValueTask.CompletedTask;
    }
}