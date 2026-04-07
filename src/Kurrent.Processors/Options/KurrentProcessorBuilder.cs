using Kurrent.Client;
using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Client.Streams;
using Kurrent.Processors.Diagnostics;
using Kurrent.Processors.Models;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Processors.Options;

/// <summary>
/// Fluent builder for configuring processor execution.
/// </summary>
/// <remarks>
/// <para>
/// Provides a fluent API for configuring processing options, client options, and
/// processor instances before executing the processing operation with <see cref="RunAsync"/>.
/// </para>
/// <para>
/// Start with <see cref="KurrentProcessors.Create(IRecordProcessor)"/> or
/// <see cref="KurrentProcessors.Create(IRecordProcessorFactory)"/> to create a builder instance.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Simple fire-and-forget
/// await KurrentProcessors.Create(new MyProcessor())
///     .ConnectTo("kurrentdb://localhost:2113")
///     .WithProcessorId("order-processor")
///     .RunAsync(stoppingToken);
///
/// // With processor factory and full configuration
/// await KurrentProcessors.Create(processorFactory)
///     .WithClient("kurrentdb://admin:changeit@localhost:2113?tls=true", opts => opts with {
///         Resilience = KurrentClientResilienceOptions.HighAvailability
///     })
///     .WithProcessorId("order-processor")
///     .WithFilter(RecordFilter.ByStream("orders"))
///     .WithInitialPosition(SubscriptionInitialPosition.Earliest)
///     .ConfigureBatching(b => b.MaxSize(200).Timeout(TimeSpan.FromSeconds(1)))
///     .ConfigureWorker(w => w.BufferCapacity(5000))
///     .RunAsync(stoppingToken);
///
/// // Advanced: Get worker for runtime access
/// var worker = KurrentProcessors.Create(new MyProcessor())
///     .ConnectTo("kurrentdb://localhost:2113")
///     .WithProcessorId("order-processor")
///     .Build();
///
/// Console.WriteLine($"Status: {worker.Status}");
/// await worker.RunAsync(stoppingToken);
/// </code>
/// </example>
[PublicAPI]
public sealed class KurrentProcessorBuilder {
    string?                  _processorTypeName;
    ProcessorSettingsBuilder _settingsBuilder;

    internal KurrentProcessorBuilder(IRecordProcessorFactory processorFactory, string? processorTypeName) {
        _processorTypeName = processorTypeName;

        _settingsBuilder = new ProcessorSettingsBuilder();

        GetProcessor = () => {
            try {
                return processorFactory.CreateProcessor();
            }
            catch (Exception ex) {
                throw new KurrentWorkerException("WORKER_CREATION_FAILED", "Failed to create record processor instance.", ex);
            }
        };

        GetClientOptions = () => new KurrentClientOptions();
    }

    Func<IRecordProcessor> GetProcessor { get; set; }

    KurrentClient? ProvidedClient { get; set; }

    Func<KurrentClientOptions> GetClientOptions { get; set; }

    RecordPosition StartPosition { get; set; } = RecordPosition.Unset;

    /// <summary>
    /// Sets the connection string and configures client options in a single call.
    /// </summary>
    /// <param name="connectionString">
    /// KurrentDB connection string (e.g., "kurrentdb://localhost:2113" or "kurrentdb://admin:changeit@localhost:2113?tls=true").
    /// </param>
    /// <param name="configure">Function to configure client options derived from the connection string.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// The configure function receives the options parsed from the connection string, allowing
    /// modifications to the connection-derived settings.
    /// </para>
    /// <para>
    /// This method invalidates any previously provided client instance.
    /// </para>
    /// </remarks>
    public KurrentProcessorBuilder WithClient(string connectionString, Func<KurrentClientOptions, KurrentClientOptions> configure) {
        GetClientOptions = () => configure(KurrentClientOptions.Parse(connectionString));
        ProvidedClient   = null;
        return this;
    }

    /// <summary>
    /// Sets pre-configured client options for connecting to KurrentDB.
    /// </summary>
    /// <param name="options">The pre-configured client options.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method invalidates any previously provided client instance.
    /// </para>
    /// </remarks>
    public KurrentProcessorBuilder WithClient(KurrentClientOptions options) {
        GetClientOptions = () => options;
        ProvidedClient   = null;
        return this;
    }

