using System.Text.Json;

using Kurrent.Projectors.Transactional;
using Kurrent.Quack;

namespace Kurrent.Projectors.DuckDB;

/// <summary>
/// Closed <see cref="DocumentProjection{TClient,TDocument,TKey}"/> for DuckDB, over a
/// <see cref="DuckDBDocumentStore"/> it creates, wires and owns. A projection declares its folds and
/// nothing else: the table is named after <typeparamref name="TDocument"/> through
/// <see cref="DocumentCollection.For{TDocument}"/> and created at startup.
/// </summary>
/// <remarks>
/// <para>
/// Fully qualified for the same reason as <see cref="DuckDBProjection"/>'s base: an unqualified
/// <c>DocumentProjection</c> here is at risk of binding to whatever type of that name
/// <c>Kurrent.Surge.Core</c> eventually ships in the ancestor <c>Kurrent.Projectors</c> namespace.
/// </para>
/// <para>
/// Derive from <see cref="DocumentProjection{TClient,TDocument,TKey}"/> directly, with a store of your own,
/// when the JSON row this store writes is not the shape you want — real columns, a different key encoding,
/// prepared statements.
/// </para>
/// </remarks>
/// <param name="time">Stamps every row the store writes. Pass a fake to make timestamps deterministic.</param>
/// <param name="json">
/// Serializer options for <typeparamref name="TDocument"/>. Pass options carrying your
/// <c>JsonSerializerContext</c> under Native AOT; the default resolves source-generated metadata and falls
/// back to reflection only where the link-time switch leaves it enabled.
/// </param>
[PublicAPI]
public abstract class DuckDBDocumentProjection<TDocument, TKey> : DocumentProjection<DuckDBAdvancedConnection, TDocument, TKey>
    where TDocument : class
    where TKey : notnull {
    protected DuckDBDocumentProjection(TimeProvider? time = null, JsonSerializerOptions? json = null)
        : this(new DuckDBDocumentStore(time, json)) { }

    // Private hop so the store is available as a field: a base-constructor argument cannot be read back,
    // and Wire has to reach the same instance the base is calling.
    DuckDBDocumentProjection(DuckDBDocumentStore store) : base(store) {
        _store = store;

        Setup((_, cancellationToken) => store.EnsureCollection<TDocument>(cancellationToken));
    }

    readonly DuckDBDocumentStore _store;

    /// <inheritdoc/>
    public override void Wire(
        string projectionId,
        IProjectionTransaction<DuckDBAdvancedConnection> transaction,
        IProjectionCheckpointStore<DuckDBAdvancedConnection> checkpoints
    ) {
        base.Wire(projectionId, transaction, checkpoints);

        _store.Bind(transaction);
    }
}
