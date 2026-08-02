using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors.Transactional;

/// <summary>
/// Projection over a single document type, keyed and coalesced per batch: every message in the batch
/// for the same key shares one load and folds into one write.
/// </summary>
/// <typeparam name="TClient">The backend's client type.</typeparam>
/// <typeparam name="TDocument">The projected document.</typeparam>
/// <typeparam name="TKey">The document's key, extracted from each message.</typeparam>
/// <remarks>
/// <para>
/// A working map, local to each batch, holds the current value of every key the batch touches. The
/// first message for a key calls <see cref="IDocumentStore.Load{TDocument,TKey}"/>; every later message in
/// the batch folds onto the in-memory value instead — even when that value is <c>null</c>, because a delete
/// is authoritative and must not be reloaded away. When the batch ends the map is drained through one call
/// to <see cref="IDocumentStore.Save{TDocument,TKey}"/> per document type: a non-null value writes the
/// document, a null value removes it. One load and one write per key per batch.
/// </para>
/// <para>
/// Earlier batches of the same transaction are visible through the store's load, provided the store reads on
/// the open transaction's client: a value written batches ago — though not yet committed — still comes back.
/// The open transaction is the only state that spans batches; nothing else is buffered, so there is nothing
/// extra to discard if the run dies before its checkpoint.
/// </para>
/// <para>
/// Pick this shape when a later message needs to see what an earlier one wrote — including deleting the
/// row by returning <c>null</c>. When a message always carries the document's complete state and there
/// is nothing for a load to add, <see cref="UpsertProjection{TClient,TDocument}"/> is cheaper.
/// </para>
/// <para>
/// <see cref="OnBatch"/> ignores its client: every read and write goes through the store, which resolves its
/// own. <paramref name="collection"/> travels on every store call, so one store instance serves as many
/// projections and document types as a backend has.
/// </para>
/// </remarks>
/// <param name="store">Where the documents live.</param>
[PublicAPI]
public abstract class DocumentProjection<TClient, TDocument, TKey>(IDocumentStore store)
    : ProjectionProcessor<TClient>
    where TDocument : class
    where TKey : notnull where TClient : IAsyncDisposable {
    readonly HandlerRegistry<Fold> _handlers = new();

    readonly DocumentsDataContext _documents = new(store);

    #region ->> Project <<-

    /// <summary>
    /// Registers an async fold for messages of type <typeparamref name="T"/>: the message and the
    /// document's current value in, the next value out. Returning <c>null</c> deletes the document.
    /// </summary>
    protected DocumentProjection<TClient, TDocument, TKey> Project<T>(
        Func<T, TKey> selectKey,
        Func<T, TDocument?, DocumentProjectionContext, ValueTask<TDocument?>> fold
    ) {
        _handlers.Add<T>(new Fold(
            record => selectKey(record.As<T>()),
            (record, current, context) => fold(record.As<T>(), current, context)));

        return this;
    }

    /// <inheritdoc cref="Project{T}(Func{T, TKey}, Func{T, TDocument, DocumentProjectionContext, ValueTask{TDocument}})"/>
    protected DocumentProjection<TClient, TDocument, TKey> Project<T>(
        Func<T, TKey> selectKey,
        Func<T, TDocument?, DocumentProjectionContext, TDocument?> fold
    ) =>
        Project<T>(selectKey, (message, document, context) => ValueTask.FromResult(fold(message, document, context)));

    #endregion

    /// <inheritdoc/>
    public sealed override async ValueTask OnBatch(BatchContext context) {
        // The key of every record is known before any fold runs, because selectKey is a function of the
        // message alone. That is what turns a batch into one read instead of one read per key.
        var folds = new List<(TKey Key, Fold Fold, Record Record)>();

        foreach (var record in context.Records) {
            var match = _handlers.Resolve(record);

            if (!match.Found)
                continue;

            folds.Add((match.Handler.SelectKey(record), match.Handler, record));
        }

        if (folds.Count == 0)
            return;

        await _documents
            .LoadMany<TDocument, TKey>(folds.Select(fold => fold.Key).Distinct().ToArray(), context.CancellationToken)
            .ConfigureAwait(false);

        foreach (var (key, fold, record) in folds) {
            var current = _documents.Document<TDocument, TKey>(key);

            var next = await fold
                .Apply(record, current, DocumentProjectionContext.From(record, context))
                .ConfigureAwait(false);

            if (next is null)
                _documents.Delete<TDocument, TKey>(key);
            else
                _documents.Store(key, next);

            // Tracked after each fold. A fold that throws never reaches the map, so the frontier
            // always equals exactly what the map holds, and the final checkpoint after a mid-batch
            // failure drains and commits that consistent prefix.
            context.Track(record);
        }
    }

    /// <inheritdoc/>
    protected override ValueTask OnCheckpoint(CheckpointContext context) =>
        _documents.SaveChanges(context.CancellationToken);

    /// <summary>
    /// One registration, already closed over its message type: the key it reads from a record, and the fold
    /// it applies. Two members, because the key has to be known before any fold runs.
    /// </summary>
    sealed record Fold(
        Func<Record, TKey>                                                         SelectKey,
        Func<Record, TDocument?, DocumentProjectionContext, ValueTask<TDocument?>> Apply
    );
}


/// <summary>
/// Context for a fold that projects a record into a document. The document's current value travels as
/// the fold's own parameter, and the client stays out of reach entirely: a fold is pure — storage flows
/// through the projection's <see cref="IDocumentStore"/>, never through a handler.
/// </summary>
[PublicAPI]
public readonly record struct DocumentProjectionContext {
    public required Record            Record            { get; init; }
    public required string            ProjectionId      { get; init; }
    public required ILogger           Logger            { get; init; }
    public required CancellationToken CancellationToken { get; init; }

    internal static DocumentProjectionContext From(Record record, BatchContext source) =>
        new() {
            Record            = record,
            ProjectionId      = source.ProcessorId,
            Logger            = source.Logger,
            CancellationToken = source.CancellationToken
        };
}