    /// <summary>
    /// Uses an existing KurrentClient instance for processing.
    /// </summary>
    /// <param name="client">The client instance to use.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// When using a provided client instance, the runner will NOT dispose it after processing completes.
    /// The caller is responsible for the client's lifecycle.
    /// </para>
    /// <para>
    /// This method invalidates any previously configured client options via <see cref="ConnectTo"/> or
    /// <see cref="WithClient(string, Func{KurrentClientOptions, KurrentClientOptions})"/>.
    /// </para>
    /// </remarks>
    public KurrentProcessorBuilder UseClient(KurrentClient client) {
        GetClientOptions = () => client.Options;
        ProvidedClient   = client;
        return this;
    }

    /// <summary>
    /// Sets the connection string for connecting to KurrentDB.
    /// <remarks>
    /// <para>
    /// Any previously set client options will be overridden, and any provided client
    /// instance will be ignored.
    /// </para>
    /// </remarks>
    /// </summary>
    /// <param name="connectionString">
    /// KurrentDB connection string (e.g., "kurrentdb://localhost:2113" or "kurrentdb://admin:changeit@localhost:2113?tls=true").
    /// </param>
    /// <returns>The runner instance for method chaining.</returns>
    public KurrentProcessorBuilder ConnectTo(string connectionString) => WithClient(connectionString, options => options);

    /// <summary>
    /// Loads settings from an <see cref="IConfiguration"/> section based on the processor type name.
    /// </summary>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// The configuration section is determined by the processor type name.
    /// For example, if the processor type is <c>TransferProcessor</c>, it will load from
    /// <c>Kurrent:Processors:TransferProcessor</c>.
    /// </para>
    /// <para>
    /// If <c>ProcessorId</c> is not set in config, it will be auto-generated from the processor type name
    /// (e.g., <c>TransferProcessor</c> becomes <c>transfer_processor</c>).
    /// </para>
    /// <para>
    /// Settings loaded from configuration can be overridden by subsequent fluent method calls.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when using <see cref="KurrentProcessors.Create(IRecordProcessorFactory)"/> since the
    /// processor type is not known at builder creation time.
    /// </exception>
    public KurrentProcessorBuilder FromConfiguration(IConfiguration configuration) {
        if (_processorTypeName is null)
            throw new InvalidOperationException(
                "FromConfiguration is not supported when using IRecordProcessorFactory. " +
                "Use KurrentProcessors.Create(processor) instead of Create(factory).");

        _settingsBuilder.FromConfiguration(configuration, _processorTypeName);
        return this;
    }

    /// <summary>
    /// Enables checkpointing for resumable processing with the specified processor ID.
    /// </summary>
    /// <param name="processorId">The processor group name for checkpoint storage.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Checkpointing enables resumable processing by persisting progress to KurrentDB.
    /// When the processor restarts, it resumes from the last committed checkpoint rather
    /// than reprocessing from the beginning.
    /// </para>
    /// <para>
    /// <b>Processor Groups:</b> Multiple workers with the same processor ID share checkpoints,
    /// enabling coordinated processing across instances.
    /// </para>
    /// </remarks>
    public KurrentProcessorBuilder WithProcessorId(string processorId) {
        _settingsBuilder.WithProcessorId(processorId);
        return this;
    }

    /// <summary>
    /// Sets the unique identifier for this worker instance within the processor group.
    /// </summary>
    /// <param name="workerId">The worker identifier.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Used for checkpoint attribution. For sticky partition assignment,
    /// use a consistent identifier like <see cref="Environment.MachineName"/>.
    /// </para>
    /// <para>
    /// <b>Note:</b> Only used when <see cref="WithProcessorId"/> is also called.
    /// </para>
    /// </remarks>
    public KurrentProcessorBuilder WithWorkerId(string workerId) {
        _settingsBuilder.WithWorkerId(workerId);
        return this;
    }

    /// <summary>
    /// Sets the maximum number of partitions (key groups) for parallel processing.
    /// </summary>
    /// <param name="maxParallelism">Maximum parallelism (1-32768, default: 128).</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Determines the upper bound for parallel processing.
    /// </para>
    /// </remarks>
    public KurrentProcessorBuilder WithMaxParallelism(int maxParallelism) {
        _settingsBuilder.WithMaxParallelism(maxParallelism);
        return this;
    }

