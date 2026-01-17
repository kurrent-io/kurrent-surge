using Microsoft.Extensions.Configuration;

namespace Kurrent.Client.Streams;

/// <summary>
/// Fluent builder for configuring a processor's hosting options.
/// Used with <see cref="KurrentProcessorServiceCollectionExtensions.AddKurrentProcessor{TProcessor}"/>.
/// </summary>
/// <remarks>
/// <para>
/// This builder configures how a processor runs as a hosted service, including
/// client connection, filtering, batching, and checkpoint settings.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// services.AddKurrentProcessor&lt;MyProcessor&gt;(config => config
///     .UseClient("kurrentdb://localhost:2113")
///     .WithFilter("schema_name = 'OrderPlaced'")
///     .ConfigureCheckpoint(c => c with { Interval = 10_000 })
/// );
/// </code>
/// </example>
[PublicAPI]
public sealed class KurrentProcessorHostBuilder {
    ProcessorSettingsBuilder                          _settingsBuilder = new();
    string?                                           _connectionString;
    Func<KurrentClientOptions, KurrentClientOptions>? _clientConfigure;
    bool                                              _useOwnClient;
    string?                                           _processorTypeName;

    // ═══════════════════════════════════════════════════════════════════════════════
    // Configuration Binding
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Binds settings from configuration based on processor type name.
    /// Called internally before user configuration is applied.
    /// </summary>
    internal KurrentProcessorHostBuilder BindFromConfiguration(IConfiguration? configuration, string processorTypeName) {
        _processorTypeName = processorTypeName;
        _settingsBuilder.FromConfiguration(configuration, processorTypeName);
        return this;
    }

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
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the processor type is not available (should not happen with normal usage).
    /// </exception>
    /// <example>
    /// <code>
    /// services.AddKurrentProcessor&lt;MyProcessor&gt;(config => config
    ///     .FromConfiguration(configuration)
    ///     .WithWorkerId(Environment.MachineName)
    /// );
    /// </code>
    /// </example>
    public KurrentProcessorHostBuilder FromConfiguration(IConfiguration configuration) {
        if (_processorTypeName is null)
            throw new InvalidOperationException("Processor type not available for configuration binding.");

        _settingsBuilder.FromConfiguration(configuration, _processorTypeName);
        return this;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Client Configuration
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Uses a connection string to create a dedicated client for this processor.
    /// </summary>
    /// <param name="connectionString">
    /// KurrentDB connection string (e.g., "kurrentdb://localhost:2113").
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// When specified, this processor will use its own client instead of the shared
    /// client registered via <c>AddKurrentClient()</c>.
    /// </para>
    /// </remarks>
    public KurrentProcessorHostBuilder UseClient(string connectionString) {
        _connectionString = connectionString;
        _useOwnClient     = true;
        return this;
    }

    /// <summary>
    /// Configures a custom client for this processor with full options control.
    /// </summary>
    /// <param name="configure">Function to configure client options.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Use this when you need to customize client options beyond what the connection
    /// string provides (e.g., resilience settings, interceptors).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// .ConfigureClient(opts => opts with {
    ///     Resilience = KurrentClientResilienceOptions.HighAvailability
    /// })
    /// </code>
    /// </example>
    public KurrentProcessorHostBuilder ConfigureClient(Func<KurrentClientOptions, KurrentClientOptions> configure) {
        _clientConfigure = configure;
        _useOwnClient    = true;
        return this;
    }

    /// <summary>
    /// Uses a connection string with additional options configuration.
    /// </summary>
    /// <param name="connectionString">
    /// KurrentDB connection string (e.g., "kurrentdb://localhost:2113").
    /// </param>
    /// <param name="configure">Function to configure client options.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <example>
    /// <code>
    /// .UseClient("kurrentdb://localhost:2113", opts => opts with {
    ///     Resilience = KurrentClientResilienceOptions.HighAvailability
    /// })
    /// </code>
    /// </example>
    public KurrentProcessorHostBuilder UseClient(
        string connectionString,
        Func<KurrentClientOptions, KurrentClientOptions> configure) {
        _connectionString = connectionString;
        _clientConfigure  = configure;
        _useOwnClient     = true;
        return this;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Filter Configuration
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets a SQL-like filter expression for server-side filtering.
    /// </summary>
    /// <param name="sqlExpression">
    /// Filter expression (e.g., <c>"schema_name = 'OrderPlaced'"</c>,
    /// <c>"stream_id LIKE 'orders-%'"</c>).
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Server-side filtering reduces data transfer by filtering at the source.
    /// This is more efficient than client-side filtering for large volumes.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// .WithFilter("schema_name IN ('OrderPlaced', 'OrderShipped')")
    /// .WithFilter("stream_id LIKE 'customer-%' AND schema_name = 'CustomerCreated'")
    /// </code>
    /// </example>
    public KurrentProcessorHostBuilder WithFilter(string sqlExpression) {
        _settingsBuilder.WithFilterExpression(sqlExpression);
        return this;
    }

    /// <summary>
    /// Sets a programmatic filter for client-side filtering.
    /// </summary>
    /// <param name="filter">The filter to apply to records.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// For server-side filtering (more efficient), use <see cref="WithFilter(string)"/> instead.
    /// </para>
    /// </remarks>
    public KurrentProcessorHostBuilder WithFilter(IRecordFilter filter) {
        _settingsBuilder.WithFilter(filter);
        return this;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Nested Settings Configuration (Configure* pattern)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Configures checkpoint settings using a transformation function.
    /// </summary>
    /// <param name="configure">
    /// Function to transform checkpoint settings.
    /// Use <c>with</c> expression for immutable updates.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <example>
    /// <code>
    /// .ConfigureCheckpoint(c => c with {
    ///     Interval = 10_000,  // 10 seconds
    ///     Threshold = 100     // or every 100 records
    /// })
    /// </code>
    /// </example>
    public KurrentProcessorHostBuilder ConfigureCheckpointing(Func<CheckpointSettings, CheckpointSettings> configure) {
        _settingsBuilder.ConfigureCheckpointing(configure);
        return this;
    }

    /// <summary>
    /// Configures batching settings using a transformation function.
    /// </summary>
    /// <param name="configure">
    /// Function to transform batching settings.
    /// Use <c>with</c> expression for immutable updates.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <example>
    /// <code>
    /// .ConfigureBatching(b => b with {
    ///     MaxSize = 50,
    ///     Timeout = 1000
    /// })
    /// </code>
    /// </example>
    public KurrentProcessorHostBuilder ConfigureBatching(Func<BatchingSettings, BatchingSettings> configure) {
        _settingsBuilder.ConfigureBatching(configure);
        return this;
    }

    /// <summary>
    /// Configures subscription settings using a transformation function.
    /// </summary>
    /// <param name="configure">
    /// Function to transform subscription settings.
    /// Use <c>with</c> expression for immutable updates.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <example>
    /// <code>
    /// .ConfigureSubscription(s => s with {
    ///     InitialPosition = SubscriptionInitialPosition.Earliest,
    ///     PartitionKeySelector = "data.customer_id"
    /// })
    /// </code>
    /// </example>
    public KurrentProcessorHostBuilder ConfigureSubscription(Func<SubscriptionSettings, SubscriptionSettings> configure) {
        _settingsBuilder.ConfigureSubscription(configure);
        return this;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Direct Settings
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets the processor ID for checkpoint coordination.
    /// </summary>
    /// <param name="processorId">The processor group identifier.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// If not set, the processor ID is auto-generated from the processor type name
    /// (e.g., <c>MyProcessor</c> becomes <c>my_processor</c>).
    /// </para>
    /// </remarks>
    public KurrentProcessorHostBuilder WithProcessorId(string processorId) {
        _settingsBuilder.WithProcessorId(processorId);
        return this;
    }

    /// <summary>
    /// Sets the worker ID for this processor instance.
    /// </summary>
    /// <param name="workerId">The worker identifier.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Use a consistent identifier (like machine name) for sticky partition assignment.
    /// </para>
    /// </remarks>
    public KurrentProcessorHostBuilder WithWorkerId(string workerId) {
        _settingsBuilder.WithWorkerId(workerId);
        return this;
    }

    /// <summary>
    /// Sets the maximum parallelism for partition processing.
    /// </summary>
    /// <param name="maxParallelism">Maximum number of partitions (1-32768).</param>
    /// <returns>The builder instance for method chaining.</returns>
    public KurrentProcessorHostBuilder WithMaxParallelism(int maxParallelism) {
        _settingsBuilder.WithMaxParallelism(maxParallelism);
        return this;
    }

    /// <summary>
    /// Sets the buffer capacity for record buffering.
    /// </summary>
    /// <param name="capacity">Buffer size in records (default: 1000, max: 10000).</param>
    /// <returns>The builder instance for method chaining.</returns>
    public KurrentProcessorHostBuilder WithBufferCapacity(int capacity) {
        _settingsBuilder.WithBufferCapacity(capacity);
        return this;
    }

    /// <summary>
    /// Skips record deserialization for raw byte access.
    /// </summary>
    /// <param name="skip">True to skip decoding.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public KurrentProcessorHostBuilder WithSkipDecoding(bool skip = true) {
        _settingsBuilder.WithSkipDecoding(skip);
        return this;
    }

    /// <summary>
    /// Provides full control over processor settings.
    /// </summary>
    /// <param name="configure">Function to configure all processor settings.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public KurrentProcessorHostBuilder Configure(Func<ProcessorSettings, ProcessorSettings> configure) {
        _settingsBuilder.Configure(configure);
        return this;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Build
    // ═══════════════════════════════════════════════════════════════════════════════

    internal KurrentProcessorRegistration<TProcessor> Build<TProcessor>()
        where TProcessor : IRecordProcessor {
        return new KurrentProcessorRegistration<TProcessor>(
            _settingsBuilder.Settings,
            _connectionString,
            _clientConfigure,
            _useOwnClient
        );
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Additional Direct Settings (API parity with KurrentProcessorBuilder)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets the path to extract partition key from records.
    /// </summary>
    /// <param name="selector">Path expression (e.g., <c>"stream_id"</c>, <c>"data.customer_id"</c>).</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Required when <see cref="WithMaxParallelism"/> > 1.
    /// </para>
    /// </remarks>
    public KurrentProcessorHostBuilder WithPartitionKeySelector(string selector) {
        _settingsBuilder.WithPartitionKeySelector(selector);
        return this;
    }

    /// <summary>
    /// Sets the initial position for processing when no checkpoint exists.
    /// </summary>
    /// <param name="position">The initial position (Earliest or Latest).</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This setting only applies when there is no stored checkpoint.
    /// Use <see cref="WithStartPosition"/> to override any existing checkpoint.
    /// </para>
    /// </remarks>
    public KurrentProcessorHostBuilder WithInitialPosition(SubscriptionInitialPosition position) {
        _settingsBuilder.WithInitialPosition(position);
        return this;
    }

    /// <summary>
    /// Forces processing to start from a specific position, ignoring any checkpoint.
    /// </summary>
    /// <param name="position">The log position to start from.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// When set, any existing checkpoint will be ignored and processing will begin from
    /// the specified position.
    /// </para>
    /// </remarks>
    public KurrentProcessorHostBuilder WithStartPosition(long position) {
        _settingsBuilder.WithStartPosition(position);
        return this;
    }

    /// <summary>
    /// Configures record batching for processing.
    /// </summary>
    /// <param name="settings">The batching configuration.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Batching controls how records are grouped before delivery to the processor.
    /// Smaller batches reduce latency; larger batches improve throughput.
    /// </para>
    /// </remarks>
    public KurrentProcessorHostBuilder WithBatching(BatchingSettings settings) {
        _settingsBuilder.WithBatching(settings);
        return this;
    }

    /// <summary>
    /// Configures checkpoint settings for fault tolerance.
    /// </summary>
    /// <param name="settings">The checkpoint configuration.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Checkpoints enable recovery from failures by periodically saving progress.
    /// </para>
    /// </remarks>
    public KurrentProcessorHostBuilder WithCheckpoint(CheckpointSettings settings) {
        _settingsBuilder.WithCheckpoint(settings);
        return this;
    }
}
