using Kurrent.Client;
using Kurrent.Client.Streams;
using Kurrent.Processors.Models;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Processors.Options;

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
///     .ConfigureCheckpointing(c => c.Interval(TimeSpan.FromSeconds(10)))
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
    /// Configures checkpoint settings using a fluent builder.
    /// </summary>
    /// <param name="configure">Action to configure the checkpoint settings builder.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public KurrentProcessorHostBuilder ConfigureCheckpointing(Action<CheckpointSettingsBuilder> configure) {
        _settingsBuilder.ConfigureCheckpointing(configure);
        return this;
    }

    /// <summary>
    /// Configures batching settings using a fluent builder.
    /// </summary>
    /// <param name="configure">Action to configure the batching settings builder.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public KurrentProcessorHostBuilder ConfigureBatching(Action<BatchingSettingsBuilder> configure) {
        _settingsBuilder.ConfigureBatching(configure);
        return this;
    }

    /// <summary>
    /// Configures subscription settings using a fluent builder.
    /// </summary>
    /// <param name="configure">Action to configure the subscription settings builder.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public KurrentProcessorHostBuilder ConfigureSubscription(Action<SubscriptionSettingsBuilder> configure) {
        _settingsBuilder.ConfigureSubscription(configure);
        return this;
    }

    /// <summary>
    /// Configures worker settings using a fluent builder.
    /// </summary>
    /// <param name="configure">Action to configure the worker settings builder.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public KurrentProcessorHostBuilder ConfigureWorker(Action<WorkerSettingsBuilder> configure) {
        _settingsBuilder.ConfigureWorker(configure);
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
    /// </para>
    /// </remarks>
    public KurrentProcessorHostBuilder WithInitialPosition(SubscriptionInitialPosition position) {
        _settingsBuilder.WithInitialPosition(position);
        return this;
    }
}
