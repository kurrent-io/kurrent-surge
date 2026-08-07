using DuckDB.NET.Data;
using Kurrent.Quack;
using Kurrent.Projectors.Transactional;
using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors.DuckDB;

[PublicAPI]
public static class DuckDBProjector {
    public static KurrentProcessor Create(
        ProjectionProcessor<DuckDBAdvancedConnection> projection,
        Action<DuckDBProjectorOptions>? configure = null
    ) =>
        Create(_ => projection, configure);

    public static KurrentProcessor Create(
        Func<DuckDBProjectionTransaction, ProjectionProcessor<DuckDBAdvancedConnection>> build,
        Action<DuckDBProjectorOptions>? configure = null
    ) {
        var options = new DuckDBProjectorOptions();
        configure?.Invoke(options);

        if (options.ClientProvider is null)
            throw new InvalidOperationException(
                "No DuckDB connection configured. Set ClientProvider before running.");

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
            provider,
            new DuckDBProjectionCheckpointStore(options.ProjectionId, options.Time));

        return KurrentProcessor.Create(projection, options.ToProcessorOptions());
    }
}
