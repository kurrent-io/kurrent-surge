using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors.Transactional;

/// <summary>
/// Projection over a single document type with no load and no coalescing: every message that maps to a
/// document writes it immediately.
/// </summary>
/// <typeparam name="TClient">The backend's client type.</typeparam>
/// <typeparam name="TDocument">The projected document.</typeparam>
/// <remarks>
/// There is no <c>TKey</c> because nothing is keyed — no load, no delete, no folding of several messages
/// into one write. Pick this shape when a message always carries the document's complete state and the
/// upsert's own conflict handling is the only merge that ever needs to happen; the read a
/// <see cref="DocumentProjection{TClient,TDocument,TKey}"/> would do is pure waste there. When a later
/// message needs to see what an earlier one wrote, that is the shape instead.
/// </remarks>
[PublicAPI]
public abstract class UpsertProjection<TClient, TDocument> : ProjectionProcessor<TClient>
    where TDocument : class {
    readonly HandlerRegistry<IUpsertHandler> _handlers = new();

    /// <summary>Inserts or updates <paramref name="document"/>.</summary>
    protected abstract ValueTask Upsert(TClient client, TDocument document, CancellationToken cancellationToken);

    #region ->> Project <<-

    /// <summary>Registers an async handler. Returning <c>null</c> writes nothing for that message.</summary>
    protected UpsertProjection<TClient, TDocument> Project<T>(Func<T, ProjectionContext<TClient>, ValueTask<TDocument?>> handler) {
        _handlers.Add<T>(new AsyncHandler<T>(handler));
        return this;
    }

    /// <inheritdoc cref="Project{T}(Func{T, ProjectionContext{TClient}, ValueTask{TDocument}})"/>
    protected UpsertProjection<TClient, TDocument> Project<T>(Func<T, ProjectionContext<TClient>, TDocument?> handler) {
        _handlers.Add<T>(new SyncHandler<T>(handler));
        return this;
    }

    #endregion

    /// <inheritdoc/>
    public sealed override async ValueTask OnProcessBatch(TClient client, BatchContext context) {
        foreach (var record in context.Records) {
            var match = _handlers.Resolve(record);

            if (match.Found) {
                var document = await match.Handler
                    .Map(ProjectionContext<TClient>.From(client, record, context))
                    .ConfigureAwait(false);

                if (document is not null)
                    await Upsert(client, document, context.CancellationToken).ConfigureAwait(false);
            }

            // Tracked after each record, so the frontier always equals the applied work — on a
            // mid-batch throw, the final checkpoint commits exactly the upserts sitting in the open
            // transaction.
            context.Track(record);
        }
    }

    #region ->> Handler Types <<-

    interface IUpsertHandler {
        ValueTask<TDocument?> Map(ProjectionContext<TClient> context);
    }

    sealed class AsyncHandler<T>(Func<T, ProjectionContext<TClient>, ValueTask<TDocument?>> handler) : IUpsertHandler {
        public ValueTask<TDocument?> Map(ProjectionContext<TClient> context) =>
            handler(context.Record.As<T>(), context);
    }

    sealed class SyncHandler<T>(Func<T, ProjectionContext<TClient>, TDocument?> handler) : IUpsertHandler {
        public ValueTask<TDocument?> Map(ProjectionContext<TClient> context) =>
            ValueTask.FromResult(handler(context.Record.As<T>(), context));
    }

    #endregion
}
