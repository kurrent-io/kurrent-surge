using Kurrent.Projectors.Transactional;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors.Experimental;

/// <summary>
/// Per-message projection built on <see cref="BulkProjection{TClient}"/>: the batch loop and the typed
/// dispatch live here, not in the core.
/// </summary>
/// <remarks>
/// The client is resolved once for the batch and handed to every handler on its context, so a handler
/// never resolves one of its own — on a pooled provider that would be a different connection, outside
/// whatever transaction the projection is running in.
/// </remarks>
[PublicAPI]
public abstract class ExperimentalProjection<TClient> : BulkProjection<TClient>
    where TClient : IAsyncDisposable {
    readonly HandlerRegistry<IProjectionHandler> _handlers = new();

    protected ExperimentalProjection() =>
        base.Project(async context => {
            await using var client = await context.Clients
                .GetClient(context.CancellationToken)
                .ConfigureAwait(false);

            foreach (var record in context.Records) {
                var (handler, isFallback, found) = _handlers.Resolve(record);

                if (handler is { } && found)
                    await handler
                        .Handle(ProjectionContext<TClient>.From(client, record, context))
                        .ConfigureAwait(false);

                // Tracked after each record, so the frontier always equals the applied work.
                context.Track(record);
            }
        });

    /// <summary>Registers an async handler for messages of type <typeparamref name="T"/>.</summary>
    protected ExperimentalProjection<TClient> Project<T>(Func<T, ProjectionContext<TClient>, ValueTask> handler) {
        _handlers.Add<T>(new ProxyHandler<T>(handler));
        return this;
    }

    /// <summary>Registers a sync handler for messages of type <typeparamref name="T"/>.</summary>
    protected ExperimentalProjection<TClient> Project<T>(Action<T, ProjectionContext<TClient>> handler) {
        _handlers.Add<T>(new ProxyHandler<T>(handler));
        return this;
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

/// <summary>
/// Context for a handler that projects one record. Carries the client itself, not a provider — the shape
/// resolved it once for the batch.
/// </summary>
[PublicAPI]
public readonly record struct ProjectionContext<TClient> where TClient : IAsyncDisposable {
    public required TClient           Client            { get; init; }
    public required Record            Record            { get; init; }
    public required string            ProjectionId      { get; init; }
    public required ILogger           Logger            { get; init; }
    public required CancellationToken CancellationToken { get; init; }

    internal static ProjectionContext<TClient> From(
        TClient client,
        Record record,
        BulkProjectionContext<TClient> source
    ) =>
        new() {
            Client            = client,
            Record            = record,
            ProjectionId      = source.ProjectionId,
            Logger            = source.Logger,
            CancellationToken = source.CancellationToken
        };
}