    /// <summary>
    /// Sets the record filter for processing using an <see cref="IRecordFilter"/>.
    /// </summary>
    /// <param name="filter">The filter to apply to records.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Filters reduce data transfer and processing overhead by targeting specific streams,
    /// schemas, or metadata properties.
    /// </para>
    /// </remarks>
    public KurrentProcessorBuilder WithFilter(IRecordFilter filter) {
        _settingsBuilder.WithFilter(filter);
        return this;
    }

    /// <summary>
    /// Sets the filter expression string for server-side filtering.
    /// </summary>
    /// <param name="filterExpression">Filter expression (e.g., <c>"$all"</c>, <c>"stream_id LIKE 'orders-%'"</c>).</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Use this for configuration-based filtering. For programmatic filtering,
    /// prefer <see cref="WithFilter(IRecordFilter)"/>.
    /// </para>
    /// </remarks>
    public KurrentProcessorBuilder WithFilterExpression(string filterExpression) {
        _settingsBuilder.WithFilterExpression(filterExpression);
        return this;
    }

    /// <summary>
    /// Sets the path to extract partition key from records.
    /// </summary>
    /// <param name="selector">Path expression (e.g., <c>"stream_id"</c>, <c>"data.customer_id"</c>).</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Required when <see cref="WithMaxParallelism"/> > 1.
    /// </para>
    /// </remarks>
    public KurrentProcessorBuilder WithPartitionKeySelector(string selector) {
        _settingsBuilder.WithPartitionKeySelector(selector);
        return this;
    }

    /// <summary>
    /// Sets the initial position for processing when no checkpoint exists.
    /// </summary>
    /// <param name="position">The initial position (Earliest or Latest).</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This setting only applies when there is no stored checkpoint.
    /// </para>
    /// </remarks>
    public KurrentProcessorBuilder WithInitialPosition(SubscriptionInitialPosition position) {
        _settingsBuilder.WithInitialPosition(position);
        return this;
    }

    /// <summary>
    /// Configures checkpoint settings using a fluent builder.
    /// </summary>
    /// <param name="configure">Action to configure the checkpoint settings builder.</param>
    /// <returns>The runner instance for method chaining.</returns>
    public KurrentProcessorBuilder ConfigureCheckpointing(Action<CheckpointSettingsBuilder> configure) {
        _settingsBuilder.ConfigureCheckpointing(configure);
        return this;
    }

    /// <summary>
    /// Configures batching settings using a fluent builder.
    /// </summary>
    /// <param name="configure">Action to configure the batching settings builder.</param>
    /// <returns>The runner instance for method chaining.</returns>
    public KurrentProcessorBuilder ConfigureBatching(Action<BatchingSettingsBuilder> configure) {
        _settingsBuilder.ConfigureBatching(configure);
        return this;
    }

    /// <summary>
    /// Configures subscription settings using a fluent builder.
    /// </summary>
    /// <param name="configure">Action to configure the subscription settings builder.</param>
    /// <returns>The runner instance for method chaining.</returns>
    public KurrentProcessorBuilder ConfigureSubscription(Action<SubscriptionSettingsBuilder> configure) {
        _settingsBuilder.ConfigureSubscription(configure);
        return this;
    }

    /// <summary>
    /// Configures worker settings using a fluent builder.
    /// </summary>
    /// <param name="configure">Action to configure the worker settings builder.</param>
    /// <returns>The runner instance for method chaining.</returns>
    public KurrentProcessorBuilder ConfigureWorker(Action<WorkerSettingsBuilder> configure) {
        _settingsBuilder.ConfigureWorker(configure);
        return this;
    }

    /// <summary>
    /// Sets the position in the log from which the processor will begin reading records, effectively resetting all existing checkpoints.
    /// </summary>
    /// <param name="startPosition">The log position to start processing from.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// This is a destructive operation that resets all existing checkpoints to the specified position,
    /// that will act as a high watermark for processing.
    /// Use with caution as it can lead to data loss or duplicate processing if used incorrectly.
    /// </remarks>
    public KurrentProcessorBuilder StartFrom(RecordPosition startPosition) {
        if (startPosition == RecordPosition.Unset)
            throw new ArgumentException("Start position cannot be Unset.", nameof(startPosition));

        StartPosition = startPosition;
        return this;
    }

