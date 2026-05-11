using System.Collections.Frozen;
using Kurrent.Client.Streams;
using Kurrent.Streaming;

namespace Kurrent.Projections;

/// <summary>
/// Base class for a type-dispatching projection that runs on top of <see cref="IRecordProcessor"/>.
/// <para>
/// Register per-type handlers via <see cref="Project{T}(Func{T, ProjectionContext{TClient}, ValueTask})"/>
/// (and overloads), an optional <c>Setup</c> callback for one-time initialization, and an optional
/// fallback for unhandled record types.
/// </para>
/// <para>
/// The client is resolved lazily via the user-supplied <see cref="GetProjectionClient{TClient}"/>
/// delegate set on <see cref="ProjectorOptions{TClient}.ResolveClient"/> (or an integration-specific
/// equivalent such as <c>DuckDBProjectorOptions.ConnectionProvider</c>). Caching, pooling, and
/// fresh-per-call semantics are the responsibility of the delegate implementation.
/// </para>
/// </summary>
[PublicAPI]
public abstract class Projection<TClient> : IRecordProcessor {
    readonly Dictionary<Type, IProjectionHandler> _handlers = [];

    IProjectionHandler?                          _fallback;
    Func<TClient, CancellationToken, ValueTask>? _setup;
    GetProjectionClient<TClient>?                _resolveClient;

    FrozenDictionary<Type, IProjectionHandler> Handlers => field ??= _handlers.ToFrozenDictionary();

    internal void UseClientResolver(GetProjectionClient<TClient> resolver) =>
        _resolveClient = resolver;

    #region ->> Setup <<-

    /// <summary>
    /// Registers an async setup handler invoked once when the projection connects,
    /// before any records are dispatched.
    /// </summary>
    protected void Setup(Func<TClient, CancellationToken, ValueTask> handler) =>
        _setup = handler;

    /// <inheritdoc cref="Setup(Func{TClient, CancellationToken, ValueTask})"/>
    protected void Setup(Action<TClient, CancellationToken> handler) =>
        _setup = (client, ct) => {
            handler(client, ct);
            return ValueTask.CompletedTask;
        };

    #endregion

    #region ->> Project <<-

    /// <summary>
    /// Registers an async handler for messages of type <typeparamref name="T"/>.
    /// Handler resolves the client via <see cref="ProjectionContext{TClient}.GetClient"/>.
    /// </summary>
    protected Projection<TClient> Project<T>(Func<T, ProjectionContext<TClient>, ValueTask> handler) {
        _handlers[typeof(T)] = new AsyncHandler<T>(handler);
        return this;
    }

    /// <summary>
    /// Registers a sync handler for messages of type <typeparamref name="T"/>.
    /// Handler resolves the client via <see cref="ProjectionContext{TClient}.GetClient"/>.
    /// </summary>
    protected Projection<TClient> Project<T>(Action<T, ProjectionContext<TClient>> handler) {
        _handlers[typeof(T)] = new SyncHandler<T>(handler);
        return this;
    }

    /// <summary>
    /// Registers an async handler for messages of type <typeparamref name="T"/> with
    /// a pre-resolved client. The framework resolves the client before dispatch.
    /// </summary>
    protected Projection<TClient> Project<T>(Func<T, TClient, ProjectionContext<TClient>, ValueTask> handler) {
        _handlers[typeof(T)] = new AsyncHandlerWithClient<T>(handler);
        return this;
    }

    /// <summary>
    /// Registers a sync handler for messages of type <typeparamref name="T"/> with
    /// a pre-resolved client. The framework resolves the client before dispatch.
    /// </summary>
    protected Projection<TClient> Project<T>(Action<T, TClient, ProjectionContext<TClient>> handler) {
        _handlers[typeof(T)] = new SyncHandlerWithClient<T>(handler);
        return this;
    }

    #endregion

    #region ->> Fallback <<-

    /// <summary>Registers an async fallback handler invoked when no typed handler matches.</summary>
    protected Projection<TClient> Fallback(Func<ProjectionContext<TClient>, ValueTask> handler) {
        _fallback = new AsyncFallback(handler);
        return this;
    }

    /// <summary>Registers a sync fallback handler invoked when no typed handler matches.</summary>
    protected Projection<TClient> Fallback(Action<ProjectionContext<TClient>> handler) {
        _fallback = new SyncFallback(handler);
        return this;
    }

