using System.Text.Json;

using Kurrent.Projectors.Transactional;
using Kurrent.Quack;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors.DuckDB.Experimental;

[PublicAPI]
public abstract class DuckDBDocumentProjection<TDocument, TKey> : DuckDBBulkProjection
    where TDocument : class
    where TKey : notnull {
    readonly HandlerRegistry<Fold> _handlers = new();

    readonly DuckDBDocumentStore  _store;
    readonly DocumentsDataContext _documents;

    protected DuckDBDocumentProjection(TimeProvider? time = null, JsonSerializerOptions? json = null)
        : this(new DuckDBDocumentStore(time, json)) { }

    DuckDBDocumentProjection(DuckDBDocumentStore store) {
        _store     = store;
        _documents = new(store);

        Setup((_, cancellationToken) => store.EnsureCollection<TDocument>(cancellationToken));
    }

    #region ->> Project <<-

    protected DuckDBDocumentProjection<TDocument, TKey> Project<T>(
        Func<T, TKey> selectKey,
        Func<T, TDocument?, CancellationToken, ValueTask<TDocument?>> fold
    ) {
        _handlers.Add<T>(new Fold(
            record => selectKey(record.As<T>()),
            (record, current, cancellationToken) => fold(record.As<T>(), current, cancellationToken)));

        return this;
    }

    protected DuckDBDocumentProjection<TDocument, TKey> Project<T>(
        Func<T, TKey> selectKey,
        Func<T, TDocument?, TDocument?> fold
    ) =>
        Project<T>(selectKey, (message, document, _) => ValueTask.FromResult(fold(message, document)));

    #endregion

    public override void Wire(
        string projectionId,
        IProjectionClientProvider<DuckDBAdvancedConnection> clients,
        IProjectionCheckpointStore<DuckDBAdvancedConnection> checkpoints
    ) {
        base.Wire(projectionId, clients, checkpoints);

        _store.Bind(Transaction);
    }

    public sealed override async ValueTask Project(BulkProjectionContext<DuckDBAdvancedConnection> context) {
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

            var next = await fold.Apply(record, current, context.CancellationToken).ConfigureAwait(false);

            if (next is null)
                _documents.Delete<TDocument, TKey>(key);
            else
                _documents.Store(key, next);

            context.Track(record);
        }
    }

    protected override async ValueTask OnCheckpoint(CheckpointContext context) {
        await _documents.SaveChanges(context.CancellationToken).ConfigureAwait(false);

        await base.OnCheckpoint(context).ConfigureAwait(false);
    }

    sealed record Fold(
        Func<Record, TKey>                                              SelectKey,
        Func<Record, TDocument?, CancellationToken, ValueTask<TDocument?>> Apply
    );
}
