using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Client.Streams;
using Kurrent.Processors.Gateway;
using Kurrent.Processors.Options;

namespace Kurrent.Processors;

/// <summary>
/// Runs a configured processor. Returned by <see cref="KurrentProcessor.Create"/>
/// and <see cref="KurrentPlatform.Processor"/>.
/// </summary>
[PublicAPI]
public sealed class KurrentProcessorRunner {
    readonly ProcessorOptions _options;

    internal KurrentProcessorRunner(ProcessorOptions options) => _options = options;

    /// <summary>
    /// Builds a processor drone for runtime access and control.
    /// </summary>
    public IDrone Build() {
        var processor = _options.Subscribe.ResolveProcessor();

        // Auto-generate ProcessorId if not set; the setter derives DroneId for us.
        if (string.IsNullOrWhiteSpace(_options.ProcessorId))
            _options.ProcessorId = processor.GetType().Name.ToSnakeCase();

        _options.EnsureValid();

        var ownsClient = _options.OwnsClient;
        var client     = _options.BuildClient();
        var source     = new SubscriptionSource(client, _options, TimeProvider.System, ownsClient);

        return new Drone(
            _options,
            source,
            processor,
            client.Options.LoggerFactory,
            TimeProvider.System
        );
    }

    /// <summary>
    /// Runs the processor until cancellation is requested.
    /// </summary>
    public async ValueTask RunAsync(CancellationToken stoppingToken = default) {
        var drone = Build();
        await drone.RunAsync(stoppingToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the processor with automatic graceful shutdown on SIGINT/SIGTERM.
    /// </summary>
    public async ValueTask RunWithGracefulShutdownAsync(TimeSpan? shutdownTimeout = null) {
        var timeout          = shutdownTimeout ?? TimeSpan.FromSeconds(30);
        var cancellator      = new CancellationTokenSource();
        var shutdownComplete = new ManualResetEventSlim(false);

        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        try {
            await RunAsync(cancellator.Token).ConfigureAwait(false);
        }
        finally {
            Console.CancelKeyPress -= OnCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

            shutdownComplete.Set();
            cancellator.Dispose();
            shutdownComplete.Dispose();
        }

        return;

        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e) {
            e.Cancel = true;
            try { cancellator.Cancel(); } catch (ObjectDisposedException) { }
        }

        void OnProcessExit(object? sender, EventArgs e) {
            try { cancellator.Cancel(); } catch (ObjectDisposedException) { }
            try { shutdownComplete.Wait(timeout); } catch (ObjectDisposedException) { }
        }
    }
}
