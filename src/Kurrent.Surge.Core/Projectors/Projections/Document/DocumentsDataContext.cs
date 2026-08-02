namespace Kurrent.Projectors.Transactional;

/// <summary>
/// Tracks documents across the batches between checkpoints and writes every change in two statements per
/// document type — one save, one delete.
/// </summary>
/// <remarks>
/// <para>
/// An identity map per document type: a key is read from storage at most once, and every later fold sees
/// what the previous one produced. <see cref="SaveChanges"/> drains the changes, then leaves the entries
/// clean at their new versions, so they stay usable as a read cache for the batches that follow.
/// </para>
/// <para>
/// Nothing here commits. The writes land in whatever transaction the store is running on, and that
/// transaction's commit is what makes them durable.
/// </para>
/// <para>
/// Not thread-safe, and does not need to be: a drone dispatches batches to one processor sequentially.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class DocumentsDataContext(IDocumentStore store) {
    readonly Dictionary<Type, Collection> _collections = [];

    /// <summary>Whether anything tracked is waiting to be written.</summary>
    public bool HasChanges => _collections.Values.Any(collection => collection.HasChanges);

    #region ->> Reading <<-

    /// <summary>
    /// Reads one document, from this context if it has seen the key and from storage otherwise.
    /// </summary>
    /// <typeparam name="TDocument">The document type.</typeparam>
    /// <typeparam name="TKey">The document's key.</typeparam>
    /// <param name="key">The document's key.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The document, or <c>null</c> when there is none. Storage is read at most once per key.</returns>
    public async ValueTask<TDocument?> Load<TDocument, TKey>(TKey key, CancellationToken cancellationToken)
        where TDocument : class
        where TKey : notnull {
        var collection = CollectionFor<TDocument, TKey>();

        if (collection.Documents.TryGetValue(key, out var tracked))
            return tracked.Current;

        StoredDocument<TDocument, TKey>? stored = null;

        await foreach (var found in store
            .Load<TDocument, TKey>(new LoadRequest<TKey>([key]), cancellationToken)
            .ConfigureAwait(false))
            stored = found;

        return collection.Track(key, stored).Current;
    }

    /// <summary>
    /// Reads every key this context has not seen, in one round trip. No unseen keys means no query at all.
    /// </summary>
    /// <typeparam name="TDocument">The document type.</typeparam>
    /// <typeparam name="TKey">The document's key.</typeparam>
    /// <param name="keys">The keys to read. Duplicates and already-tracked keys are skipped.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async ValueTask LoadMany<TDocument, TKey>(TKey[] keys, CancellationToken cancellationToken)
        where TDocument : class
        where TKey : notnull {
        var collection = CollectionFor<TDocument, TKey>();
        var missing    = keys.Where(key => !collection.Documents.ContainsKey(key)).Distinct().ToArray();

        if (missing.Length == 0)
            return;

        var found = new HashSet<TKey>();

        await foreach (var stored in store
            .Load<TDocument, TKey>(new LoadRequest<TKey>(missing), cancellationToken)
            .ConfigureAwait(false)) {
            collection.Track(stored.Key, stored);

            found.Add(stored.Key);
        }

        // Keys with no row are tracked too, as "looked, nothing there". Skipping them would send every
        // absent key back to storage on its next Load.
        foreach (var key in missing.Where(key => !found.Contains(key)))
            collection.Track(key, null);
    }

    /// <summary>Gets a tracked document. Never reads storage.</summary>
    /// <typeparam name="TDocument">The document type.</typeparam>
    /// <typeparam name="TKey">The document's key.</typeparam>
    /// <param name="key">The document's key.</param>
    /// <returns>
    /// The document, or <c>null</c> when there is no row or this context deleted it.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The key was never loaded. Returning <c>null</c> for an unread key would be indistinguishable from a
    /// document that does not exist.
    /// </exception>
    public TDocument? Document<TDocument, TKey>(TKey key)
        where TDocument : class
        where TKey : notnull {
        if (!CollectionFor<TDocument, TKey>().Documents.TryGetValue(key, out var tracked))
            throw new InvalidOperationException($"{typeof(TDocument).Name} '{key}' was never loaded into this session. Call Load or LoadMany first.");

        return tracked.Current;
    }

    #endregion

    #region ->> Writing <<-

    /// <summary>
    /// Records <paramref name="document"/> as the next value for <paramref name="key"/>. There is no dirty
    /// check: a call is a write.
    /// </summary>
    /// <typeparam name="TDocument">The document type.</typeparam>
    /// <typeparam name="TKey">The document's key.</typeparam>
    /// <param name="key">The document's key.</param>
    /// <param name="document">The value to write.</param>
    /// <remarks>
    /// Storing a key this context never loaded writes it as a new row. If one already exists, the store's
    /// version guard rejects the write rather than overwriting it.
    /// </remarks>
    public void Store<TDocument, TKey>(TKey key, TDocument document)
        where TDocument : class
        where TKey : notnull {
        var collection = CollectionFor<TDocument, TKey>();

        if (collection.Documents.TryGetValue(key, out var tracked))
            tracked.Store(document);
        else
            collection.Documents[key] = Collection<TDocument, TKey>.TrackedDocument.Created(document);
    }

    /// <summary>Records <paramref name="key"/> as deleted. A key with no row drains as nothing.</summary>
    /// <typeparam name="TDocument">The document type.</typeparam>
    /// <typeparam name="TKey">The document's key.</typeparam>
    /// <param name="key">The document's key.</param>
    public void Delete<TDocument, TKey>(TKey key)
        where TDocument : class
        where TKey : notnull {
        var collection = CollectionFor<TDocument, TKey>();

        if (collection.Documents.TryGetValue(key, out var tracked))
            tracked.Delete();
        else
            collection.Documents[key] = Collection<TDocument, TKey>.TrackedDocument.Absent();
    }

    /// <summary>
    /// Writes every tracked change and advances the versions it wrote. One call per document type.
    /// </summary>
    /// <remarks>
    /// One state per key, so a key appears once in the request: a removal carries a null document, and
    /// whatever it held earlier never reaches storage.
    /// </remarks>
    public async ValueTask SaveChanges(CancellationToken cancellationToken) {
        foreach (var collection in _collections.Values)
            await collection.SaveChanges(store, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Drops every tracked document and every pending change.</summary>
    public void Clear() =>
        _collections.Clear();

    #endregion

    Collection<TDocument, TKey> CollectionFor<TDocument, TKey>()
        where TDocument : class
        where TKey : notnull {
        if (!_collections.TryGetValue(typeof(TDocument), out var collection)) {
            var created = new Collection<TDocument, TKey>();
            _collections[typeof(TDocument)] = created;
            return created;
        }

        // A document type is one collection, so it has one key type. Two key types would encode keys two
        // ways into the same storage, which has to fail here rather than track two maps that both look right.
        return collection as Collection<TDocument, TKey>
            ?? throw new InvalidOperationException(
                   $"{typeof(TDocument).Name} is already tracked with key type {collection.KeyType.Name}, "
                 + $"not {typeof(TKey).Name}.");
    }

    #region ->> Tracking <<-

    // The store's methods are generic, but a drain only has Type values to work from. A typed collection
    // keeps the generic arguments alive from the call that created it, so the drain reaches the right
    // instantiation through a plain virtual call rather than reflection.
    abstract class Collection {
        public abstract Type KeyType { get; }

        public abstract bool HasChanges { get; }

        public abstract ValueTask SaveChanges(IDocumentStore store, CancellationToken cancellationToken);
    }

    sealed class Collection<TDocument, TKey> : Collection
        where TDocument : class
        where TKey : notnull {
        public Dictionary<TKey, TrackedDocument> Documents { get; } = [];

        public override Type KeyType => typeof(TKey);

        public override bool HasChanges => Documents.Values.Any(document => document.IsDirty);

        public TrackedDocument Track(TKey key, StoredDocument<TDocument, TKey>? stored) =>
            Documents[key] = stored is { } found ? TrackedDocument.Loaded(found) : TrackedDocument.Absent();

        public override async ValueTask SaveChanges(IDocumentStore store, CancellationToken cancellationToken) {
            var changes = Documents
                .Where(tracked => tracked.Value.IsDirty)
                // A key that never existed and ends deleted has nothing to remove.
                .Where(tracked => tracked.Value.State is not DocumentState.Deleted || tracked.Value.ExpectedVersion != 0)
                .Select(tracked => (
                    tracked.Key, tracked.Value.ExpectedVersion, tracked.Value.Version, Document: tracked.Value.Current))
                .ToArray();

            if (changes.Length > 0)
                await store.Save<TDocument, TKey>(new SaveRequest<TDocument, TKey>(changes), cancellationToken).ConfigureAwait(false);

            foreach (var tracked in Documents.Values)
                tracked.MarkSaved();
        }

        /// <summary>A document as this context holds it.</summary>
        /// <remarks>
        /// <c>ExpectedVersion</c> is the version storage had when the document was read and does not move
        /// until a write lands — it is what the store guards on. <c>Version</c> counts every change applied
        /// on top of it and is what the store writes: a key folded five times between checkpoints would
        /// otherwise end at version+1 in the row and version+5 here.
        /// </remarks>
        internal sealed class TrackedDocument {
            public TDocument?    Document        { get; private set; }
            public long          ExpectedVersion { get; private set; }
            public long          Version         { get; private set; }
            public DocumentState State           { get; private set; }

            /// <summary>What a fold should see: the document, or null when there is none to see.</summary>
            public TDocument? Current => State is DocumentState.Loaded or DocumentState.Stored ? Document : null;

            public bool IsDirty => State is DocumentState.Stored or DocumentState.Deleted;

            public static TrackedDocument Loaded(StoredDocument<TDocument, TKey> stored) =>
                new() {
                    Document        = stored.Document,
                    ExpectedVersion = stored.Version,
                    Version         = stored.Version,
                    State           = DocumentState.Loaded
                };

            public static TrackedDocument Absent() =>
                new() { State = DocumentState.Missing };

            public static TrackedDocument Created(TDocument document) =>
                new() { Document = document, Version = 1, State = DocumentState.Stored };

            public void Store(TDocument document) {
                Document = document;
                Version++;
                State = DocumentState.Stored;
            }

            // The document stays: a delete that has not drained yet has a version to guard on, and holding
            // the last known value keeps every field non-null for the documents that survive.
            public void Delete() =>
                State = DocumentState.Deleted;

            public void MarkSaved() {
                switch (State) {
                    case DocumentState.Stored:
                        ExpectedVersion = Version;
                        State           = DocumentState.Loaded;
                    break;

                    case DocumentState.Deleted:
                        Document        = null;
                        ExpectedVersion = 0;
                        Version         = 0;
                        State           = DocumentState.Missing;
                    break;
                }
            }
        }
    }

    enum DocumentState {
        /// <summary>Read from storage and unchanged since.</summary>
        Loaded,

        /// <summary>Read from storage, which had no row.</summary>
        Missing,

        /// <summary>Holds a value waiting to be written.</summary>
        Stored,

        /// <summary>Waiting to be removed.</summary>
        Deleted
    }

    #endregion
}