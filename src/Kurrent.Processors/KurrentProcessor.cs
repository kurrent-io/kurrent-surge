using Kurrent.Client;
using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Client.Streams;
using Kurrent.Processors.Gateway;
using Kurrent.Processors.Options;

namespace Kurrent.Processors;

/// <summary>
/// Entry point for configuring and running a Kurrent processor. Use one of the
/// <c>Create</c> factories to produce a configured runner, then call
/// <see cref="RunAsync"/> or <see cref="RunWithGracefulShutdownAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// The processor instance is supplied explicitly — either as a prebuilt
/// <see cref="IRecordProcessor"/> or as an <see cref="Action{T}"/> callback that composes a
/// <see cref="MessageProcessor"/> for inline typed handlers.
/// </para>
/// <para>
/// Each runner owns its own <see cref="KurrentClient"/> — client instances are never
/// shared across processors.
/// </para>
/// <para>
/// <see cref="KurrentProcessor"/> is standalone: it does not depend on
/// <c>IServiceProvider</c> or <c>IConfiguration</c>. The caller must populate
/// <see cref="ProcessorOptions.ClientOptions"/> via <c>opts.Connection(...)</c> before
/// <see cref="Build"/>. For DI-hosted apps the <c>AddKurrentProcessor</c> extension
/// handles connection resolution from <c>IConfiguration</c> and registered
/// <see cref="KurrentClientOptions"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Class-based processor
/// await KurrentProcessor.Create(new OrderProcessor(), opts => {
///     opts.Connection("kurrentdb://localhost:2113");
///     opts.Subscription.Filter = RecordFilter.ByStream("orders");
/// }).RunAsync(ct);
///
/// // Inline handlers
/// await KurrentProcessor.Create(
///     handlers => handlers
///         .Process&lt;OrderPlaced&gt;(async (msg, ctx) => { /* ... */ })
///         .Process&lt;OrderShipped&gt;((msg, ctx) => { /* sync */ }),
///     opts => opts.Connection("kurrentdb://localhost:2113")
/// ).RunAsync(ct);
/// </code>
/// </example>
[PublicAPI]
public sealed class KurrentProcessor {
    readonly ProcessorOptions _options;
    readonly IRecordProcessor _processor;

    internal KurrentProcessor(ProcessorOptions options, IRecordProcessor processor) {
        _options   = options;
        _processor = processor;
    }

    /// <summary>
    /// Creates a processor from a prebuilt <see cref="IRecordProcessor"/>.
    /// </summary>
    public static KurrentProcessor Create(IRecordProcessor processor, ProcessorOptions options) {
        ArgumentNullException.ThrowIfNull(processor);
        return new KurrentProcessor(options, processor);
    }
    
    public static KurrentProcessor Create(IRecordProcessor processor, Action<ProcessorOptions>? configure = null) {
        ArgumentNullException.ThrowIfNull(processor);

        var options = new ProcessorOptions();
        configure?.Invoke(options);
        return Create(processor, options);
    }

    /// <summary>
    /// Creates a processor using an inline <see cref="MessageProcessor"/> composed via
    /// the <paramref name="handlers"/> callback.
    /// </summary>
    public static KurrentProcessor Create(Action<MessageProcessor> handlers, Action<ProcessorOptions>? configure = null) {
        ArgumentNullException.ThrowIfNull(handlers);

        var processor = new MessageProcessor();
        handlers(processor);
        return Create(processor, configure);
    }

    /// <summary>
    /// Builds the processor drone for runtime access and control. Validates options and
    /// constructs the drone without starting it. Throws if
    /// <see cref="ProcessorOptions.ClientOptions"/> has not been set.
    /// </summary>
    internal IDrone Build() {
        if (string.IsNullOrWhiteSpace(_options.ProcessorId))
            _options.ProcessorId = _processor.GetType().Name.ToSnakeCase();

        if (_options.ClientOptions is null)
            throw new InvalidOperationException(
                $"No connection configured for processor '{_options.ProcessorId}'. " +
                "Call opts.Connection(...) before building the processor.");

        _options.EnsureValid();

        var client = new KurrentClient(_options.ClientOptions);
        var source = new SubscriptionSource(client, _options, TimeProvider.System);

        return new Drone(
            _options,
            source,
            _processor,
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
