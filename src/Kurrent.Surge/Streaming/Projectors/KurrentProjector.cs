using Kurrent.Projectors.Transactional;
using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors;

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
    /// Wraps an already-wired projection. A backend's own projector builds the transaction and the
    /// checkpoint storage and calls <c>Wire</c> before handing the projection here — this type only
    /// keeps the processor vocabulary out of user-facing code.
    /// </summary>
    /// <remarks>
    /// A projection that arrives unwired is not rejected here; it throws from <c>OnInitialize</c>, which
    /// is the one place that can tell the difference and the last moment before it would silently run
    /// against the framework's own checkpoint store.
    /// </remarks>
    public static KurrentProjector Create<TClient>(
        ProjectionProcessor<TClient> projection,
        ProjectorOptions<TClient> options
    ) where TClient : class {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(options);

        options.EnsureValid();

        return new KurrentProjector(KurrentProcessor.Create(projection, options.ToProcessorOptions()));
    }
}
