using Kurrent.Projectors.Transactional;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors;

/// <summary>
/// Full-control by message projection. Handlers receive the client and do whatever they need — read current state,
/// decide, write across as many tables as they like.
/// </summary>
[PublicAPI]
public abstract class Projection<TClient> : ProjectionProcessor<TClient> {
    readonly HandlerRegistry<IProjectionHandler> _handlers = new();
    
    /// <summary>
    /// Registers an async handler for messages of type <typeparamref name="T"/>.
    /// </summary>
    protected Projection<TClient> Project<T>(Func<T, ProjectionContext<TClient>, ValueTask> handler) {
        _handlers.Add<T>(new ProxyHandler<T>(handler));
        return this;
    }

    /// <summary>Registers a sync handler for messages of type <typeparamref name="T"/>.</summary>
    protected Projection<TClient> Project<T>(Action<T, ProjectionContext<TClient>> handler) {
        _handlers.Add<T>(new ProxyHandler<T>(handler));
        return this;
    }
    
    /// <inheritdoc/>
    public sealed override async ValueTask OnProcessBatch(TClient client, BatchContext context) {
        foreach (var record in context.Records) {
            var (handler, isFallback, found) = _handlers.Resolve(record);

            if (handler is { } && found)
                await handler
                    .Handle(ProjectionContext<TClient>.From(client, record, context))
                    .ConfigureAwait(false);

            // Tracked after each record, so the frontier always equals the applied work — on a
            // mid-batch throw, the final checkpoint commits exactly the writes sitting in the open
            // transaction.
            context.Track(record);
        }
    }

    #region ->> Inner Handlers <<-

    interface IProjectionHandler {
        ValueTask Handle(ProjectionContext<TClient> context);
    }

    sealed class ProxyHandler<T> : IProjectionHandler {
        readonly Func<T, ProjectionContext<TClient>, ValueTask> _handler;

        public ProxyHandler(Func<T, ProjectionContext<TClient>, ValueTask> handler) =>
            _handler = handler;

        public ProxyHandler(Action<T, ProjectionContext<TClient>> handler) =>
            _handler = (t, ctx) => {
                handler(t, ctx);
                return ValueTask.CompletedTask;
            };

        public ValueTask Handle(ProjectionContext<TClient> context) =>
            _handler(context.Record.As<T>(), context);
    }
        
    #endregion
}
