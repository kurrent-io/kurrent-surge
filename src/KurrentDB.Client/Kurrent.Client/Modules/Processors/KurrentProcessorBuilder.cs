namespace Kurrent.Client.Streams;

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
///     .WithBatching(new BatchingSettings { MaxSize = 200, Timeout = 1000 })
///     .WithBufferCapacity(5000)
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
    internal KurrentProcessorBuilder(IRecordProcessorFactory processorFactory) {
        GetProcessor = () => {
            try {
                return processorFactory.CreateProcessor();
            }
            catch (Exception ex) {
                throw new FailedToCreateWorkerException(ex);
            }
        };

        ProcessorSettings  = new ProcessorSettings();
        GetClientOptions   = () => new KurrentClientOptions();
    }

    Func<IRecordProcessor>     GetProcessor       { get; set; }
    ProcessorSettings          ProcessorSettings  { get; set; }
    KurrentClient?             ProvidedClient     { get; set; }
    Func<KurrentClientOptions> GetClientOptions   { get; set; }

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
    /// <example>
    /// <code>
    /// .WithClient("kurrentdb://localhost:2113", opts => opts with {
    ///     Resilience = KurrentClientResilienceOptions.HighAvailability
    /// })
    /// </code>
    /// </example>
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
    /// <example>
    /// <code>
    /// await KurrentProcessors.Create(new MyProcessor())
    ///     .ConnectTo("kurrentdb://localhost:2113")
    ///     .WithProcessorId("order-processor")
    ///     .RunAsync(stoppingToken);
    /// </code>
    /// </example>
    public KurrentProcessorBuilder WithProcessorId(string processorId) {
        ProcessorSettings = ProcessorSettings with { ProcessorId = processorId };
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
    /// <example>
    /// <code>
    /// await KurrentProcessors.Create(new MyProcessor())
    ///     .ConnectTo("kurrentdb://localhost:2113")
    ///     .WithProcessorId("order-processor")
    ///     .WithWorkerId(Environment.MachineName)
    ///     .RunAsync(stoppingToken);
    /// </code>
    /// </example>
    public KurrentProcessorBuilder WithWorkerId(string workerId) {
        ProcessorSettings = ProcessorSettings with { WorkerId = workerId };
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
    /// Similar to Flink's <c>setMaxParallelism()</c>.
    /// </para>
    /// </remarks>
    public KurrentProcessorBuilder WithMaxParallelism(int maxParallelism) {
        ProcessorSettings = ProcessorSettings with { MaxParallelism = maxParallelism };
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
    /// <example>
    /// <code>
    /// .WithFilter(RecordFilter.ByStream("orders"))
    /// .WithFilter(RecordFilter.ByStreamPrefix("tenant-"))
    /// .WithFilter(RecordFilter.BySchema("OrderPlaced"))
    /// </code>
    /// </example>
    public KurrentProcessorBuilder WithFilter(IRecordFilter filter) {
        ProcessorSettings = ProcessorSettings with {
            Subscription = ProcessorSettings.Subscription with { Filter = filter }
        };
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
    /// <example>
    /// <code>
    /// .WithFilterExpression("stream_id LIKE 'orders-%'")
    /// .WithFilterExpression("category = 'orders'")
    /// </code>
    /// </example>
    public KurrentProcessorBuilder WithFilterExpression(string filterExpression) {
        // Create an ExpressionFilter from the expression string to enable server-side filtering
        var filter = new ExpressionFilter(filterExpression);
        ProcessorSettings = ProcessorSettings with {
            Subscription = ProcessorSettings.Subscription with {
                FilterExpression = filterExpression,
                Filter = filter
            }
        };
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
        ProcessorSettings = ProcessorSettings with {
            Subscription = ProcessorSettings.Subscription with { PartitionKeySelector = selector }
        };
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
    /// Use <see cref="WithStartPosition"/> to override any existing checkpoint.
    /// </para>
    /// </remarks>
    public KurrentProcessorBuilder WithInitialPosition(SubscriptionInitialPosition position) {
        ProcessorSettings = ProcessorSettings with {
            Subscription = ProcessorSettings.Subscription with { InitialPosition = position }
        };
        return this;
    }

    /// <summary>
    /// Forces processing to start from a specific position, ignoring any checkpoint.
    /// </summary>
    /// <param name="position">The log position to start from.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// When set, any existing checkpoint will be ignored and processing will begin from
    /// the specified position.
    /// </para>
    /// </remarks>
    public KurrentProcessorBuilder WithStartPosition(long position) {
        ProcessorSettings = ProcessorSettings with {
            Subscription = ProcessorSettings.Subscription with { StartPosition = position }
        };
        return this;
    }

    /// <summary>
    /// Configures record batching for processing.
    /// </summary>
    /// <param name="settings">The batching configuration.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Batching controls how records are grouped before delivery to the processor.
    /// Smaller batches reduce latency; larger batches improve throughput.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// .WithBatching(new BatchingSettings { MaxSize = 200, Timeout = 1000 })
    /// </code>
    /// </example>
    public KurrentProcessorBuilder WithBatching(BatchingSettings settings) {
        ProcessorSettings = ProcessorSettings with { Batching = settings };
        return this;
    }

    /// <summary>
    /// Configures checkpoint settings for fault tolerance.
    /// </summary>
    /// <param name="settings">The checkpoint configuration.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Checkpoints enable recovery from failures by periodically saving progress.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// .WithCheckpoint(new CheckpointSettings { Interval = 60000, Threshold = 5000 })
    /// </code>
    /// </example>
    public KurrentProcessorBuilder WithCheckpoint(CheckpointSettings settings) {
        ProcessorSettings = ProcessorSettings with { Checkpoint = settings };
        return this;
    }

    /// <summary>
    /// Sets the buffer capacity for record buffering.
    /// </summary>
    /// <param name="capacity">Buffer capacity in records (default: 1000, max: 10000).</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Larger buffers improve throughput but increase memory usage.
    /// The buffer capacity should be at least as large as the batch size.
    /// </para>
    /// </remarks>
    public KurrentProcessorBuilder WithBufferCapacity(int capacity) {
        ProcessorSettings = ProcessorSettings with { BufferCapacity = capacity };
        return this;
    }

    /// <summary>
    /// Skips record deserialization for raw byte access.
    /// </summary>
    /// <param name="skip">True to skip decoding (default: true when called).</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Use when processing metadata only or with custom deserialization.
    /// Reduces CPU usage in high-throughput scenarios.
    /// </para>
    /// </remarks>
    public KurrentProcessorBuilder WithSkipDecoding(bool skip = true) {
        ProcessorSettings = ProcessorSettings with { SkipDecoding = skip };
        return this;
    }

    /// <summary>
    /// Provides full control over processor settings.
    /// </summary>
    /// <param name="configure">Function to configure processor settings.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Use this method for advanced processing configuration that isn't covered
    /// by the individual property methods.
    /// </para>
    /// </remarks>
    public KurrentProcessorBuilder ConfigureProcessing(Func<ProcessorSettings, ProcessorSettings> configure) {
        ProcessorSettings = configure(ProcessorSettings);
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
    /// <example>
    /// <code>
    /// var worker = KurrentProcessors.Create(new MyProcessor())
    ///     .ConnectTo("kurrentdb://localhost:2113")
    ///     .WithProcessorId("order-processor")
    ///     .Build();
    ///
    /// Console.WriteLine($"Status: {worker.Status}");
    /// await worker.RunAsync(stoppingToken);
    /// </code>
    /// </example>
    public IProcessorWorker Build() {
        var options    = GetClientOptions();
        var ownsClient = ProvidedClient is null;
        var client     = ProvidedClient ?? new KurrentClient(options);
        return new ProcessorWorker(client, GetProcessor(), ProcessorSettings, options.LoggerFactory, ownsClient);
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
        await using var worker = Build();
        await worker.RunAsync(stoppingToken).ConfigureAwait(false);
    }
}