    /// <summary>Registers an async fallback handler with a pre-resolved client.</summary>
    protected Projection<TClient> Fallback(Func<TClient, ProjectionContext<TClient>, ValueTask> handler) {
        _fallback = new AsyncFallbackWithClient(handler);
        return this;
    }

    /// <summary>Registers a sync fallback handler with a pre-resolved client.</summary>
    protected Projection<TClient> Fallback(Action<TClient, ProjectionContext<TClient>> handler) {
        _fallback = new SyncFallbackWithClient(handler);
        return this;
    }

    #endregion

    #region ->> IRecordProcessor <<-

    /// <summary>
    /// Returns <c>true</c> if a handler is registered for the record's decoded message type.
    /// </summary>
    public bool CanProject(Record record) =>
        record.IsDecoded && Handlers.ContainsKey(record.ValueType);

    async ValueTask IProcessor.OnConnected(ConnectedContext context) {
        if (_resolveClient is null)
            throw new InvalidOperationException(
                "Projection client resolver is not configured. Set ProjectorOptions.ResolveClient " +
                "(or an integration-specific equivalent such as DuckDBProjectorOptions.ConnectionProvider).");

        if (_setup is null)
            return;

        var client = await _resolveClient(context.CancellationToken).ConfigureAwait(false);
        await _setup(client, context.CancellationToken).ConfigureAwait(false);
    }

    ValueTask IRecordProcessor.ProcessRecord(ProcessRecordContext context) {
        var projCtx = new ProjectionContext<TClient>(context, _resolveClient!);

        if (context.Record.IsDecoded && Handlers.TryGetValue(context.Record.ValueType, out var handler))
            return handler.Handle(projCtx);

        return _fallback?.Handle(projCtx) ?? ValueTask.CompletedTask;
    }

    #endregion

    #region ->> Handler Types <<-

    interface IProjectionHandler {
        ValueTask Handle(ProjectionContext<TClient> ctx);
    }

    sealed class AsyncHandler<T>(Func<T, ProjectionContext<TClient>, ValueTask> handler) : IProjectionHandler {
        public ValueTask Handle(ProjectionContext<TClient> ctx) =>
            handler(ctx.Record.As<T>(), ctx);
    }

    sealed class SyncHandler<T>(Action<T, ProjectionContext<TClient>> handler) : IProjectionHandler {
        public ValueTask Handle(ProjectionContext<TClient> ctx) {
            handler(ctx.Record.As<T>(), ctx);
            return ValueTask.CompletedTask;
        }
    }

    sealed class AsyncHandlerWithClient<T>(Func<T, TClient, ProjectionContext<TClient>, ValueTask> handler) : IProjectionHandler {
        public async ValueTask Handle(ProjectionContext<TClient> ctx) {
            var client = await ctx.GetClient(ctx.CancellationToken).ConfigureAwait(false);
            await handler(ctx.Record.As<T>(), client, ctx).ConfigureAwait(false);
        }
    }

    sealed class SyncHandlerWithClient<T>(Action<T, TClient, ProjectionContext<TClient>> handler) : IProjectionHandler {
        public async ValueTask Handle(ProjectionContext<TClient> ctx) {
            var client = await ctx.GetClient(ctx.CancellationToken).ConfigureAwait(false);
            handler(ctx.Record.As<T>(), client, ctx);
        }
    }

    sealed class AsyncFallback(Func<ProjectionContext<TClient>, ValueTask> handler) : IProjectionHandler {
        public ValueTask Handle(ProjectionContext<TClient> ctx) => handler(ctx);
    }

    sealed class SyncFallback(Action<ProjectionContext<TClient>> handler) : IProjectionHandler {
        public ValueTask Handle(ProjectionContext<TClient> ctx) {
            handler(ctx);
            return ValueTask.CompletedTask;
        }
    }

    sealed class AsyncFallbackWithClient(Func<TClient, ProjectionContext<TClient>, ValueTask> handler) : IProjectionHandler {
        public async ValueTask Handle(ProjectionContext<TClient> ctx) {
            var client = await ctx.GetClient(ctx.CancellationToken).ConfigureAwait(false);
            await handler(client, ctx).ConfigureAwait(false);
        }
    }

    sealed class SyncFallbackWithClient(Action<TClient, ProjectionContext<TClient>> handler) : IProjectionHandler {
        public async ValueTask Handle(ProjectionContext<TClient> ctx) {
            var client = await ctx.GetClient(ctx.CancellationToken).ConfigureAwait(false);
            handler(client, ctx);
        }
    }

    #endregion
}
