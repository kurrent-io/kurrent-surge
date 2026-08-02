using DuckDB.NET.Data;
using Kurrent.Quack;
using Kurrent.Projectors.Transactional;
using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors.DuckDB;

/// <summary>
/// Builds a runnable processor from a DuckDB projection.
/// </summary>
/// <remarks>
/// This is the only place a projection gets wired, and that is deliberate: the wiring is what makes the
/// projection its own checkpoint owner. A projection registered through the generic processor entry
/// points would run with the drone's default KurrentDB checkpoint store, putting its positions in one
/// database and its projected state in another — the failure this whole design exists to prevent. A
/// projection built here throws on startup if it somehow reaches a drone unwired.
/// </remarks>
[PublicAPI]
public static class DuckDBProjector {
    /// <summary>
    /// Builds a projector for a projection that already exists — the common case, for every projection
    /// that does not need a store bound to its own transaction.
    /// </summary>
    /// <param name="projection">The projection to wire and run.</param>
    /// <param name="configure">
    /// Configures <see cref="DuckDBProjectorOptions"/>, in particular <see cref="ProjectorOptions{TClient}.ClientProvider"/>.
    /// </param>
    /// <returns>A <see cref="KurrentProcessor"/> ready to run.</returns>
    public static KurrentProcessor Create(
        ProjectionProcessor<DuckDBAdvancedConnection> projection,
        Action<DuckDBProjectorOptions>? configure = null
    ) =>
        Create(_ => projection, configure);

    /// <summary>
    /// Builds a projector for a projection that needs the transaction before it exists — a document store,
    /// which has to read and write on the transaction's own connection rather than borrowing one of its own.
    /// </summary>
    /// <param name="build">Builds the projection from the transaction it will be wired with.</param>
    /// <param name="configure">
    /// Configures <see cref="DuckDBProjectorOptions"/>, in particular <see cref="ProjectorOptions{TClient}.ClientProvider"/>.
    /// </param>
    /// <returns>A <see cref="KurrentProcessor"/> ready to run.</returns>
    /// <remarks>
    /// The transaction reaches the projection through the factory rather than through the options so there
    /// is only ever one: a store handed a transaction the projection was not wired with would commit on a
    /// different connection, and nothing would report it.
    /// </remarks>
    public static KurrentProcessor Create(
        Func<IProjectionTransaction<DuckDBAdvancedConnection>, ProjectionProcessor<DuckDBAdvancedConnection>> build,
        Action<DuckDBProjectorOptions>? configure = null
    ) {
        var options = new DuckDBProjectorOptions();
        configure?.Invoke(options);

        if (options.ClientProvider is null)
            throw new InvalidOperationException(
                "No DuckDB connection configured. Set ClientProvider before running.");

        // The transaction borrows and returns its connection rather than just asking for one, which only
        // IDuckDBConnectionProvider can express — GetClient alone has no way to hand anything back, so a
        // delegate-backed provider from ResolveClient cannot satisfy this backend.
        if (options.ClientProvider is not IDuckDBConnectionProvider provider)
            throw new InvalidOperationException(
                $"ClientProvider must implement {nameof(IDuckDBConnectionProvider)}, but was "
              + $"{options.ClientProvider.GetType().Name}. Assign a provider directly rather than "
              + $"calling ResolveClient(...), which builds a delegate-backed provider this backend "
              + "cannot borrow from.");

        var transaction = new DuckDBProjectionTransaction(provider);
        var projection  = build(transaction);

        if (string.IsNullOrWhiteSpace(options.ProjectionId))
            options.ProjectionId = projection.GetType().Name;

        projection.Wire(
            options.ProjectionId,
            transaction,
            new DuckDBProjectionCheckpointStore(options.ProjectionId, options.Time));

        return KurrentProcessor.Create(projection, options.ToProcessorOptions());
    }
}