    /// <summary>
    /// Builds a processor worker for runtime access and control.
    /// </summary>
    /// <returns>An <see cref="IProcessorWorker"/> instance for monitoring and control.</returns>
    /// <remarks>
    /// <para>
    /// Use this method when you need access to worker state, metrics, or checkpoints during execution.
    /// The returned worker must be started with <see cref="IProcessorWorker.RunAsync"/>.
    /// </para>
    /// </remarks>
    public IProcessorWorker Build() {
        var recordProcessor = GetProcessor();

        var settings = _settingsBuilder.Settings;

        // Auto-generate WorkerId if not explicitly set.
        if (string.IsNullOrWhiteSpace(settings.WorkerId)) {
            _settingsBuilder.WithWorkerId(PunyId.New());
            settings = _settingsBuilder.Settings;
        }

        // Auto-generate ProcessorId from processor type name if not explicitly set.
        if (string.IsNullOrWhiteSpace(settings.ProcessorId)) {
            var processorId = recordProcessor.GetType().Name.ToSnakeCase();
            _settingsBuilder.WithProcessorId(processorId);
            settings = _settingsBuilder.Settings;
        }

        if (StartPosition != RecordPosition.Unset) {
            _settingsBuilder.ConfigureSubscription(s => s with { StartPosition = StartPosition });
            settings = _settingsBuilder.Settings;
        }

        // Validate processor settings
        settings.EnsureOptionsAreValid();

        var options    = GetClientOptions();
        var ownsClient = ProvidedClient is null;
        var client     = ProvidedClient ?? new KurrentClient(options);

        return new ProcessorWorker(
            settings,
            client,
            recordProcessor,
            options.LoggerFactory,
            TimeProvider.System,
            ownsClient
        );
    }

    /// <summary>
    /// Executes the processor as a fire-and-forget operation.
    /// </summary>
    /// <param name="stoppingToken">Token to request cancellation of processing.</param>
    /// <returns>A task that completes when processing finishes or is cancelled.</returns>
    /// <remarks>
    /// <para>
    /// This is the simplest way to run a processor. For runtime access to status,
    /// use <see cref="Build"/> instead.
    /// </para>
    /// </remarks>
    public async ValueTask RunAsync(CancellationToken stoppingToken = default) {
        var worker = Build();
        await worker.RunAsync(stoppingToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the processor with automatic graceful shutdown on SIGINT/SIGTERM.
    /// </summary>
    /// <param name="shutdownTimeout">
    /// Maximum time to wait for graceful shutdown after receiving a termination signal.
    /// Defaults to 30 seconds if not specified.
    /// </param>
    /// <returns>A task that completes when the processor has stopped.</returns>
    /// <remarks>
    /// <para>
    /// This method automatically handles process termination signals (Ctrl+C, SIGTERM)
    /// and initiates graceful shutdown. Use this for console applications that don't
    /// have their own host lifecycle management.
    /// </para>
    /// <para>
    /// For ASP.NET Core applications, use <see cref="RunAsync"/> with the host's
    /// <see cref="CancellationToken"/> instead.
    /// </para>
    /// </remarks>
    public async ValueTask RunWithGracefulShutdownAsync(TimeSpan? shutdownTimeout = null) {
        var timeout          = shutdownTimeout ?? TimeSpan.FromSeconds(30);
        var cancellator      = new CancellationTokenSource();
        var shutdownComplete = new ManualResetEventSlim(false);

        // Handle Ctrl+C (SIGINT)
        Console.CancelKeyPress += OnCancelKeyPress;

        // Handle SIGTERM (process termination)
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        try {
            await RunAsync(cancellator.Token).ConfigureAwait(false);
        }
        finally {
            Console.CancelKeyPress -= OnCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

            // Signal that shutdown is complete before disposing
            shutdownComplete.Set();
            cancellator.Dispose();
            shutdownComplete.Dispose();
        }

        return;

        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e) {
            e.Cancel = true; // Prevent immediate termination
            try { cancellator.Cancel(); } catch (ObjectDisposedException) { }
        }

        void OnProcessExit(object? sender, EventArgs e) {
            try { cancellator.Cancel(); } catch (ObjectDisposedException) { }

            // Block ProcessExit until shutdown completes or times out
            // This gives the processor time to complete gracefully
            try { shutdownComplete.Wait(timeout); } catch (ObjectDisposedException) { }
        }
    }
}
