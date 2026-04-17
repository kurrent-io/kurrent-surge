using System.Collections.Frozen;
using Kurrent.Client.Streams;

namespace Kurrent.Processors;

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
    readonly Dictionary<Type, IMessageHandler> _handlers = [];

    IMessageHandler? _fallback;

    FrozenDictionary<Type, IMessageHandler> Handlers => field ??= _handlers.ToFrozenDictionary();

    /// <summary>
    /// Registers an async handler for messages of type <typeparamref name="T"/>.
    /// </summary>
    public MessageProcessor Process<T>(Func<T, ProcessRecordContext, ValueTask> handler) {
        _handlers[typeof(T)] = new AsyncHandler<T>(handler);
        return this;
    }

    /// <summary>
    /// Registers a synchronous handler for messages of type <typeparamref name="T"/>.
    /// </summary>
    public MessageProcessor Process<T>(Action<T, ProcessRecordContext> handler) {
        _handlers[typeof(T)] = new SyncHandler<T>(handler);
        return this;
    }

    /// <summary>
    /// Registers an async fallback handler invoked when no typed handler matches
    /// or the record is not decoded.
    /// </summary>
    public MessageProcessor Fallback(Func<ProcessRecordContext, ValueTask> handler) {
        _fallback = new AsyncFallbackHandler(handler);
        return this;
    }

    /// <summary>
    /// Registers a synchronous fallback handler invoked when no typed handler matches
    /// or the record is not decoded.
    /// </summary>
    public MessageProcessor Fallback(Action<ProcessRecordContext> handler) {
        _fallback = new SyncFallbackHandler(handler);
        return this;
    }

    Func<CheckpointContext, ValueTask>? _onCheckpoint;
    Func<ErrorContext, ValueTask>?      _onError;
    Func<CompleteContext, ValueTask>?   _onComplete;

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
    /// Registers a handler invoked when the drone encounters an unrecoverable error.
    /// </summary>
    public MessageProcessor OnError(Func<ErrorContext, ValueTask> handler) {
        _onError = handler;
        return this;
    }

    /// <inheritdoc cref="OnError(Func{ErrorContext, ValueTask})"/>
    public MessageProcessor OnError(Action<ErrorContext> handler) {
        _onError = ctx => {
            handler(ctx);
            return ValueTask.CompletedTask;
        };

        return this;
    }

    /// <summary>
    /// Registers a handler invoked when the drone completes gracefully.
    /// </summary>
    public MessageProcessor OnComplete(Func<CompleteContext, ValueTask> handler) {
        _onComplete = handler;
        return this;
    }

    /// <inheritdoc cref="OnComplete(Func{CompleteContext, ValueTask})"/>
    public MessageProcessor OnComplete(Action<CompleteContext> handler) {
        _onComplete = ctx => {
            handler(ctx);
            return ValueTask.CompletedTask;
        };

        return this;
    }

    /// <summary>
    /// Returns <c>true</c> if a handler is registered for the record's decoded message type.
    /// </summary>
    public bool CanProcess(Record record) =>
        record.IsDecoded && Handlers.ContainsKey(record.Value!.GetType());

    ValueTask IRecordProcessor.ProcessRecord(ProcessRecordContext context) {
        if (context.Record.IsDecoded && Handlers.TryGetValue(context.Record.ValueType, out var handler))
            return handler.Handle(context);

        return _fallback?.Handle(context) ?? ValueTask.CompletedTask;
    }

    ValueTask IRecordProcessor.OnCheckpoint(CheckpointContext context) =>
        _onCheckpoint?.Invoke(context) ?? ValueTask.CompletedTask;

    ValueTask IRecordProcessor.OnError(ErrorContext context) =>
        _onError?.Invoke(context) ?? ValueTask.CompletedTask;

    ValueTask IRecordProcessor.OnComplete(CompleteContext context) =>
        _onComplete?.Invoke(context) ?? ValueTask.CompletedTask;
}

interface IMessageHandler {
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