using Kurrent.Processors;
using Kurrent.Projections.DuckDB;

namespace Kurrent.Projections;

/// <summary>
/// Entry point for configuring and running a projection. Internally wraps a <see cref="KurrentProcessor"/>
/// so the processor vocabulary stays out of user-facing code.
/// </summary>
[PublicAPI]
public sealed class KurrentProjector {
    readonly KurrentProcessor _inner;

    internal KurrentProjector(KurrentProcessor inner) => _inner = inner;

    /// <summary>
    /// Runs the projection until cancellation is requested.
    /// </summary>
    public ValueTask RunAsync(CancellationToken stoppingToken = default) =>
        _inner.RunAsync(stoppingToken);

    /// <summary>
    /// Runs the projection with automatic graceful shutdown on SIGINT/SIGTERM.
    /// </summary>
    public ValueTask RunWithGracefulShutdownAsync(TimeSpan? shutdownTimeout = null) =>
        _inner.RunWithGracefulShutdownAsync(shutdownTimeout);

    /// <summary>
    /// Creates a projector from any <see cref="Projection{TClient}"/>. The caller must configure
    /// <see cref="ProjectorOptions{TClient}.ResolveClient"/>.
    /// </summary>
    public static KurrentProjector Create<TClient>(
        Projection<TClient> projection,
        Action<ProjectorOptions<TClient>>? configure = null
    ) {
        ArgumentNullException.ThrowIfNull(projection);

        var opts = new ProjectorOptions<TClient>();
        configure?.Invoke(opts);

        return BuildFrom(projection, opts);
    }

    /// <summary>
    /// Creates a projector from a <see cref="DuckDBProjection"/>. Accepts <see cref="DuckDBProjectorOptions"/>
    /// as the options surface for DuckDB-specific extensibility.
    /// </summary>
    public static KurrentProjector Create(
        DuckDBProjection projection,
        Action<DuckDBProjectorOptions>? configure = null
    ) {
        ArgumentNullException.ThrowIfNull(projection);

        var opts = new DuckDBProjectorOptions();
        configure?.Invoke(opts);

        return BuildFrom(projection, opts);
    }

    static KurrentProjector BuildFrom<TClient>(Projection<TClient> projection, ProjectorOptions<TClient> opts) {
        opts.EnsureValid();
        projection.UseClientResolver(opts.ResolveClient!);
        return new KurrentProjector(KurrentProcessor.Create(projection, opts));
    }
}
